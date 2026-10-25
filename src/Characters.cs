// Characters and the pose images they are drawn with. A character is a folder of pose images, named as in
// Pet.Poses: <pose>.png for a still pose; <pose>_1.png, <pose>_2.png ... or <pose>.gif for an animated one. An
// optional character.txt in the folder says more, one "name = value" per line, # starting a comment line:
//   name = 海茵, name.en = Hein       the name in the settings page, per language as in effect.txt
//   credit = ...                     who made the character, shown under the picker
//   width = 104, height = 168        the box every pose is scaled to fit, in CSS px; float.width for the float pose
//   sleep.z = off                    no Zs drift up from her head in the idle pose
//   idle.frames = 500, 300, 700      how long each numbered frame of a pose stays, in ms (one number: every frame)
// The built-in characters are the folders in characters\ next to the exe; custom images use the same layout.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

// one pose: a single frame, or the frames of an animation with how long each one stays
sealed class PoseImage : IDisposable
{
    public readonly Bitmap[] Frames;
    public readonly double[] Delays;        // seconds
    public readonly double Total;

    public PoseImage(Bitmap[] frames, double[] delays)
    {
        Frames = frames;
        Delays = delays;
        foreach (double d in delays) Total += d;
    }

    public static PoseImage Still(Bitmap bmp)
    {
        return new PoseImage(new[] { bmp }, new[] { 1.0 });
    }

    public bool Animated { get { return Frames.Length > 1; } }

    // the frame showing t seconds after the pose started; animations loop
    public int FrameAt(double t)
    {
        if (!Animated) return 0;
        t %= Total;
        for (int i = 0; i < Frames.Length; i++)
        {
            t -= Delays[i];
            if (t < 0) return i;
        }
        return Frames.Length - 1;
    }

    public void Dispose()
    {
        foreach (Bitmap b in Frames) b.Dispose();
    }
}

sealed class CharacterInfo
{
    public const string FileName = "character.txt";
    public const string DefaultId = "hein";
    const double FrameDelay = 0.15;          // a numbered frame without its own time in character.txt

    public readonly string Id, Dir;
    readonly Dictionary<string, string> values = new Dictionary<string, string>();

    CharacterInfo(string dir, string[] lines)
    {
        Dir = dir;
        Id = dir == null ? null : Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            int eq = line.IndexOf('=');
            if (line.StartsWith("#") || eq <= 0) continue;
            values[line.Substring(0, eq).Trim().ToLowerInvariant()] = line.Substring(eq + 1).Trim();
        }
    }

    public static CharacterInfo Load(string dir)
    {
        string[] lines = new string[0];
        try
        {
            string file = Path.Combine(dir, FileName);
            if (File.Exists(file)) lines = File.ReadAllLines(file, Encoding.UTF8);
        }
        catch (Exception) { }               // an unreadable file counts as empty
        return new CharacterInfo(dir, lines);
    }

    // the text of a character.txt that is not in a folder (one inside a pack); null reads as empty
    public static CharacterInfo Parse(string text)
    {
        return new CharacterInfo(null, (text ?? "").Split('\n'));
    }

    public static string BuiltinDir { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "characters"); } }

    // every built-in character with a default.png, Hein first
    public static List<CharacterInfo> All()
    {
        var list = new List<CharacterInfo>();
        if (Directory.Exists(BuiltinDir))
            foreach (string dir in Directory.GetDirectories(BuiltinDir))
                if (PoseFiles.Exists(dir, "default")) list.Add(Load(dir));
        list.Sort((a, b) => a.Id == DefaultId ? -1 : b.Id == DefaultId ? 1 : string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
        return list;
    }

    // the character with this id, else Hein, else the first one there is
    public static CharacterInfo Find(string id)
    {
        List<CharacterInfo> all = All();
        CharacterInfo hein = null;
        foreach (CharacterInfo c in all)
        {
            if (string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) return c;
            if (c.Id == DefaultId) hein = c;
        }
        if (hein != null) return hein;
        if (all.Count > 0) return all[0];
        throw new FileNotFoundException("No character with a default.png in " + BuiltinDir);
    }

    string Local(string key)
    {
        string s;
        if (values.TryGetValue(key + "." + Lang.Code.ToLowerInvariant(), out s) && s.Length > 0) return s;
        return values.TryGetValue(key, out s) ? s : null;
    }

    public string Name { get { return Local("name") ?? Id ?? ""; } }
    public string Credit { get { return Local("credit"); } }
    public bool SleepZ { get { string s; return !values.TryGetValue("sleep.z", out s) || s.ToLowerInvariant() != "off"; } }
    public double Width { get { return Number("width", 104); } }
    public double Height { get { return Number("height", 168); } }
    public double FloatWidth { get { return Number("float.width", 200); } }

    double Number(string key, double fallback)
    {
        string s;
        double v;
        if (values.TryGetValue(key, out s) && double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v) && v >= 20 && v <= 600)
            return v;
        return fallback;
    }

    // seconds for each of count numbered frames of a pose
    public double[] Delays(string pose, int count)
    {
        var ms = new List<double>();
        string s;
        if (values.TryGetValue(pose + ".frames", out s))
            foreach (string part in s.Split(','))
            {
                double v;
                if (double.TryParse(part.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v))
                    ms.Add(Math.Max(20, Math.Min(10000, v)) / 1000);
            }
        var delays = new double[count];
        for (int i = 0; i < count; i++)
            delays[i] = ms.Count == 0 ? FrameDelay : ms[Math.Min(i, ms.Count - 1)];
        return delays;
    }

    // writes "<pose>.frames = ..." into dir's character.txt, replacing the old line; null removes it
    public static void WriteDelays(string dir, string pose, double[] seconds)
    {
        string file = Path.Combine(dir, FileName);
        var lines = new List<string>();
        if (File.Exists(file)) lines.AddRange(File.ReadAllLines(file, Encoding.UTF8));
        string key = pose + ".frames";
        lines.RemoveAll(l =>
        {
            int eq = l.IndexOf('=');
            return eq > 0 && l.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase);
        });
        if (seconds != null)
        {
            var ms = new List<string>();
            foreach (double d in seconds) ms.Add(((int)Math.Round(d * 1000)).ToString());
            lines.Add(key + " = " + string.Join(", ", ms));
        }
        if (lines.Count == 0) File.Delete(file);
        else File.WriteAllLines(file, lines, new UTF8Encoding(false));
    }
}

static class PoseFiles
{
    // the pose in a character folder, or null when it has none (or its files are broken)
    public static PoseImage Read(string dir, string pose, CharacterInfo info)
    {
        try
        {
            List<string> numbered = Numbered(dir, pose);
            if (numbered.Count > 0)
            {
                var frames = new List<Bitmap>();
                try
                {
                    foreach (string f in numbered) frames.Add(Pet.ReadBitmap(f));
                }
                catch (Exception)
                {
                    foreach (Bitmap b in frames) b.Dispose();
                    throw;
                }
                return new PoseImage(frames.ToArray(), info.Delays(pose, frames.Count));
            }
            string gif = Path.Combine(dir, pose + ".gif");
            if (File.Exists(gif)) return ReadFile(gif);
            string png = Path.Combine(dir, pose + ".png");
            if (File.Exists(png)) return PoseImage.Still(Pet.ReadBitmap(png));
        }
        catch (Exception) { }               // a broken file counts as missing
        return null;
    }

    public static bool Exists(string dir, string pose)
    {
        return File.Exists(Path.Combine(dir, pose + ".png")) || File.Exists(Path.Combine(dir, pose + ".gif"))
            || File.Exists(Path.Combine(dir, pose + "_1.png"));
    }

    // removes every file of the pose, and its frame times
    public static void Delete(string dir, string pose)
    {
        if (!Directory.Exists(dir)) return;
        File.Delete(Path.Combine(dir, pose + ".png"));
        File.Delete(Path.Combine(dir, pose + ".gif"));
        foreach (string f in Numbered(dir, pose)) File.Delete(f);
        CharacterInfo.WriteDelays(dir, pose, null);
    }

    // <pose>_1.png, <pose>_2.png ... up to the first number missing
    public static List<string> Numbered(string dir, string pose)
    {
        var files = new List<string>();
        for (int i = 1; ; i++)
        {
            string f = Path.Combine(dir, pose + "_" + i + ".png");
            if (!File.Exists(f)) break;
            files.Add(f);
        }
        return files;
    }

    // one picture file: every frame of an animated GIF, otherwise a single frame
    public static PoseImage ReadFile(string path)
    {
        using (var ms = new MemoryStream(File.ReadAllBytes(path)))
            return Decode(ms);
    }

    public static PoseImage Decode(Stream stream)
    {
        using (Image img = Image.FromStream(stream))
        {
            int count = 1;
            if (Array.IndexOf(img.FrameDimensionsList, FrameDimension.Time.Guid) >= 0)
                count = img.GetFrameCount(FrameDimension.Time);
            if (count <= 1) return PoseImage.Still(new Bitmap(img));
            byte[] times = null;
            try { times = img.GetPropertyItem(0x5100 /* PropertyTagFrameDelay, 1/100 s per frame */).Value; }
            catch (Exception) { }
            var frames = new Bitmap[count];
            var delays = new double[count];
            for (int i = 0; i < count; i++)
            {
                img.SelectActiveFrame(FrameDimension.Time, i);
                frames[i] = new Bitmap(img);
                int cs = times != null && times.Length >= 4 * (i + 1) ? BitConverter.ToInt32(times, 4 * i) : 10;
                delays[i] = (cs <= 1 ? 10 : cs) / 100.0;  // browsers also play 0 and 1 as 1/10 s
            }
            return new PoseImage(frames, delays);
        }
    }
}

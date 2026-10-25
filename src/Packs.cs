// Image packs: a .zip holding a character's pose images (<pose>.png, <pose>.gif or <pose>_1.png, <pose>_2.png ...
// with pose names as in Pet.Poses) and its character.txt, for sharing a character, and optionally the click
// effect in an effect\ folder. Only entries whose file name is a known pose are read as pose images; other
// folders inside the zip are ignored.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

static class Packs
{
    const long MaxEntryBytes = 20 * 1024 * 1024;    // a pose image is never near this; refuse anything bigger

    // writes every custom pose with its character.txt, and the click effect from effectFolder unless it is null,
    // into a new zip; returns how many poses were written
    public static int Export(string zipPath, string effectFolder)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        int count = 0;
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (string pose in Pet.Poses)
            {
                var files = PoseFiles.Numbered(Pet.CustomDir, pose);
                string still = Path.Combine(Pet.CustomDir, pose + ".png");
                if (files.Count == 0 && File.Exists(still)) files.Add(still);
                foreach (string file in files) zip.CreateEntryFromFile(file, Path.GetFileName(file));
                if (files.Count > 0) count++;
            }
            string info = Path.Combine(Pet.CustomDir, CharacterInfo.FileName);
            if (count > 0 && File.Exists(info)) zip.CreateEntryFromFile(info, CharacterInfo.FileName);
            if (effectFolder != null)
                foreach (string file in Directory.GetFiles(effectFolder))
                    if (IsEffectFile(Path.GetFileName(file)))
                        zip.CreateEntryFromFile(file, "effect/" + Path.GetFileName(file));
        }
        return count;
    }

    // ---- click effects ----

    const long MaxEffectBytes = 5 * 1024 * 1024;

    static bool IsEffectFile(string name)
    {
        return name.Equals("effect.txt", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }

    // Installs the effect inside a zip (the effect.txt nearest the top, with the pictures beside it) as a user
    // effect named after the zip. Returns its id, or null with error unset when the zip has no effect, or
    // null with error set when it is broken.
    public static string InstallEffect(string zipPath, out string error)
    {
        error = null;
        string target = null;
        try
        {
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                ZipArchiveEntry main = null;
                foreach (ZipArchiveEntry entry in zip.Entries)
                    if (entry.Name.Equals("effect.txt", StringComparison.OrdinalIgnoreCase)
                        && (main == null || entry.FullName.Length < main.FullName.Length))
                        main = entry;
                if (main == null) return null;
                string prefix = main.FullName.Substring(0, main.FullName.Length - main.Name.Length);
                string id = SafeId(Path.GetFileNameWithoutExtension(zipPath));
                Directory.CreateDirectory(EffectDef.UserDir);
                string unique = id;
                for (int k = 2; Directory.Exists(Path.Combine(EffectDef.UserDir, unique)); k++) unique = id + "-" + k;
                target = Path.Combine(EffectDef.UserDir, unique);
                Directory.CreateDirectory(target);
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    // only files sitting right beside effect.txt, by their plain file name
                    if (entry.FullName != prefix + entry.Name || !IsEffectFile(entry.Name) || entry.Length > MaxEffectBytes) continue;
                    entry.ExtractToFile(Path.Combine(target, Path.GetFileName(entry.Name)), true);
                }
                EffectDef.Load(target).Dispose();      // refuse an effect that does not load
                return unique;
            }
        }
        catch (Exception e)
        {
            if (target != null && Directory.Exists(target)) Directory.Delete(target, true);
            error = e.Message;
            return null;
        }
    }

    // a folder name made of letters, digits, - and _
    static string SafeId(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char ch in name) sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '-');
        string id = sb.ToString().Trim('-');
        return id.Length == 0 ? "effect" : id;
    }

    const int MaxFrames = 120;

    // Reads the poses in a zip, with frame times from its character.txt; the other lines of that file come back
    // in info (null when there is none). Returns null when the zip cannot be opened.
    public static Dictionary<string, PoseImage> Read(string zipPath, out string info)
    {
        info = null;
        var images = new Dictionary<string, PoseImage>();
        try
        {
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                var still = new Dictionary<string, ZipArchiveEntry>();
                var numbered = new Dictionary<string, SortedDictionary<int, ZipArchiveEntry>>();
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (entry.FullName.StartsWith("effect/", StringComparison.OrdinalIgnoreCase)) continue;   // effect pictures
                    if (entry.Length > MaxEntryBytes) continue;
                    if (entry.Name.Equals(CharacterInfo.FileName, StringComparison.OrdinalIgnoreCase))
                    {
                        using (var r = new StreamReader(entry.Open(), System.Text.Encoding.UTF8)) info = r.ReadToEnd();
                        continue;
                    }
                    string name = Path.GetFileNameWithoutExtension(entry.Name).ToLowerInvariant();
                    string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                    if (ext != ".png" && ext != ".gif") continue;
                    if (Array.IndexOf(Pet.Poses, name) >= 0)
                    {
                        if (!still.ContainsKey(name) || ext == ".gif") still[name] = entry;
                        continue;
                    }
                    int cut = name.LastIndexOf('_'), n;
                    if (ext == ".png" && cut > 0 && int.TryParse(name.Substring(cut + 1), out n) && n >= 1 && n <= MaxFrames
                        && Array.IndexOf(Pet.Poses, name.Substring(0, cut)) >= 0)
                    {
                        string pose = name.Substring(0, cut);
                        if (!numbered.ContainsKey(pose)) numbered[pose] = new SortedDictionary<int, ZipArchiveEntry>();
                        numbered[pose][n] = entry;
                    }
                }
                CharacterInfo times = CharacterInfo.Parse(info);
                foreach (var pair in numbered)
                {
                    var frames = new List<Bitmap>();
                    foreach (ZipArchiveEntry e in pair.Value.Values)
                    {
                        PoseImage one = Decode(e);
                        if (one == null) continue;  // a broken frame is skipped
                        frames.Add(one.Frames[0]);
                        for (int i = 1; i < one.Frames.Length; i++) one.Frames[i].Dispose();
                    }
                    if (frames.Count > 0) images[pair.Key] = new PoseImage(frames.ToArray(), times.Delays(pair.Key, frames.Count));
                }
                foreach (var pair in still)
                {
                    if (images.ContainsKey(pair.Key)) continue;
                    PoseImage img = Decode(pair.Value);
                    if (img != null) images[pair.Key] = img;
                }
            }
        }
        catch (Exception)
        {
            foreach (PoseImage p in images.Values) p.Dispose();
            return null;
        }
        return images;
    }

    static PoseImage Decode(ZipArchiveEntry entry)
    {
        try
        {
            using (Stream s = entry.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                ms.Position = 0;
                return PoseFiles.Decode(ms);
            }
        }
        catch (Exception) { return null; }
    }

    // replaces all custom images with these; info is the pack's character.txt (its frame times are rewritten
    // for the frames as stored)
    public static void Install(Dictionary<string, PoseImage> images, string info)
    {
        Directory.CreateDirectory(Pet.CustomDir);
        foreach (string pose in Pet.Poses) PoseFiles.Delete(Pet.CustomDir, pose);
        string file = Path.Combine(Pet.CustomDir, CharacterInfo.FileName);
        if (info != null) File.WriteAllText(file, info, new System.Text.UTF8Encoding(false));
        else File.Delete(file);
        foreach (var pair in images) Save(pair.Value, pair.Key);
    }

    // Stores an image for a pose: the transparent margin is cut off (the pet is scaled by the image size, so a
    // wide empty border would make her tiny) and the longest side is shrunk to at most 640 px. The frames of an
    // animation are cut and shrunk alike, so they stay lined up, and saved as <pose>_1.png, <pose>_2.png ...
    // with their times in character.txt.
    public static void Save(PoseImage src, string pose)
    {
        Directory.CreateDirectory(Pet.CustomDir);
        PoseFiles.Delete(Pet.CustomDir, pose);
        Rectangle crop = Opaque(src.Frames[0]);
        foreach (Bitmap frame in src.Frames) crop = Rectangle.Union(crop, Opaque(frame));
        double f = Math.Min(1, 640.0 / Math.Max(crop.Width, crop.Height));
        int w = Math.Max(1, (int)Math.Round(crop.Width * f)), h = Math.Max(1, (int)Math.Round(crop.Height * f));
        for (int i = 0; i < src.Frames.Length; i++)
            using (var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(dst))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(src.Frames[i], new Rectangle(0, 0, w, h), crop, GraphicsUnit.Pixel);
                }
                ClearAlphaLowBit(dst);
                string name = src.Animated ? pose + "_" + (i + 1) : pose;
                dst.Save(Path.Combine(Pet.CustomDir, name + ".png"), ImageFormat.Png);
            }
        if (src.Animated) CharacterInfo.WriteDelays(Pet.CustomDir, pose, src.Delays);
    }

    // NovelAI hides the generation prompt in the lowest bit of each alpha value; an image stored at its own size
    // keeps those bits, and a shared pack would carry the prompt along. Clearing them (as the Aaalice NAI Launcher
    // does before sharing) changes alpha by at most 1/255. Saving re-encodes the pixels, so text chunks are gone too.
    static void ClearAlphaLowBit(Bitmap bmp)
    {
        BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var pixels = new byte[d.Stride * bmp.Height];
        Marshal.Copy(d.Scan0, pixels, 0, pixels.Length);
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++) pixels[y * d.Stride + x * 4 + 3] &= 0xFE;
        Marshal.Copy(pixels, 0, d.Scan0, pixels.Length);
        bmp.UnlockBits(d);
    }

    public static bool HasTransparency(Bitmap bmp)
    {
        int stride;
        byte[] pixels = Pixels(bmp, out stride);
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
                if (pixels[y * stride + x * 4 + 3] < 250) return true;
        return false;
    }

    // the smallest rectangle holding every pixel that is not almost fully transparent; the whole image if none
    static Rectangle Opaque(Bitmap bmp)
    {
        int stride;
        byte[] pixels = Pixels(bmp, out stride);
        int left = bmp.Width, top = bmp.Height, right = -1, bottom = -1;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
                if (pixels[y * stride + x * 4 + 3] > 8)
                {
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                }
        return right < 0 ? new Rectangle(0, 0, bmp.Width, bmp.Height)
                         : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    static byte[] Pixels(Bitmap bmp, out int stride)
    {
        BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[d.Stride * bmp.Height];
        Marshal.Copy(d.Scan0, pixels, 0, pixels.Length);
        bmp.UnlockBits(d);
        stride = d.Stride;
        return pixels;
    }
}

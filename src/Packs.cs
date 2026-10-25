// Image packs: a .zip holding <pose>.png files (pose names as in Pet.Poses), for sharing a character, and
// optionally the click effect in an effect\ folder. Only entries whose file name is a known pose are read as
// pose images; other folders inside the zip are ignored.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

static class Packs
{
    const long MaxEntryBytes = 20 * 1024 * 1024;    // a pose image is never near this; refuse anything bigger

    // writes every custom image, and the click effect from effectFolder unless it is null, into a new zip;
    // returns how many images were written
    public static int Export(string zipPath, string effectFolder)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        int count = 0;
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (string pose in Pet.Poses)
            {
                string file = Pet.CustomPath(pose);
                if (!File.Exists(file)) continue;
                zip.CreateEntryFromFile(file, pose + ".png");
                count++;
            }
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

    // reads the pose images in a zip; returns null when the zip cannot be opened
    public static System.Collections.Generic.Dictionary<string, Bitmap> Read(string zipPath)
    {
        var images = new System.Collections.Generic.Dictionary<string, Bitmap>();
        try
        {
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (entry.FullName.StartsWith("effect/", StringComparison.OrdinalIgnoreCase)) continue;   // effect pictures
                    string pose = Path.GetFileNameWithoutExtension(entry.Name).ToLowerInvariant();
                    if (!entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || Array.IndexOf(Pet.Poses, pose) < 0
                        || entry.Length > MaxEntryBytes || images.ContainsKey(pose)) continue;
                    try
                    {
                        using (Stream s = entry.Open())
                        using (var ms = new MemoryStream())
                        {
                            s.CopyTo(ms);
                            ms.Position = 0;
                            using (Image img = Image.FromStream(ms)) images[pose] = new Bitmap(img);
                        }
                    }
                    catch (Exception) { }   // a broken image is skipped
                }
        }
        catch (Exception)
        {
            foreach (Bitmap b in images.Values) b.Dispose();
            return null;
        }
        return images;
    }

    // replaces all custom images with these
    public static void Install(System.Collections.Generic.Dictionary<string, Bitmap> images)
    {
        Directory.CreateDirectory(Pet.CustomDir);
        foreach (string pose in Pet.Poses) File.Delete(Pet.CustomPath(pose));
        foreach (var pair in images) Save(pair.Value, pair.Key);
    }

    // Stores an image for a pose: the transparent margin is cut off (the pet is scaled by the image size, so a
    // wide empty border would make her tiny) and the longest side is shrunk to at most 640 px.
    public static void Save(Bitmap src, string pose)
    {
        Directory.CreateDirectory(Pet.CustomDir);
        Rectangle crop = Opaque(src);
        double f = Math.Min(1, 640.0 / Math.Max(crop.Width, crop.Height));
        int w = Math.Max(1, (int)Math.Round(crop.Width * f)), h = Math.Max(1, (int)Math.Round(crop.Height * f));
        using (var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h), crop, GraphicsUnit.Pixel);
            }
            ClearAlphaLowBit(dst);
            dst.Save(Pet.CustomPath(pose), ImageFormat.Png);
        }
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

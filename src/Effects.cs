// Click effects written as text files: effects\<id>\effect.txt next to the exe for the built-in ones and
// %LOCALAPPDATA%\MomPet\effects\<id>\effect.txt for the user's own. Each line is "key = value"; positions,
// sizes, turning and fading are formulas over the time since the click, so a new effect needs no code.
// Formulas can only compute numbers, which keeps shared effects safe. docs\點擊特效說明.md describes the format.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

// The numbers a formula can read while one ring or particle is being drawn.
sealed class EffectContext
{
    public double T, Time, I, N, Angle;
    public int Seed;                        // random per click, so every burst differs

    // a stable random number in 0..1 for this click, particle and slot
    public double Random(int slot)
    {
        unchecked
        {
            uint h = (uint)Seed * 2654435761u ^ (uint)(I + 1) * 2246822519u ^ (uint)(slot + 1) * 3266489917u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
            return h / 4294967296.0;
        }
    }
}

delegate double Formula(EffectContext c);

sealed class EffectException : Exception
{
    public EffectException(string message) : base(message) { }
}

// Turns "cos(angle) * (26 + 14 * r1) * ease(t)" into a Formula. Operators: + - * / % ^ and parentheses.
sealed class FormulaParser
{
    readonly string s;
    int pos;
    readonly Func<int> nextSlot;            // each random() call gets its own slot, shared across one effect

    FormulaParser(string text, Func<int> nextSlot)
    {
        s = text;
        this.nextSlot = nextSlot;
    }

    public static Formula Parse(string text, Func<int> nextSlot)
    {
        var p = new FormulaParser(text, nextSlot);
        Formula f = p.Sum();
        p.Space();
        if (p.pos < p.s.Length) throw new EffectException(Lang.T("fx.err.syntax", p.s.Substring(p.pos)));
        return f;
    }

    void Space() { while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++; }

    bool Take(char c)
    {
        Space();
        if (pos < s.Length && s[pos] == c) { pos++; return true; }
        return false;
    }

    Formula Sum()
    {
        Formula left = Product();
        while (true)
        {
            Formula a = left, b;
            if (Take('+')) { b = Product(); left = c => a(c) + b(c); }
            else if (Take('-')) { b = Product(); left = c => a(c) - b(c); }
            else return left;
        }
    }

    Formula Product()
    {
        Formula left = Unary();
        while (true)
        {
            Formula a = left, b;
            if (Take('*')) { b = Unary(); left = c => a(c) * b(c); }
            else if (Take('/')) { b = Unary(); left = c => { double d = b(c); return d == 0 ? 0 : a(c) / d; }; }
            else if (Take('%')) { b = Unary(); left = c => { double d = b(c); return d == 0 ? 0 : a(c) % d; }; }
            else return left;
        }
    }

    Formula Unary()
    {
        if (Take('-')) { Formula a = Unary(); return c => -a(c); }
        if (Take('+')) return Unary();
        Formula b = Atom();
        if (Take('^')) { Formula e = Unary(); return c => Math.Pow(b(c), e(c)); }
        return b;
    }

    Formula Atom()
    {
        Space();
        if (pos >= s.Length) throw new EffectException(Lang.T("fx.err.end"));
        if (Take('('))
        {
            Formula inner = Sum();
            if (!Take(')')) throw new EffectException(Lang.T("fx.err.paren"));
            return inner;
        }
        char ch = s[pos];
        if (char.IsDigit(ch) || ch == '.')
        {
            int start = pos;
            while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.')) pos++;
            double v;
            if (!double.TryParse(s.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                throw new EffectException(Lang.T("fx.err.syntax", s.Substring(start)));
            return c => v;
        }
        if (char.IsLetter(ch) || ch == '_')
        {
            int start = pos;
            while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_')) pos++;
            string name = s.Substring(start, pos - start).ToLowerInvariant();
            if (Take('(')) return Call(name);
            return Variable(name);
        }
        throw new EffectException(Lang.T("fx.err.syntax", s.Substring(pos)));
    }

    Formula Variable(string name)
    {
        switch (name)
        {
            case "t": return c => c.T;
            case "time": return c => c.Time;
            case "i": return c => c.I;
            case "n": return c => c.N;
            case "angle": return c => c.Angle;
            case "r1": return c => c.Random(0);
            case "r2": return c => c.Random(1);
            case "r3": return c => c.Random(2);
            case "pi": return c => Math.PI;
        }
        throw new EffectException(Lang.T("fx.err.name", name));
    }

    Formula Call(string name)
    {
        var args = new List<Formula>();
        if (!Take(')'))
        {
            do args.Add(Sum()); while (Take(','));
            if (!Take(')')) throw new EffectException(Lang.T("fx.err.paren"));
        }
        Formula[] a = args.ToArray();
        Func<int, bool> need = count =>
        {
            if (a.Length != count) throw new EffectException(Lang.T("fx.err.args", name, count));
            return true;
        };
        switch (name)
        {
            case "sin": need(1); return c => Math.Sin(a[0](c));
            case "cos": need(1); return c => Math.Cos(a[0](c));
            case "tan": need(1); return c => Math.Tan(a[0](c));
            case "abs": need(1); return c => Math.Abs(a[0](c));
            case "sqrt": need(1); return c => Math.Sqrt(Math.Max(0, a[0](c)));
            case "floor": need(1); return c => Math.Floor(a[0](c));
            case "round": need(1); return c => Math.Round(a[0](c));
            case "pow": need(2); return c => Math.Pow(a[0](c), a[1](c));
            case "min": need(2); return c => Math.Min(a[0](c), a[1](c));
            case "max": need(2); return c => Math.Max(a[0](c), a[1](c));
            case "clamp": need(3); return c => Math.Max(a[1](c), Math.Min(a[2](c), a[0](c)));
            case "lerp": need(3); return c => a[0](c) + (a[1](c) - a[0](c)) * a[2](c);
            case "step": need(2); return c => a[1](c) >= a[0](c) ? 1 : 0;
            // ease: fast start, slow finish, the curve the default effect flies with
            case "ease": need(1); return c => Ease.Bezier(.2, .7, .4, 1, a[0](c));
            case "easeout": need(1); return c => Ease.Bezier(0, 0, .58, 1, a[0](c));
            case "easein": need(1); return c => Ease.Bezier(.42, 0, 1, 1, a[0](c));
            case "bezier": need(5); return c => Ease.Bezier(a[0](c), a[1](c), a[2](c), a[3](c), a[4](c));
            // fade(t, start): 1 until start, then eases down to 0 at t = 1
            case "fade": need(2); return c =>
            {
                double u = a[0](c), st = a[1](c);
                return u <= st ? 1 : 1 - Ease.Bezier(.2, .7, .4, 1, (u - st) / Math.Max(1e-6, 1 - st));
            };
            case "random":
            {
                need(2);
                int slot = nextSlot();
                return c => a[0](c) + (a[1](c) - a[0](c)) * c.Random(slot);
            }
        }
        throw new EffectException(Lang.T("fx.err.name", name));
    }
}

// One kind of particle: a drawn star, heart or dot in a colour, or a picture from the effect's folder.
sealed class ParticleKind
{
    public const int Star = 0, Heart = 1, Dot = 2, Picture = 3;
    public int Shape;
    public Color Color;
    public Bitmap Image;
}

sealed class EffectDef
{
    public const string Off = "off", DefaultId = "stars-hearts";
    public const int MaxParticles = 40;

    public string Id, Name, Folder;
    public double Duration = 0.6, Size = 140;
    public bool Ring = true;
    public Color RingColor = Color.FromArgb(0x8b, 0x1e, 0x3f), EdgeColor = Color.White;
    public Formula RingRadius, RingWidth, RingEdge, RingScale, RingAlpha;
    public int Count = 6;
    public Formula X, Y, Scale, Spin, Alpha;
    public readonly List<ParticleKind> Kinds = new List<ParticleKind>();

    public static string BuiltinDir { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "effects"); } }
    public static string UserDir { get { return Path.Combine(Path.GetDirectoryName(Pet.CustomDir), "effects"); } }

    // every effect as (id, folder); a user effect replaces a built-in one with the same id
    public static SortedDictionary<string, string> All()
    {
        var all = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in new[] { BuiltinDir, UserDir })
            if (Directory.Exists(root))
                foreach (string dir in Directory.GetDirectories(root))
                    if (File.Exists(Path.Combine(dir, "effect.txt"))) all[Path.GetFileName(dir)] = dir;
        return all;
    }

    // the display name written in an effect's file: name.<language code> if given, else name, else the folder name
    public static string NameOf(string folder)
    {
        string plain = null, local = null, wanted = "name." + Lang.Code.ToLowerInvariant();
        try
        {
            foreach (string raw in File.ReadAllLines(Path.Combine(folder, "effect.txt"), Encoding.UTF8))
            {
                string line = StripComment(raw).Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                if (key == "name") plain = line.Substring(eq + 1).Trim();
                else if (key == wanted) local = line.Substring(eq + 1).Trim();
            }
        }
        catch (Exception) { }
        return local ?? plain ?? Path.GetFileName(folder);
    }

    // reads an effect; throws EffectException with the line number when something is wrong
    public static EffectDef Load(string folder)
    {
        var e = new EffectDef { Id = Path.GetFileName(folder), Name = Path.GetFileName(folder), Folder = folder };
        int slots = 3;                      // r1..r3 use slots 0..2
        Func<int> nextSlot = () => slots++;
        var formulas = new Dictionary<string, Formula>();
        string[] lines = File.ReadAllLines(Path.Combine(folder, "effect.txt"), Encoding.UTF8);
        for (int n = 0; n < lines.Length; n++)
        {
            string line = StripComment(lines[n]).Trim();
            if (line.Length == 0) continue;
            int eq = line.IndexOf('=');
            if (eq < 0) throw Fail(n, Lang.T("fx.err.line"));
            string key = line.Substring(0, eq).Trim().ToLowerInvariant(), value = line.Substring(eq + 1).Trim();
            try
            {
                Apply(e, key, value, formulas, nextSlot);
            }
            catch (EffectException ex)
            {
                throw Fail(n, ex.Message);
            }
        }
        Formula f;
        e.RingRadius = formulas.TryGetValue("ring.radius", out f) ? f : Const(18.5);
        e.RingWidth = formulas.TryGetValue("ring.width", out f) ? f : Const(3);
        e.RingEdge = formulas.TryGetValue("ring.edge.width", out f) ? f : Const(2);
        e.RingScale = formulas.TryGetValue("ring.scale", out f) ? f : Default("0.2 + 1.1 * easeout(time / 0.45)", nextSlot);
        e.RingAlpha = formulas.TryGetValue("ring.alpha", out f) ? f : Default("1 - easeout(time / 0.45)", nextSlot);
        e.X = formulas.TryGetValue("particle.x", out f) ? f : Default("cos(angle) * (26 + 14 * r1) * ease(t)", nextSlot);
        e.Y = formulas.TryGetValue("particle.y", out f) ? f : Default("(sin(angle) * (26 + 14 * r1) + 14) * ease(t)", nextSlot);
        e.Scale = formulas.TryGetValue("particle.scale", out f) ? f : Default("0.4 + 0.6 * ease(t)", nextSlot);
        e.Spin = formulas.TryGetValue("particle.spin", out f) ? f : Default("(r2 - 0.5) * 120 * ease(t)", nextSlot);
        e.Alpha = formulas.TryGetValue("particle.alpha", out f) ? f : Default("fade(t, 0.7)", nextSlot);
        if (e.Kinds.Count == 0)
        {
            e.Kinds.Add(new ParticleKind { Shape = ParticleKind.Star, Color = Color.FromArgb(0xe8, 0xb8, 0x4a) });
            e.Kinds.Add(new ParticleKind { Shape = ParticleKind.Heart, Color = Color.FromArgb(0xb8, 0x26, 0x4f) });
        }
        return e;
    }

    // a # starts a comment, except in a colour like #8B1E3F (exactly six hex digits)
    static string StripComment(string line)
    {
        for (int k = 0; k < line.Length; k++)
        {
            if (line[k] != '#') continue;
            bool colour = k + 7 <= line.Length && IsColour(line.Substring(k, 7))
                          && (k + 7 == line.Length || !Uri.IsHexDigit(line[k + 7]));
            if (!colour) return line.Substring(0, k);
        }
        return line;
    }

    static bool IsColour(string s)
    {
        if (s.Length != 7 || s[0] != '#') return false;
        for (int k = 1; k < 7; k++) if (!Uri.IsHexDigit(s[k])) return false;
        return true;
    }

    static EffectException Fail(int line, string message)
    {
        return new EffectException(Lang.T("fx.err.at", line + 1, message));
    }

    static Formula Const(double v) { return c => v; }

    static Formula Default(string text, Func<int> nextSlot) { return FormulaParser.Parse(text, nextSlot); }

    static void Apply(EffectDef e, string key, string value, Dictionary<string, Formula> formulas, Func<int> nextSlot)
    {
        switch (key)
        {
            case "name": e.Name = value; return;
            case "time": e.Duration = Clamp(Number(value), 0.05, 10); return;
            case "size": e.Size = Clamp(Number(value), 20, 800); return;
            case "ring": e.Ring = value.ToLowerInvariant() != "off"; return;
            case "ring.color": e.RingColor = Colour(value); return;
            case "ring.edge": e.EdgeColor = Colour(value); return;
            case "particles": e.Count = (int)Clamp(Number(value), 0, MaxParticles); return;
            case "ring.radius": case "ring.width": case "ring.edge.width": case "ring.scale": case "ring.alpha":
            case "particle.x": case "particle.y": case "particle.scale": case "particle.spin": case "particle.alpha":
                formulas[key] = FormulaParser.Parse(value, nextSlot);
                return;
        }
        if (key.StartsWith("name.")) return;   // a name in another language, read by NameOf
        if (key.StartsWith("particle.") && key.Length > 9 && char.IsDigit(key[9]))
        {
            e.Kinds.Add(Kind(e, value));
            return;
        }
        throw new EffectException(Lang.T("fx.err.key", key));
    }

    // "star #E8B84A", "heart #B8264F", "dot #FFFFFF" or "image sakura.png"
    static ParticleKind Kind(EffectDef e, string value)
    {
        string[] parts = value.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
        string shape = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        string arg = parts.Length > 1 ? parts[1].Trim() : "";
        switch (shape)
        {
            case "star": return new ParticleKind { Shape = ParticleKind.Star, Color = arg == "" ? Color.FromArgb(0xe8, 0xb8, 0x4a) : Colour(arg) };
            case "heart": return new ParticleKind { Shape = ParticleKind.Heart, Color = arg == "" ? Color.FromArgb(0xb8, 0x26, 0x4f) : Colour(arg) };
            case "dot": return new ParticleKind { Shape = ParticleKind.Dot, Color = arg == "" ? Color.White : Colour(arg) };
            case "image":
                // only a file name in the effect's own folder
                string file = Path.GetFileName(arg);
                string path = Path.Combine(e.Folder, file);
                if (file == "" || file != arg || !File.Exists(path)) throw new EffectException(Lang.T("fx.err.image", arg));
                try { return new ParticleKind { Shape = ParticleKind.Picture, Image = Pet.ReadBitmap(path) }; }
                catch (Exception) { throw new EffectException(Lang.T("fx.err.image", arg)); }
        }
        throw new EffectException(Lang.T("fx.err.shape", shape));
    }

    static double Number(string s)
    {
        double v;
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) throw new EffectException(Lang.T("fx.err.number", s));
        return v;
    }

    static double Clamp(double v, double lo, double hi) { return Math.Max(lo, Math.Min(hi, v)); }

    static Color Colour(string s)
    {
        if (!IsColour(s)) throw new EffectException(Lang.T("fx.err.color", s));
        int v = int.Parse(s.Substring(1), NumberStyles.HexNumber);
        return Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
    }

    public void Dispose()
    {
        foreach (ParticleKind k in Kinds) if (k.Image != null) k.Image.Dispose();
    }
}

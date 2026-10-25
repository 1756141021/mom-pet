// Interface text from lang\<code>.txt next to the exe. Each line is "key = value"; "\n" in a value is a
// line break and lines starting with # are comments. A key missing from the chosen language is taken
// from en.txt, so a new translation can start small. Adding a language means adding one file.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

static class Lang
{
    static string dir;
    static Dictionary<string, string> table = new Dictionary<string, string>();
    static Dictionary<string, string> fallback = new Dictionary<string, string>();

    public static string Code { get; private set; }

    // code: a file name without .txt; null or a missing file picks one from the Windows display language
    public static void Load(string langDir, string code)
    {
        dir = langDir;
        if (code == null || !File.Exists(PathOf(code))) code = FromWindows();
        Code = code;
        table = Read(code);
        fallback = Read("en");
    }

    public static string T(string key)
    {
        string s;
        if (table.TryGetValue(key, out s) || fallback.TryGetValue(key, out s)) return s;
        return key;
    }

    public static string T(string key, params object[] args)
    {
        return string.Format(T(key), args);
    }

    // every installed language as (code, its own name for itself)
    public static List<KeyValuePair<string, string>> Available()
    {
        var list = new List<KeyValuePair<string, string>>();
        if (!Directory.Exists(dir)) return list;
        foreach (string file in Directory.GetFiles(dir, "*.txt"))
        {
            string code = Path.GetFileNameWithoutExtension(file);
            string name;
            if (!Read(code).TryGetValue("language.name", out name)) name = code;
            list.Add(new KeyValuePair<string, string>(code, name));
        }
        return list;
    }

    static string FromWindows()
    {
        CultureInfo ui = CultureInfo.CurrentUICulture;
        string pick = "en";
        if (ui.TwoLetterISOLanguageName == "zh")
            pick = ui.Name.Contains("Hant") || ui.Name.EndsWith("TW") || ui.Name.EndsWith("HK") || ui.Name.EndsWith("MO")
                ? "zh-Hant" : "zh-Hans";
        else if (ui.TwoLetterISOLanguageName == "ja") pick = "ja";
        return File.Exists(PathOf(pick)) ? pick : "en";
    }

    static string PathOf(string code)
    {
        return Path.Combine(dir, code + ".txt");
    }

    static Dictionary<string, string> Read(string code)
    {
        var d = new Dictionary<string, string>();
        string path = PathOf(code);
        if (!File.Exists(path)) return d;
        foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            string line = raw.Trim();
            int eq = line.IndexOf('=');
            if (line.Length == 0 || line[0] == '#' || eq < 0) continue;
            d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim().Replace("\\n", "\n");
        }
        return d;
    }
}

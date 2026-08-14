using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Vex.Island
{
    /// <summary>
    /// ReadAloud's settings.json. One file, both tools.
    /// </summary>
    public sealed class IslandVoice
    {
        public double Speed = 1.5;
        public double Pitch = 1.0;
        public double Volume = 1.0;
        public string Engine = "google";
        public bool WiggleEnabled = true;
        public bool WigglePop = true;
        public string WiggleFeel = "normal";
        public int WiggleFlips = 5;
        public int WiggleWindowMs = 500;
        public int WiggleMinPx = 25;
        public int WiggleCooldownMs = 1800;

        public static string FilePath
        {
            get
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, ".config", "readaloud", "settings.json");
            }
        }

        public static IslandVoice Load()
        {
            var v = new IslandVoice();
            try
            {
                if (!File.Exists(FilePath))
                    return v;
                var json = File.ReadAllText(FilePath);
                v.Speed = Num(json, "Speed", v.Speed);
                v.Pitch = Num(json, "Pitch", v.Pitch);
                v.Volume = Num(json, "Volume", v.Volume);
                v.Engine = Str(json, "Engine", v.Engine);
                v.WiggleEnabled = Flag(json, "WiggleEnabled", true);
                v.WigglePop = Flag(json, "WigglePop", true);
                v.WiggleFeel = Str(json, "WiggleFeel", v.WiggleFeel);
                v.WiggleFlips = (int)Num(json, "WiggleFlips", v.WiggleFlips);
                v.WiggleWindowMs = (int)Num(json, "WiggleWindowMs", v.WiggleWindowMs);
                v.WiggleMinPx = (int)Num(json, "WiggleMinPx", v.WiggleMinPx);
                v.WiggleCooldownMs = (int)Num(json, "WiggleCooldownMs", v.WiggleCooldownMs);
                v.Normalize();
            }
            catch
            {
            }

            return v;
        }

        public void Save()
        {
            Normalize();
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var json = File.Exists(FilePath) ? File.ReadAllText(FilePath) : "{\n}\n";
            json = PutNum(json, "Speed", Speed);
            json = PutNum(json, "Pitch", Pitch);
            json = PutNum(json, "Volume", Volume);
            json = PutStr(json, "Engine", Engine);
            json = PutFlag(json, "WiggleEnabled", WiggleEnabled);
            json = PutFlag(json, "WigglePop", WigglePop);
            json = PutStr(json, "WiggleFeel", WiggleFeel);
            json = PutNum(json, "WiggleFlips", WiggleFlips);
            json = PutNum(json, "WiggleWindowMs", WiggleWindowMs);
            json = PutNum(json, "WiggleMinPx", WiggleMinPx);
            json = PutNum(json, "WiggleCooldownMs", WiggleCooldownMs);
            File.WriteAllText(FilePath, json);
        }

        public void ApplyFeel(string feel)
        {
            WiggleFeel = feel;
            switch (feel)
            {
                case "sensitive":
                    WiggleFlips = 3;
                    WiggleWindowMs = 700;
                    WiggleMinPx = 12;
                    WiggleCooldownMs = 1200;
                    break;
                case "firm":
                    WiggleFlips = 6;
                    WiggleWindowMs = 450;
                    WiggleMinPx = 35;
                    WiggleCooldownMs = 2200;
                    break;
                case "stubborn":
                    WiggleFlips = 8;
                    WiggleWindowMs = 400;
                    WiggleMinPx = 50;
                    WiggleCooldownMs = 2800;
                    break;
                default:
                    WiggleFeel = "normal";
                    WiggleFlips = 5;
                    WiggleWindowMs = 500;
                    WiggleMinPx = 25;
                    WiggleCooldownMs = 1800;
                    break;
            }
        }

        void Normalize()
        {
            if (Speed < 0.5 || Speed > 3.0)
                Speed = 1.5;
            if (Pitch < 0.5 || Pitch > 2.0)
                Pitch = 1.0;
            if (Volume < 0.25 || Volume > 2.0)
                Volume = 1.0;
            if (Engine != "google" && Engine != "inflect" && Engine != "spd")
                Engine = "google";
            if (string.IsNullOrEmpty(WiggleFeel))
                WiggleFeel = "normal";
            if (WiggleFlips < 2 || WiggleFlips > 20)
                ApplyFeel(WiggleFeel);
        }

        static double Num(string json, string key, double fallback)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(-?[0-9.]+)");
            if (!m.Success)
                return fallback;
            double n;
            return double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out n)
                ? n
                : fallback;
        }

        static string Str(string json, string key, string fallback)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : fallback;
        }

        static bool Flag(string json, string key, bool fallback)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value[0] == 't' || m.Groups[1].Value[0] == 'T' : fallback;
        }

        static string PutNum(string json, string key, double n)
        {
            var val = n.ToString("0.###", CultureInfo.InvariantCulture);
            return PutRaw(json, key, val);
        }

        static string PutStr(string json, string key, string s)
        {
            return PutRaw(json, key, "\"" + (s ?? "").Replace("\"", "") + "\"");
        }

        static string PutFlag(string json, string key, bool v)
        {
            return PutRaw(json, key, v ? "true" : "false");
        }

        static string PutRaw(string json, string key, string raw)
        {
            var pat = "\"" + key + "\"\\s*:\\s*(\"[^\"]*\"|-?[0-9.]+|true|false)";
            if (Regex.IsMatch(json, pat, RegexOptions.IgnoreCase))
                return Regex.Replace(json, pat, "\"" + key + "\": " + raw, RegexOptions.IgnoreCase);
            var i = json.LastIndexOf('}');
            if (i < 0)
                return "{\n  \"" + key + "\": " + raw + "\n}\n";
            var insert = "  \"" + key + "\": " + raw;
            var before = json.Substring(0, i).TrimEnd();
            if (!before.EndsWith("{") && !before.EndsWith(","))
                insert = ",\n" + insert;
            else
                insert = "\n" + insert;
            return before + insert + "\n}\n";
        }
    }
}

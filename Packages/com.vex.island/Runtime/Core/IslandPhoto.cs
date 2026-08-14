using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Vex.Island
{
    public enum IslandDrop
    {
        Off,
        Light,
        Heavy
    }

    /// <summary>
    /// PhotoLog's job on the island: drop an already-selected photo,
    /// edit the stamp (date / time / address / lighting), preview,
    /// write copies. Originals are never touched.
    /// Shares ~/.local/share/PhotoLog/settings.json with the Avalonia app.
    /// </summary>
    public sealed class IslandPhoto
    {
        public const string DateFmt = "MMM d, yyyy 'at' h:mm:ss tt";
        public const int DefaultShadowX = 1;
        public const int DefaultShadowY = -1;

        static readonly string[] Raster =
        {
            "png", "jpg", "jpeg", "gif", "webp", "bmp", "tif", "tiff"
        };

        static readonly IslandPhoto _current = new IslandPhoto();

        public static IslandPhoto Current => _current;
        public static string SettingsPathOverride;
        public static string OutOverride;

        readonly List<string> _files = new List<string>();
        readonly List<DateTime> _taken = new List<DateTime>();

        public IReadOnlyList<string> Files => _files;
        public int Index { get; private set; }
        public DateTime? DateOverride { get; set; }
        public DateTime? TimeOverride { get; set; }
        public string Address { get; set; } = "";
        public IslandDrop Drop { get; set; } = IslandDrop.Light;
        public int ShadowX { get; set; } = DefaultShadowX;
        public int ShadowY { get; set; } = DefaultShadowY;
        public bool PreviewStale { get; set; }
        public string PreviewPath { get; private set; } = "";
        public string LastDest { get; private set; } = "";
        public string LastNote { get; private set; } = "";
        public int LastCount { get; private set; }

        public int Count => _files.Count;
        public bool HasWork => _files.Count > 0;

        public DateTime Taken
        {
            get
            {
                if (_taken.Count == 0)
                    return DateTime.Now;
                var i = Index < 0 || Index >= _taken.Count ? 0 : Index;
                return _taken[i];
            }
        }

        public string PathAt
        {
            get
            {
                if (_files.Count == 0)
                    return "";
                var i = Index < 0 || Index >= _files.Count ? 0 : Index;
                return _files[i];
            }
        }

        public static bool Accepts(string path)
        {
            var e = IslandPaths.Ext(path);
            for (var i = 0; i < Raster.Length; i++)
            {
                if (e == Raster[i])
                    return true;
            }

            return false;
        }

        public static bool AcceptsAll(IReadOnlyList<string> files)
        {
            if (files == null || files.Count == 0)
                return false;
            for (var i = 0; i < files.Count; i++)
            {
                if (!Accepts(files[i]))
                    return false;
            }

            return true;
        }

        public static string FmtDate(DateTime dt)
        {
            return dt.ToString(DateFmt, CultureInfo.InvariantCulture);
        }

        public static DateTime Combine(DateTime taken, DateTime? date, DateTime? time)
        {
            var d = date ?? taken;
            var t = time ?? taken;
            return new DateTime(d.Year, d.Month, d.Day, t.Hour, t.Minute, t.Second);
        }

        public DateTime Effective()
        {
            return Combine(Taken, DateOverride, TimeOverride);
        }

        public DateTime Effective(DateTime taken)
        {
            return Combine(taken, DateOverride, TimeOverride);
        }

        public string StampLine()
        {
            return FmtDate(Effective());
        }

        public string[] StampLines()
        {
            var date = StampLine();
            var addr = (Address ?? "").Trim();
            if (addr.Length == 0)
                return new[] { date };
            addr = addr.Replace("\r\n", "\n").Replace('\r', '\n');
            var extra = addr.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var lines = new string[1 + extra.Length];
            lines[0] = date;
            for (var i = 0; i < extra.Length; i++)
                lines[i + 1] = extra[i].Trim();
            return lines;
        }

        public static (int X, int Y) DropOffset(IslandDrop drop)
        {
            switch (drop)
            {
                case IslandDrop.Off: return (0, 0);
                case IslandDrop.Heavy: return (3, -2);
                default: return (1, -1);
            }
        }

        public void ApplyDrop(IslandDrop drop)
        {
            Drop = drop;
            var o = DropOffset(drop);
            ShadowX = o.X;
            ShadowY = o.Y;
            MarkDirty();
        }

        public void MarkDirty()
        {
            PreviewStale = true;
            LastCount = 0;
        }

        public void Bind(IReadOnlyList<string> files)
        {
            _files.Clear();
            _taken.Clear();
            Index = 0;
            DateOverride = null;
            TimeOverride = null;
            LastDest = "";
            LastNote = "";
            LastCount = 0;
            MarkDirty();
            LoadPrefs();
            if (files == null)
                return;
            for (var i = 0; i < files.Count; i++)
            {
                var p = files[i];
                if (string.IsNullOrWhiteSpace(p) || !Accepts(p))
                    continue;
                _files.Add(p);
                _taken.Add(PhotoTime(p));
            }
        }

        public void Clear()
        {
            _files.Clear();
            _taken.Clear();
            Index = 0;
            DateOverride = null;
            TimeOverride = null;
            PreviewStale = false;
            PreviewPath = "";
            LastDest = "";
            LastNote = "";
            LastCount = 0;
        }

        public bool SameFiles(IReadOnlyList<string> files)
        {
            if (files == null || files.Count != _files.Count)
                return false;
            for (var i = 0; i < files.Count; i++)
            {
                if (!string.Equals(files[i], _files[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        public void Show(int index)
        {
            if (_files.Count == 0)
                return;
            var n = index % _files.Count;
            if (n < 0)
                n += _files.Count;
            if (n == Index)
                return;
            Index = n;
            MarkDirty();
        }

        public void NudgeDay(int days)
        {
            var day = (DateOverride ?? Taken).Date.AddDays(days);
            DateOverride = day;
            MarkDirty();
        }

        public void NudgeTime(int minutes)
        {
            var t = Effective().AddMinutes(minutes);
            DateOverride = t;
            TimeOverride = t;
            MarkDirty();
        }

        public void SetToday()
        {
            DateOverride = DateTime.Today;
            MarkDirty();
        }

        public void ClearDate()
        {
            DateOverride = null;
            MarkDirty();
        }

        public void ClearTime()
        {
            TimeOverride = null;
            MarkDirty();
        }

        public bool TrySetDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                ClearDate();
                return true;
            }

            DateTime d;
            if (!TryParseDate(raw.Trim(), out d))
                return false;
            DateOverride = d.Date;
            MarkDirty();
            return true;
        }

        public bool TrySetTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                ClearTime();
                return true;
            }

            DateTime t;
            if (!TryParseTime(raw.Trim(), out t))
                return false;
            TimeOverride = t;
            MarkDirty();
            return true;
        }

        public static bool TryParseDate(string raw, out DateTime d)
        {
            var fmts = new[]
            {
                "yyyy-MM-dd", "yyyy/MM/dd", "MMM d, yyyy", "MMM d yyyy",
                "MMMM d, yyyy", "M/d/yyyy", "M/d/yy", "d MMM yyyy"
            };
            return DateTime.TryParseExact(raw, fmts, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out d);
        }

        public static bool TryParseTime(string raw, out DateTime t)
        {
            var fmts = new[]
            {
                "h:mm:ss tt", "h:mm tt", "hh:mm:ss tt", "H:mm:ss", "HH:mm:ss",
                "H:mm", "HH:mm", "h:mm:sstt", "h:mmtt"
            };
            var s = Regex.Replace(raw, @"\s+", " ").Trim();
            return DateTime.TryParseExact(s, fmts, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out t);
        }

        public string DateField()
        {
            return Effective().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public string TimeField()
        {
            return Effective().ToString("h:mm:ss tt", CultureInfo.InvariantCulture);
        }

        public string OutFolder()
        {
            if (!string.IsNullOrEmpty(OutOverride))
                return OutOverride;
            if (!string.IsNullOrEmpty(_out))
                return Expand(_out);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "PhotoLog-output");
        }

        public static DateTime PhotoTime(string path)
        {
            DateTime exif;
            if (TryReadJpegExif(path, out exif))
                return exif;
            try
            {
                return File.GetLastWriteTime(path);
            }
            catch
            {
                return DateTime.Now;
            }
        }

        public static bool TryReadSize(string path, out int w, out int h)
        {
            w = 0;
            h = 0;
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    if (TryJpegSize(fs, out w, out h))
                        return true;
                    fs.Position = 0;
                    if (TryPngSize(fs, out w, out h))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        public string RenderPreview(int maxSide = 640)
        {
            if (_files.Count == 0)
            {
                PreviewPath = "";
                PreviewStale = false;
                return "";
            }

            var dest = Path.Combine(WorkDir(), "island-preview.jpg");
            if (!StampTo(PathAt, dest, maxSide))
            {
                PreviewPath = "";
                PreviewStale = false;
                return "";
            }

            PreviewPath = dest;
            PreviewStale = false;
            return dest;
        }

        public string ExportAll()
        {
            LastCount = 0;
            LastDest = "";
            if (_files.Count == 0)
            {
                LastNote = "photo:0";
                return LastNote;
            }

            var destDir = OutFolder();
            try
            {
                Directory.CreateDirectory(destDir);
            }
            catch
            {
                LastNote = "photo:0";
                return LastNote;
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var n = 0;
            string last = "";
            for (var i = 0; i < _files.Count; i++)
            {
                var src = _files[i];
                if (!File.Exists(src))
                    continue;
                var name = UniqueName(Path.GetFileName(src), used);
                var dest = Path.Combine(destDir, name);
                if (!StampTo(src, dest, 0))
                    continue;
                var when = Effective(_taken[i]);
                WriteJpegExif(dest, when);
                TouchTimes(dest, when);
                LogRow(src, dest, when);
                last = dest;
                n++;
            }

            LastCount = n;
            LastDest = last;
            LastNote = n == 0 ? "photo:0" : "photo:" + n;
            WriteLast();
            return LastNote;
        }

        public string ResultLine()
        {
            if (LastCount <= 0)
                return StampLine();
            var folder = Path.GetFileName(OutFolder().TrimEnd('/', '\\'));
            if (string.IsNullOrEmpty(folder))
                folder = OutFolder();
            return LastCount + (LastCount == 1 ? " copy · " : " copies · ") + folder;
        }

        public static bool HasFfmpeg()
        {
            return FfmpegBin() != null;
        }

        public static string SettingsPath()
        {
            if (!string.IsNullOrEmpty(SettingsPathOverride))
                return SettingsPathOverride;
            return Path.Combine(SettingsDir(), "settings.json");
        }

        public static string SettingsDir()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                root = Path.Combine(home, ".local", "share");
            }

            return Path.Combine(root, "PhotoLog");
        }

        public void SavePrefs()
        {
            var path = SettingsPath();
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var json = File.Exists(path) ? File.ReadAllText(path) : "{\n}\n";
                json = PutStr(json, "dropShadow", DropName(Drop));
                json = PutNum(json, "shadowX", ShadowX);
                json = PutNum(json, "shadowY", ShadowY);
                json = PutStr(json, "outFolder", OutFolder());
                json = PutStr(json, "address", Address ?? "");
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tmp, path);
            }
            catch
            {
            }
        }

        public void LoadPrefs()
        {
            _out = "";
            Address = Address ?? "";
            Drop = IslandDrop.Light;
            ShadowX = DefaultShadowX;
            ShadowY = DefaultShadowY;
            try
            {
                var path = SettingsPath();
                if (!File.Exists(path))
                    return;
                var json = File.ReadAllText(path);
                Drop = ParseDrop(Str(json, "dropShadow", "light"));
                ShadowX = (int)Num(json, "shadowX", DropOffset(Drop).X);
                ShadowY = (int)Num(json, "shadowY", DropOffset(Drop).Y);
                _out = Str(json, "outFolder", "");
                var addr = Str(json, "address", "");
                if (!string.IsNullOrEmpty(addr))
                    Address = addr.Replace("\\n", "\n");
            }
            catch
            {
            }
        }

        /// <summary>
        /// Headless checks. Returns "" when every gate holds.
        /// </summary>
        public static string SelfCheck()
        {
            if (FmtDate(new DateTime(2026, 7, 28, 8, 23, 59)) != "Jul 28, 2026 at 8:23:59 AM")
                return "date format";
            var taken = new DateTime(2026, 7, 17, 19, 35, 49);
            if (Combine(taken, new DateTime(2026, 7, 28), null) != new DateTime(2026, 7, 28, 19, 35, 49))
                return "date-only keeps clock";
            if (Combine(taken, null, new DateTime(1, 1, 1, 12, 0, 0)) != new DateTime(2026, 7, 17, 12, 0, 0))
                return "time-only keeps day";
            if (Combine(taken, new DateTime(2026, 7, 28), new DateTime(1, 1, 1, 12, 0, 0))
                != new DateTime(2026, 7, 28, 12, 0, 0))
                return "date+time replace both";
            if (DropOffset(IslandDrop.Light) != (1, -1) || DropOffset(IslandDrop.Heavy) != (3, -2))
                return "drop offset map";
            if (!Accepts("a.PNG") || !Accepts("/tmp/x.webp") || Accepts("note.md"))
                return "accepts";

            DateTime parsed;
            if (!TryParseDate("2026-07-28", out parsed) || parsed.Date != new DateTime(2026, 7, 28))
                return "parse date";
            if (!TryParseTime("7:35:49 PM", out parsed) || parsed.Hour != 19 || parsed.Minute != 35)
                return "parse time 12h";
            if (!TryParseTime("19:35:49", out parsed) || parsed.Hour != 19)
                return "parse time 24h";

            var tmp = Path.Combine(Path.GetTempPath(), "island-photo-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            var prevSettings = SettingsPathOverride;
            var prevOut = OutOverride;
            SettingsPathOverride = Path.Combine(tmp, "settings.json");
            OutOverride = Path.Combine(tmp, "out");
            try
            {
                var mtimePath = Path.Combine(tmp, "mtime.bin");
                File.WriteAllText(mtimePath, "x");
                var when = new DateTime(2026, 6, 1, 15, 4, 5);
                File.SetLastWriteTime(mtimePath, when);
                var got = PhotoTime(mtimePath);
                if (Math.Abs((got - when).TotalSeconds) > 2)
                    return "mtime fallback";

                var jpeg = Path.Combine(tmp, "exif.jpg");
                WriteProbeJpeg(jpeg, "2026:07:17 19:35:49");
                if (PhotoTime(jpeg) != new DateTime(2026, 7, 17, 19, 35, 49))
                    return "jpeg exif DateTimeOriginal";

                var session = new IslandPhoto();
                session.ApplyDrop(IslandDrop.Heavy);
                session.Address = "1521 Meander Rd";
                session.SavePrefs();
                var back = new IslandPhoto();
                back.LoadPrefs();
                if (back.Drop != IslandDrop.Heavy || back.ShadowX != 3 || back.Address != "1521 Meander Rd")
                    return "settings roundtrip";

                if (!HasFfmpeg())
                    return "";

                var src = Path.Combine(tmp, "in.jpg");
                if (!MakeGrayJpeg(src, 400, 300))
                    return "ffmpeg make jpeg";
                WriteJpegExif(src, new DateTime(2026, 7, 17, 19, 35, 49));
                if (PhotoTime(src) != new DateTime(2026, 7, 17, 19, 35, 49))
                    return "write+read exif";

                var origLen = new FileInfo(src).Length;
                session.Bind(new[] { src });
                session.DateOverride = new DateTime(2026, 7, 28);
                session.ApplyDrop(IslandDrop.Light);
                if (session.StampLine() != "Jul 28, 2026 at 7:35:49 PM")
                    return "stamp line override date";
                var note = session.ExportAll();
                if (note != "photo:1" || !File.Exists(session.LastDest))
                    return "export wrote copy";
                if (new FileInfo(src).Length != origLen)
                    return "original touched";
                if (Math.Abs((File.GetLastWriteTime(session.LastDest) - session.Effective()).TotalSeconds) > 3)
                    return "export mtime matches stamp";
                var prev = session.RenderPreview(320);
                if (string.IsNullOrEmpty(prev) || !File.Exists(prev))
                    return "preview wrote";
            }
            finally
            {
                SettingsPathOverride = prevSettings;
                OutOverride = prevOut;
                try { Directory.Delete(tmp, true); } catch { }
            }

            return "";
        }

        string _out = "";

        bool StampTo(string src, string dest, int maxSide)
        {
            var bin = FfmpegBin();
            if (bin == null || !File.Exists(src))
                return false;
            int w, h;
            if (!TryReadSize(src, out w, out h) || w <= 0)
            {
                w = maxSide > 0 ? maxSide : 1280;
                h = w;
            }

            var vw = w;
            var vh = h;
            if (maxSide > 0 && (w > maxSide || h > maxSide))
            {
                if (w >= h)
                {
                    vw = maxSide;
                    vh = Math.Max(1, h * maxSide / w);
                }
                else
                {
                    vh = maxSide;
                    vw = Math.Max(1, w * maxSide / h);
                }
            }

            var fs = Math.Max(14, vw / 30);
            var pad = Math.Max(6, fs / 2);
            var ls = Math.Max(4, fs / 3);
            var sx = ScaleShadow(ShadowX, fs);
            var sy = ScaleShadow(ShadowY, fs);
            var font = FontFile();
            if (font == null)
                return false;
            var vf = BuildVf(maxSide, font, StampLinesFor(src), fs, pad, ls, sx, sy);
            var psi = new ProcessStartInfo(bin)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(src);
            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-vf");
            psi.ArgumentList.Add(vf);
            psi.ArgumentList.Add(dest);
            try
            {
                using (var p = Process.Start(psi))
                {
                    if (p == null)
                        return false;
                    if (!p.WaitForExit(30000))
                    {
                        try { p.Kill(); } catch { }
                        return false;
                    }

                    return p.ExitCode == 0 && File.Exists(dest);
                }
            }
            catch
            {
                return false;
            }
        }

        string[] StampLinesFor(string src)
        {
            var i = _files.IndexOf(src);
            var taken = i >= 0 && i < _taken.Count ? _taken[i] : PhotoTime(src);
            var date = FmtDate(Effective(taken));
            var addr = (Address ?? "").Trim();
            if (addr.Length == 0)
                return new[] { date };
            addr = addr.Replace("\r\n", "\n").Replace('\r', '\n');
            var extra = addr.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var lines = new string[1 + extra.Length];
            lines[0] = date;
            for (var k = 0; k < extra.Length; k++)
                lines[k + 1] = extra[k].Trim();
            return lines;
        }

        string BuildVf(int maxSide, string font, string[] lines, int fs, int pad, int ls, int sx, int sy)
        {
            var parts = new List<string>();
            if (maxSide > 0)
                parts.Add("scale=" + maxSide + ":" + maxSide + ":force_original_aspect_ratio=decrease");
            var fe = FEsc(font);
            var x0 = "w-text_w-" + pad;
            var step = fs + ls;
            for (var i = 0; i < lines.Length; i++)
            {
                var te = TEsc(lines[i]);
                var y0 = (pad + i * step).ToString();
                if (Drop == IslandDrop.Heavy && (sx != 0 || sy != 0))
                {
                    parts.Add(Draw(fe, te, fs, "0x000000", x0 + "+" + sx, y0 + "+" + sy, 0, 0));
                    parts.Add(Draw(fe, te, fs, "0x191919",
                        x0 + "+" + Mul(sx, 2, 3), y0 + "+" + Mul(sy, 2, 3), 0, 0));
                    parts.Add(Draw(fe, te, fs, "0x2d2d2d",
                        x0 + "+" + Mul(sx, 1, 3), y0 + "+" + Mul(sy, 1, 3), 0, 0));
                    parts.Add(Draw(fe, te, fs, "white", x0, y0, 0, 0));
                }
                else if (Drop == IslandDrop.Light && (sx != 0 || sy != 0))
                    parts.Add(Draw(fe, te, fs, "white", x0, y0, sx, sy));
                else
                    parts.Add(Draw(fe, te, fs, "white", x0, y0, 0, 0));
            }

            return string.Join(",", parts);
        }

        static string Draw(string font, string text, int fs, string color, string x, string y, int sx, int sy)
        {
            var s = "drawtext=fontfile=" + font + ":text='" + text + "':expansion=none" +
                    ":fontsize=" + fs + ":fontcolor=" + color + ":x=" + x + ":y=" + y;
            if (sx != 0 || sy != 0)
                s += ":shadowcolor=0x282828:shadowx=" + sx + ":shadowy=" + sy;
            return s;
        }

        static string TEsc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'").Replace(":", "\\:");
        }

        static int ScaleShadow(int v, int size)
        {
            if (v == 0)
                return 0;
            var n = (int)Math.Round(v * size / 30.0);
            if (n == 0)
                return v > 0 ? 1 : -1;
            return n;
        }

        static int Mul(int v, int num, int den)
        {
            return (int)Math.Round(v * (double)num / den);
        }

        static string FEsc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace(":", "\\:").Replace("'", "\\'");
        }

        static string UniqueName(string name, HashSet<string> used)
        {
            if (string.IsNullOrEmpty(name))
                name = "photo.jpg";
            var ext = Path.GetExtension(name);
            var stem = Path.GetFileNameWithoutExtension(name);
            if (string.IsNullOrEmpty(stem))
                stem = "photo";
            var n = stem + ext;
            for (var i = 1; !used.Add(n); i++)
                n = stem + "_" + i + ext;
            return n;
        }

        void LogRow(string src, string dest, DateTime when)
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath());
                if (string.IsNullOrEmpty(dir))
                    dir = SettingsDir();
                Directory.CreateDirectory(dir);
                var line = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\t" +
                           src + "\t" + dest + "\t" + FmtDate(when) + "\t" +
                           DropName(Drop) + "\t" + (Address ?? "").Replace('\t', ' ').Replace('\n', '|') + "\n";
                File.AppendAllText(Path.Combine(dir, "island.tsv"), line);
                File.AppendAllText(Path.Combine(OutFolder(), "photolog.tsv"), line);
            }
            catch
            {
            }
        }

        void WriteLast()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath());
                if (string.IsNullOrEmpty(dir))
                    dir = SettingsDir();
                Directory.CreateDirectory(dir);
                var json = "{\n" +
                           "  \"count\": " + LastCount + ",\n" +
                           "  \"dest\": \"" + Esc(LastDest) + "\",\n" +
                           "  \"outFolder\": \"" + Esc(OutFolder()) + "\",\n" +
                           "  \"stamp\": \"" + Esc(StampLine()) + "\",\n" +
                           "  \"shadow\": \"" + DropName(Drop) + "\",\n" +
                           "  \"address\": \"" + Esc(Address ?? "") + "\"\n" +
                           "}\n";
                File.WriteAllText(Path.Combine(dir, "last-island.json"), json);
            }
            catch
            {
            }
        }

        static string Esc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }

        public static void TouchTimes(string path, DateTime when)
        {
            var t = when.Kind == DateTimeKind.Utc ? when.ToLocalTime()
                : when.Kind == DateTimeKind.Local ? when
                : DateTime.SpecifyKind(when, DateTimeKind.Local);
            if (t.Year < 1980)
                t = new DateTime(1980, 1, 1, t.Hour, t.Minute, t.Second, DateTimeKind.Local);
            try { File.SetLastWriteTime(path, t); } catch { }
            try { File.SetLastAccessTime(path, t); } catch { }
            try { File.SetCreationTime(path, t); } catch { }
        }

        public static bool TryReadJpegExif(string path, out DateTime when)
        {
            when = default;
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    if (fs.Length < 12)
                        return false;
                    if (fs.ReadByte() != 0xFF || fs.ReadByte() != 0xD8)
                        return false;
                    var buf = new byte[8];
                    while (fs.Position < fs.Length - 4)
                    {
                        var b = fs.ReadByte();
                        if (b != 0xFF)
                            continue;
                        int m;
                        do { m = fs.ReadByte(); } while (m == 0xFF);
                        if (m < 0 || m == 0xDA || m == 0xD9)
                            return false;
                        if (m == 0x00 || (m >= 0xD0 && m <= 0xD7))
                            continue;
                        if (fs.Read(buf, 0, 2) != 2)
                            return false;
                        var len = (buf[0] << 8) | buf[1];
                        if (len < 2)
                            return false;
                        var payload = len - 2;
                        if (m == 0xE1 && payload >= 14)
                        {
                            var data = new byte[payload];
                            if (fs.Read(data, 0, payload) != payload)
                                return false;
                            if (data.Length >= 6 && data[0] == (byte)'E' && data[1] == (byte)'x'
                                && data[2] == (byte)'i' && data[3] == (byte)'f' && data[4] == 0 && data[5] == 0)
                            {
                                if (TryParseTiffDate(data, 6, out when))
                                    return true;
                            }
                            continue;
                        }
                        else
                            fs.Position += payload;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        static bool TryParseTiffDate(byte[] file, int tiff, out DateTime when)
        {
            when = default;
            if (tiff + 8 > file.Length)
                return false;
            var le = file[tiff] == (byte)'I' && file[tiff + 1] == (byte)'I';
            var be = file[tiff] == (byte)'M' && file[tiff + 1] == (byte)'M';
            if (!le && !be)
                return false;
            var ifd0 = tiff + (int)U32(file, tiff + 4, le);
            string dt = null, orig = null;
            uint exifOff = 0;
            if (!ReadIfd(file, tiff, ifd0, le, ref dt, ref orig, ref exifOff))
                return false;
            if (exifOff > 0)
                ReadIfd(file, tiff, tiff + (int)exifOff, le, ref dt, ref orig, ref exifOff);
            var raw = orig ?? dt;
            return raw != null && DateTime.TryParseExact(raw.Trim(), "yyyy:MM:dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out when);
        }

        static bool ReadIfd(byte[] file, int tiff, int ifd, bool le,
            ref string dt, ref string orig, ref uint exifOff)
        {
            if (ifd < 0 || ifd + 2 > file.Length)
                return false;
            var n = U16(file, ifd, le);
            var end = ifd + 2 + n * 12;
            if (end > file.Length)
                return false;
            for (var i = 0; i < n; i++)
            {
                var e = ifd + 2 + i * 12;
                var tag = U16(file, e, le);
                var typ = U16(file, e + 2, le);
                var cnt = U32(file, e + 4, le);
                if (tag == 0x8769 && typ == 4)
                    exifOff = U32(file, e + 8, le);
                else if (typ == 2 && cnt >= 10 && cnt < 64)
                {
                    var s = Ascii(file, tiff, e, cnt, le);
                    if (tag == 0x9003 && !string.IsNullOrEmpty(s))
                        orig = s;
                    else if (tag == 0x0132 && !string.IsNullOrEmpty(s) && dt == null)
                        dt = s;
                    else if (tag == 0x9004 && !string.IsNullOrEmpty(s) && orig == null)
                        orig = s;
                }
            }

            return true;
        }

        static string Ascii(byte[] file, int tiff, int entry, uint cnt, bool le)
        {
            int off;
            if (cnt <= 4)
                off = entry + 8;
            else
                off = tiff + (int)U32(file, entry + 8, le);
            if (off < 0 || off + cnt > file.Length)
                return null;
            var n = (int)cnt;
            if (n > 0 && file[off + n - 1] == 0)
                n--;
            return Encoding.ASCII.GetString(file, off, n);
        }

        static ushort U16(byte[] b, int i, bool le)
        {
            return le ? (ushort)(b[i] | (b[i + 1] << 8)) : (ushort)((b[i] << 8) | b[i + 1]);
        }

        static uint U32(byte[] b, int i, bool le)
        {
            return le
                ? (uint)(b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24))
                : (uint)((b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]);
        }

        public static void WriteJpegExif(string path, DateTime when)
        {
            try
            {
                var s = when.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
                    return;
                if (PatchJpegExif(bytes, s))
                {
                    File.WriteAllBytes(path, bytes);
                    return;
                }

                var inserted = InsertJpegExif(bytes, s);
                if (inserted != null)
                    File.WriteAllBytes(path, inserted);
            }
            catch
            {
            }
        }

        static bool PatchJpegExif(byte[] bytes, string stamp)
        {
            var i = 2;
            while (i + 4 < bytes.Length)
            {
                if (bytes[i] != 0xFF)
                    return false;
                var m = bytes[i + 1];
                if (m == 0xDA || m == 0xD9)
                    return false;
                var len = (bytes[i + 2] << 8) | bytes[i + 3];
                if (m == 0xE1 && i + 4 + 6 < bytes.Length
                    && bytes[i + 4] == (byte)'E' && bytes[i + 5] == (byte)'x')
                {
                    var tiff = i + 10;
                    var want = Encoding.ASCII.GetBytes(stamp);
                    var hit = false;
                    hit |= PatchAscii(bytes, tiff, 0x9003, want);
                    hit |= PatchAscii(bytes, tiff, 0x9004, want);
                    hit |= PatchAscii(bytes, tiff, 0x0132, want);
                    return hit;
                }

                i += 2 + len;
            }

            return false;
        }

        static bool PatchAscii(byte[] file, int tiff, int wantTag, byte[] stamp)
        {
            if (tiff + 8 > file.Length)
                return false;
            var le = file[tiff] == (byte)'I';
            var ifd = tiff + (int)U32(file, tiff + 4, le);
            uint exifOff = 0;
            string dt = null, orig = null;
            if (!ReadIfd(file, tiff, ifd, le, ref dt, ref orig, ref exifOff))
                return false;
            if (WriteTag(file, tiff, ifd, le, wantTag, stamp))
                return true;
            if (exifOff > 0)
                return WriteTag(file, tiff, tiff + (int)exifOff, le, wantTag, stamp);
            return false;
        }

        static bool WriteTag(byte[] file, int tiff, int ifd, bool le, int wantTag, byte[] stamp)
        {
            if (ifd < 0 || ifd + 2 > file.Length)
                return false;
            var n = U16(file, ifd, le);
            for (var i = 0; i < n; i++)
            {
                var e = ifd + 2 + i * 12;
                if (e + 12 > file.Length)
                    return false;
                if (U16(file, e, le) != wantTag || U16(file, e + 2, le) != 2)
                    continue;
                var cnt = U32(file, e + 4, le);
                if (cnt < 20)
                    continue;
                var off = tiff + (int)U32(file, e + 8, le);
                if (off < 0 || off + stamp.Length > file.Length)
                    return false;
                Buffer.BlockCopy(stamp, 0, file, off, stamp.Length);
                if (off + stamp.Length < file.Length)
                    file[off + stamp.Length] = 0;
                return true;
            }

            return false;
        }

        static byte[] InsertJpegExif(byte[] jpeg, string stamp)
        {
            var app1 = BuildApp1(stamp);
            var insertAt = 2;
            if (jpeg.Length > 6 && jpeg[2] == 0xFF && jpeg[3] == 0xE0)
            {
                var len = (jpeg[4] << 8) | jpeg[5];
                insertAt = 2 + 2 + len;
                if (insertAt > jpeg.Length)
                    insertAt = 2;
            }

            var dest = new byte[jpeg.Length + app1.Length];
            Buffer.BlockCopy(jpeg, 0, dest, 0, insertAt);
            Buffer.BlockCopy(app1, 0, dest, insertAt, app1.Length);
            Buffer.BlockCopy(jpeg, insertAt, dest, insertAt + app1.Length, jpeg.Length - insertAt);
            return dest;
        }

        static byte[] BuildApp1(string stamp)
        {
            var dt = Encoding.ASCII.GetBytes(stamp);
            if (dt.Length != 19)
                dt = Encoding.ASCII.GetBytes("1980:01:01 00:00:00");
            // TIFF: IFD0 has ExifOffset; ExifIFD has DateTimeOriginal + DateTimeDigitized
            // sharing one 20-byte string at 0x38.
            var tiff = new byte[76];
            tiff[0] = (byte)'I';
            tiff[1] = (byte)'I';
            tiff[2] = 0x2A;
            tiff[4] = 8;
            tiff[8] = 1;
            WriteEntry(tiff, 10, 0x8769, 4, 1, 26);
            tiff[26] = 2;
            WriteEntry(tiff, 28, 0x9003, 2, 20, 56);
            WriteEntry(tiff, 40, 0x9004, 2, 20, 56);
            Buffer.BlockCopy(dt, 0, tiff, 56, 19);
            var payload = new byte[6 + tiff.Length];
            payload[0] = (byte)'E';
            payload[1] = (byte)'x';
            payload[2] = (byte)'i';
            payload[3] = (byte)'f';
            Buffer.BlockCopy(tiff, 0, payload, 6, tiff.Length);
            var app1 = new byte[4 + payload.Length];
            app1[0] = 0xFF;
            app1[1] = 0xE1;
            var len = payload.Length + 2;
            app1[2] = (byte)(len >> 8);
            app1[3] = (byte)len;
            Buffer.BlockCopy(payload, 0, app1, 4, payload.Length);
            return app1;
        }

        static void WriteEntry(byte[] tiff, int at, int tag, int typ, int cnt, int value)
        {
            tiff[at] = (byte)(tag & 0xFF);
            tiff[at + 1] = (byte)(tag >> 8);
            tiff[at + 2] = (byte)(typ & 0xFF);
            tiff[at + 3] = (byte)(typ >> 8);
            tiff[at + 4] = (byte)(cnt & 0xFF);
            tiff[at + 8] = (byte)(value & 0xFF);
            tiff[at + 9] = (byte)((value >> 8) & 0xFF);
        }

        public static void WriteProbeJpeg(string path, string exifDate)
        {
            var app1 = BuildApp1(exifDate);
            var jpeg = new byte[2 + app1.Length + 2];
            jpeg[0] = 0xFF;
            jpeg[1] = 0xD8;
            Buffer.BlockCopy(app1, 0, jpeg, 2, app1.Length);
            jpeg[jpeg.Length - 2] = 0xFF;
            jpeg[jpeg.Length - 1] = 0xD9;
            File.WriteAllBytes(path, jpeg);
        }

        static bool TryJpegSize(Stream fs, out int w, out int h)
        {
            w = 0;
            h = 0;
            if (fs.ReadByte() != 0xFF || fs.ReadByte() != 0xD8)
                return false;
            var buf = new byte[8];
            while (fs.Position < fs.Length - 8)
            {
                var b = fs.ReadByte();
                if (b != 0xFF)
                    continue;
                int m;
                do { m = fs.ReadByte(); } while (m == 0xFF);
                if (m < 0 || m == 0xDA || m == 0xD9)
                    return false;
                if (m == 0x00 || (m >= 0xD0 && m <= 0xD7))
                    continue;
                if (fs.Read(buf, 0, 2) != 2)
                    return false;
                var len = (buf[0] << 8) | buf[1];
                if ((m >= 0xC0 && m <= 0xC3) || (m >= 0xC5 && m <= 0xC7)
                    || (m >= 0xC9 && m <= 0xCB) || (m >= 0xCD && m <= 0xCF))
                {
                    if (len < 7 || fs.Read(buf, 0, 5) != 5)
                        return false;
                    h = (buf[1] << 8) | buf[2];
                    w = (buf[3] << 8) | buf[4];
                    return w > 0 && h > 0;
                }

                fs.Position += Math.Max(0, len - 2);
            }

            return false;
        }

        static bool TryPngSize(Stream fs, out int w, out int h)
        {
            w = 0;
            h = 0;
            var sig = new byte[24];
            if (fs.Read(sig, 0, 24) != 24)
                return false;
            if (sig[0] != 0x89 || sig[1] != 0x50 || sig[2] != 0x4E || sig[3] != 0x47)
                return false;
            w = (sig[16] << 24) | (sig[17] << 16) | (sig[18] << 8) | sig[19];
            h = (sig[20] << 24) | (sig[21] << 16) | (sig[22] << 8) | sig[23];
            return w > 0 && h > 0;
        }

        static bool MakeGrayJpeg(string path, int w, int h)
        {
            var bin = FfmpegBin();
            if (bin == null)
                return false;
            var psi = new ProcessStartInfo(bin)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("lavfi");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add("color=c=0x6a7a88:s=" + w + "x" + h);
            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add(path);
            try
            {
                using (var p = Process.Start(psi))
                {
                    if (p == null)
                        return false;
                    p.WaitForExit(10000);
                    return p.ExitCode == 0 && File.Exists(path);
                }
            }
            catch
            {
                return false;
            }
        }

        static string FfmpegBin()
        {
            var env = Environment.GetEnvironmentVariable("ISLAND_FFMPEG");
            if (!string.IsNullOrEmpty(env) && File.Exists(env))
                return env;
            var hits = new[] { "/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/opt/homebrew/bin/ffmpeg" };
            for (var i = 0; i < hits.Length; i++)
            {
                if (File.Exists(hits[i]))
                    return hits[i];
            }

            return HasCmd("ffmpeg") ? "ffmpeg" : null;
        }

        static string FontFile()
        {
            var env = Environment.GetEnvironmentVariable("ISLAND_FONT");
            if (!string.IsNullOrEmpty(env) && File.Exists(env))
                return env;
            var hits = new[]
            {
                "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
                "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
                "/System/Library/Fonts/Supplemental/Arial.ttf",
                "/System/Library/Fonts/Helvetica.ttc",
                "C:\\Windows\\Fonts\\arial.ttf"
            };
            for (var i = 0; i < hits.Length; i++)
            {
                if (File.Exists(hits[i]))
                    return hits[i];
            }

            return null;
        }

        static string WorkDir()
        {
            var d = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d))
                d = Path.GetTempPath();
            return d;
        }

        static bool HasCmd(string cmd)
        {
            try
            {
                var psi = new ProcessStartInfo("bash")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add("-lc");
                psi.ArgumentList.Add("command -v " + cmd);
                using (var p = Process.Start(psi))
                {
                    if (p == null)
                        return false;
                    var o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(800);
                    return p.ExitCode == 0 && o.Trim().Length > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        static string Expand(string p)
        {
            if (string.IsNullOrWhiteSpace(p))
                return "";
            p = p.Trim();
            if (p.StartsWith("~"))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                p = Path.Combine(home, p.TrimStart('~').TrimStart('/', '\\'));
            }

            return p;
        }

        static string DropName(IslandDrop d)
        {
            switch (d)
            {
                case IslandDrop.Off: return "off";
                case IslandDrop.Heavy: return "heavy";
                default: return "light";
            }
        }

        static IslandDrop ParseDrop(string s)
        {
            if (string.IsNullOrEmpty(s))
                return IslandDrop.Light;
            s = s.Trim().ToLowerInvariant();
            if (s == "off" || s == "none")
                return IslandDrop.Off;
            if (s == "heavy")
                return IslandDrop.Heavy;
            return IslandDrop.Light;
        }

        static double Num(string json, string key, double fallback)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(-?[0-9.]+)");
            double n;
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out n)
                ? n
                : fallback;
        }

        static string Str(string json, string key, string fallback)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : fallback;
        }

        static string PutNum(string json, string key, double n)
        {
            return PutRaw(json, key, n.ToString("0.###", CultureInfo.InvariantCulture));
        }

        static string PutStr(string json, string key, string s)
        {
            return PutRaw(json, key, "\"" + Esc(s) + "\"");
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

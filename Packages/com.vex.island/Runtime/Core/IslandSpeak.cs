using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Vex.Island
{
    /// <summary>
    /// ReadAloud's job, without the tray. Speaks selection or file text
    /// through the existing ReadAloud binary when present, else spd-say.
    /// </summary>
    public static class IslandSpeak
    {
        static Process _child;
        static readonly object Gate = new object();

        public static bool IsLive
        {
            get
            {
                lock (Gate)
                {
                    try
                    {
                        return _child != null && !_child.HasExited;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }

        public static string Status()
        {
            if (!IsLive)
                return "";
            try
            {
                var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                var path = Path.Combine(
                    string.IsNullOrEmpty(runtime) ? Path.GetTempPath() : runtime,
                    "readaloud.status");
                if (!File.Exists(path))
                    return "speak";
                var parts = File.ReadAllText(path).Split('\t');
                if (parts.Length >= 3
                    && DateTime.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var when)
                    && DateTime.UtcNow - when > TimeSpan.FromSeconds(20))
                    return "speak";
                var detail = parts.Length > 1 ? parts[1] : parts[0];
                return string.IsNullOrEmpty(detail) ? "speak" : detail;
            }
            catch
            {
                return "speak";
            }
        }

        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            s = Regex.Replace(s, @"```.*?```", " code block. ", RegexOptions.Singleline);
            s = Regex.Replace(s, @"`([^`]*)`", "$1");
            s = Regex.Replace(s, @"\[([^\]]*)\]\([^)]*\)", "$1");
            s = Regex.Replace(s, @"https?://\S+", " link ");
            s = Regex.Replace(s, @"[#*_>|~]", " ");
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        public static string Preview(string s, int n = 18)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            s = s.Replace('\n', ' ').Trim();
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }

        public static string Selection()
        {
            var text = RunOut("wl-paste", "--primary", "--no-newline");
            if (text.Length > 0)
                return text;
            return RunOut("xclip", "-o", "-selection", "primary");
        }

        public static string Speak(string text)
        {
            text = Clean(text);
            if (text.Length == 0)
                return "empty";
            Stop();
            var bin = Binary();
            lock (Gate)
            {
                try
                {
                    if (bin != null)
                    {
                        var psi = new ProcessStartInfo(bin)
                        {
                            RedirectStandardInput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        psi.ArgumentList.Add("--stdin");
                        _child = Process.Start(psi);
                        if (_child == null)
                            return "fail";
                        _child.StandardInput.Write(text);
                        _child.StandardInput.Close();
                        return "speak";
                    }

                    if (HasCmd("spd-say"))
                    {
                        var psi = new ProcessStartInfo("spd-say")
                        {
                            RedirectStandardInput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        psi.ArgumentList.Add("-e");
                        _child = Process.Start(psi);
                        if (_child == null)
                            return "fail";
                        _child.StandardInput.Write(text);
                        _child.StandardInput.Close();
                        return "offline";
                    }
                }
                catch
                {
                    _child = null;
                    return "fail";
                }
            }

            return "no-voice";
        }

        public static string Stop()
        {
            lock (Gate)
            {
                try
                {
                    if (_child != null && !_child.HasExited)
                        _child.Kill();
                }
                catch
                {
                }

                _child = null;
            }

            var bin = Binary();
            if (bin != null)
            {
                try
                {
                    var psi = new ProcessStartInfo(bin)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    psi.ArgumentList.Add("--stop");
                    using (var p = Process.Start(psi))
                        p?.WaitForExit(1500);
                }
                catch
                {
                }
            }

            if (HasCmd("spd-say"))
            {
                try
                {
                    using (var p = Process.Start(new ProcessStartInfo("spd-say", "-C")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }))
                        p?.WaitForExit(500);
                }
                catch
                {
                }
            }

            return "stop";
        }

        public static string Binary()
        {
            var env = Environment.GetEnvironmentVariable("ISLAND_READALOUD");
            if (!string.IsNullOrEmpty(env) && File.Exists(env))
                return env;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var hits = new[]
            {
                Path.Combine(home, "GitHub", "ReadAloud", "publish", "ReadAloud"),
                Path.Combine(home, ".local", "bin", "ReadAloud")
            };
            for (var i = 0; i < hits.Length; i++)
            {
                if (File.Exists(hits[i]))
                    return hits[i];
            }

            var which = RunOut("bash", "-lc", "command -v ReadAloud");
            return which.Length > 0 && File.Exists(which) ? which : null;
        }

        static bool HasCmd(string cmd)
        {
            return RunOut("bash", "-lc", "command -v " + cmd).Length > 0;
        }

        static string RunOut(string cmd, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(cmd)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                for (var i = 0; i < args.Length; i++)
                    psi.ArgumentList.Add(args[i]);
                using (var p = Process.Start(psi))
                {
                    if (p == null)
                        return "";
                    var o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(1500);
                    return p.ExitCode == 0 ? (o ?? "").Trim() : "";
                }
            }
            catch
            {
                return "";
            }
        }
    }
}

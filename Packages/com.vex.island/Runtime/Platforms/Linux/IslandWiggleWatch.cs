using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace Vex.Island
{
    static class IslandWiggleWatch
    {
        static DateTime _stamp;
        static IslandVoice _voice = new IslandVoice();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
            foreach (var dev in Mice())
            {
                var path = dev;
                new Thread(() => Watch(path)) { IsBackground = true, Name = "island-wiggle" }.Start();
            }
#endif
        }

        static IslandVoice Voice()
        {
            try
            {
                var t = File.GetLastWriteTimeUtc(IslandVoice.FilePath);
                if (t != _stamp)
                {
                    _stamp = t;
                    _voice = IslandVoice.Load();
                }
            }
            catch
            {
            }

            return _voice;
        }

        static List<string> Mice()
        {
            var list = new List<string>();
            try
            {
                foreach (var block in File.ReadAllText("/proc/bus/input/devices").Split(new[] { "\n\n" }, StringSplitOptions.None))
                {
                    if (!Regex.IsMatch(block, @"Handlers=.*\bmouse\d"))
                        continue;
                    var m = Regex.Match(block, @"\bevent(\d+)\b");
                    if (m.Success)
                        list.Add("/dev/input/event" + m.Groups[1].Value);
                }
            }
            catch
            {
            }

            return list;
        }

        static void Watch(string dev)
        {
            try
            {
                using (var fs = new FileStream(dev, FileMode.Open, FileAccess.Read))
                {
                    var det = new IslandWiggle();
                    var buf = new byte[24];
                    while (ReadFull(fs, buf))
                    {
                        var type = BitConverter.ToUInt16(buf, 16);
                        var code = BitConverter.ToUInt16(buf, 18);
                        var value = BitConverter.ToInt32(buf, 20);
                        if (type != 2 || code != 0)
                            continue;
                        var s = Voice();
                        if (!s.WiggleEnabled)
                            continue;
                        if (det.Feed(value, Environment.TickCount, s.WiggleFlips, s.WiggleWindowMs, s.WiggleMinPx, s.WiggleCooldownMs))
                        {
                            if (s.WigglePop)
                                Pop(s.Volume);
                            IslandWiggle.Note();
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch
            {
            }
        }

        static void Pop(double volume)
        {
            try
            {
                var af = Math.Abs(volume - 1.0) > 0.001
                    ? " -af volume=" + volume.ToString("0.###", CultureInfo.InvariantCulture)
                    : "";
                Process.Start(new ProcessStartInfo("ffplay",
                    "-f lavfi -i sine=frequency=880:duration=0.07 -autoexit -nodisp -loglevel quiet" + af)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch
            {
            }
        }

        static bool ReadFull(Stream s, byte[] buf)
        {
            var got = 0;
            while (got < buf.Length)
            {
                var n = s.Read(buf, got, buf.Length - got);
                if (n <= 0)
                    return false;
                got += n;
            }

            return true;
        }
    }
}

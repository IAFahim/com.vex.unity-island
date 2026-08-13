using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Vex.Island
{
    public sealed class IslandLinuxChrome : IIslandChrome
    {
        const string Lib = "island";

        public string Id { get; private set; } = "x11";
        public bool DragLive => Native.DragLive() != 0;
        public bool WantQuit => Native.WantQuit() != 0;

        public bool Apply(int x, int y, int w, int h, int flags)
        {
            var rc = Native.Apply(Process.GetCurrentProcess().Id, x, y, w, h, flags);
            if (rc == 1)
            {
                Id = "x11";
                return true;
            }

            Id = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
                ? "none"
                : "wayland";
            return false;
        }

        public void Move(int x, int y) => Native.Move(x, y);
        public void SetVisible(bool visible) => Native.SetVisible(visible ? 1 : 0);
        public void SetShape(int[] xywh, int count) => Native.SetShape(xywh, count);
        public void BeginDrag() => Native.BeginDrag();
        public void Drag() => Native.Drag();
        public void EndDrag() => Native.EndDrag();

        public bool TryPointer(out int x, out int y) => Native.Pointer(out x, out y) == 1;

        public IslandRect[] QueryScreens()
        {
            var xr = ParseXrandr();
            if (xr.Length > 0)
                return xr;
            var buf = new int[32 * 4];
            var n = Native.GetScreens(buf, 32);
            if (n <= 0)
                return Array.Empty<IslandRect>();
            var rects = new IslandRect[n];
            for (var i = 0; i < n; i++)
                rects[i] = new IslandRect(buf[i * 4], buf[i * 4 + 1], buf[i * 4 + 2], buf[i * 4 + 3]);
            if (rects[0].W <= 0 || rects[0].H <= 0)
                return Array.Empty<IslandRect>();
            return rects;
        }

        public void Overlay(int x, int y, int w, int h) => Native.Overlay(x, y, w, h);
        public void ArmEdge(int x, int y, int w, int h) => Native.ArmEdge(x, y, w, h);

        public string[] PollDrop()
        {
            var buf = new byte[4096];
            var n = Native.XdndPoll(buf, buf.Length);
            if (n <= 0)
                return Array.Empty<string>();
            return IslandPaths.ParseUriList(System.Text.Encoding.UTF8.GetString(buf, 0, n));
        }

        static IslandRect[] ParseXrandr()
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "xrandr",
                    Arguments = "--current",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (p == null)
                    return Array.Empty<IslandRect>();
                var text = p.StandardOutput.ReadToEnd();
                p.WaitForExit(1500);
                var list = new List<IslandRect>();
                IslandRect? primary = null;
                var lines = text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (line.IndexOf(" connected", StringComparison.Ordinal) < 0)
                        continue;
                    var r = ParseMode(line);
                    if (r.W <= 0 || r.H <= 0)
                        continue;
                    if (line.IndexOf(" primary ", StringComparison.Ordinal) >= 0)
                        primary = r;
                    else
                        list.Add(r);
                }

                if (primary.HasValue)
                    list.Insert(0, primary.Value);
                return list.ToArray();
            }
            catch
            {
                return Array.Empty<IslandRect>();
            }
        }

        static IslandRect ParseMode(string line)
        {
            var plus = line.LastIndexOf('+');
            if (plus <= 0)
                return new IslandRect(0, 0, 0, 0);
            var plus2 = line.LastIndexOf('+', plus - 1);
            if (plus2 <= 0)
                return new IslandRect(0, 0, 0, 0);
            var xAt = line.LastIndexOf('x', plus2);
            if (xAt <= 0)
                return new IslandRect(0, 0, 0, 0);
            var sp = line.LastIndexOf(' ', xAt);
            if (sp < 0)
                return new IslandRect(0, 0, 0, 0);
            int w, h, x, y;
            if (!int.TryParse(line.Substring(sp + 1, xAt - sp - 1), out w))
                return new IslandRect(0, 0, 0, 0);
            if (!int.TryParse(line.Substring(xAt + 1, plus2 - xAt - 1), out h))
                return new IslandRect(0, 0, 0, 0);
            if (!int.TryParse(line.Substring(plus2 + 1, plus - plus2 - 1), out x))
                return new IslandRect(0, 0, 0, 0);
            var end = plus + 1;
            while (end < line.Length && line[end] >= '0' && line[end] <= '9')
                end++;
            if (!int.TryParse(line.Substring(plus + 1, end - plus - 1), out y))
                return new IslandRect(0, 0, 0, 0);
            return new IslandRect(x, y, w, h);
        }

        static class Native
        {
            [DllImport(Lib, EntryPoint = "Island_Apply")]
            public static extern int Apply(int pid, int x, int y, int w, int h, int flags);

            [DllImport(Lib, EntryPoint = "Island_Move")]
            public static extern int Move(int x, int y);

            [DllImport(Lib, EntryPoint = "Island_SetVisible")]
            public static extern int SetVisible(int visible);

            [DllImport(Lib, EntryPoint = "Island_SetShape")]
            public static extern int SetShape(int[] xywh, int count);

            [DllImport(Lib, EntryPoint = "Island_BeginDrag")]
            public static extern int BeginDrag();

            [DllImport(Lib, EntryPoint = "Island_Drag")]
            public static extern int Drag();

            [DllImport(Lib, EntryPoint = "Island_EndDrag")]
            public static extern int EndDrag();

            [DllImport(Lib, EntryPoint = "Island_GetScreens")]
            public static extern int GetScreens(int[] xywh, int max);

            [DllImport(Lib, EntryPoint = "Island_Pointer")]
            public static extern int Pointer(out int x, out int y);

            [DllImport(Lib, EntryPoint = "Island_Overlay")]
            public static extern int Overlay(int x, int y, int w, int h);

            [DllImport(Lib, EntryPoint = "Island_ArmEdge")]
            public static extern int ArmEdge(int x, int y, int w, int h);

            [DllImport(Lib, EntryPoint = "Island_XdndPoll")]
            public static extern int XdndPoll(byte[] buf, int n);

            [DllImport(Lib, EntryPoint = "Island_DragLive")]
            public static extern int DragLive();

            [DllImport(Lib, EntryPoint = "Island_WantQuit")]
            public static extern int WantQuit();
        }
    }

    static class IslandLinuxBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
            IslandChrome.Register(new IslandLinuxChrome());
#endif
        }
    }
}

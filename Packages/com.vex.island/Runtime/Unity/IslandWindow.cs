using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Vex.Island
{
    /// <summary>
    /// Player-facing chrome facade. OS work lives in IIslandChrome.
    /// Never touches the Editor host window.
    /// </summary>
    public static class IslandWindow
    {
        public const int Width = IslandMetrics.Width;
        public const int Height = IslandMetrics.Height;
        public const int Radius = IslandMetrics.Radius;
        public const int TopMargin = IslandMetrics.TopMargin;

        public const int FlagBorderless = IslandChromeFlags.Borderless;
        public const int FlagTopmost = IslandChromeFlags.Topmost;
        public const int FlagSkipTaskbar = IslandChromeFlags.SkipTaskbar;
        public const int FlagShape = IslandChromeFlags.Shape;

        public static int X { get; private set; }
        public static int Y { get; private set; }
        public static bool Applied { get; private set; }
        public static string LastReport { get; private set; } = "";
        public static string Backend { get; private set; } = "";

        static IIslandChrome Chrome => IslandChrome.Current;

        public static string StatusLabel
        {
            get
            {
                if (Applied)
                    return "live";
                if (Backend == "wayland")
                    return "wayland";
                return "chrome-pending";
            }
        }

        public static bool Apply()
        {
#if UNITY_EDITOR
            LastReport = "skipped=editor";
            Applied = false;
            return false;
#else
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.SetResolution(Width, Height, FullScreenMode.Windowed);

            var flags = FlagBorderless | FlagTopmost | FlagSkipTaskbar;
            var screens = QueryScreens();
            int px = 0, py = 0;
            TryPointer(out px, out py);
            var edge = IslandLayout.NearerOuter(screens, px);
            var place = IslandLayout.Dock(screens, px, py, edge, IslandSpan.VirtualDesktop,
                Width, Height, TopMargin);
            X = place.X;
            Y = place.Y;
            Applied = Chrome.Apply(X, Y, Width, Height, flags);
            Backend = Chrome.Id;
            if (!Applied && Backend == "none")
                LastReport = "unsupported=" + Application.platform;
            WriteReport();
            return Applied;
#endif
        }

        public static void BeginDrag() => Chrome.BeginDrag();
        public static void Drag() => Chrome.Drag();
        public static void EndDrag() => Chrome.EndDrag();

        public static void Move(int x, int y)
        {
            X = x;
            Y = y;
            Chrome.Move(x, y);
        }

        public static void SetVisible(bool visible) => Chrome.SetVisible(visible);

        public static IslandRect[] QueryScreens()
        {
            var screens = Chrome.QueryScreens();
            if (screens != null && screens.Length > 0)
                return screens;
            return FallbackScreens();
        }

        public static void ApplyShape()
        {
            var rects = new int[Height * 4];
            var n = IslandShape.BuildRoundedRect(Width, Height, Radius, rects);
            if (n > 0)
                Chrome.SetShape(rects, n);
        }

        public static void ClearShape() => Chrome.SetShape(null, 0);

        public static void PlaceDropTarget(int x, int y) => Chrome.Overlay(x, y, Width, Height);

        public static void ArmEdges()
        {
            var screens = QueryScreens();
            if (screens == null || screens.Length == 0)
                return;
            var left = IslandLayout.Leftmost(screens);
            var right = IslandLayout.Rightmost(screens);
            const int sliver = 16;
            Chrome.ArmEdge(left.X, left.Y, sliver, left.H);
            Chrome.ArmEdge(right.X + right.W - sliver, right.Y, sliver, right.H);
        }

        public static void OverlayHide() => Chrome.Overlay(0, 0, 0, 0);
        public static bool DragLive() => Chrome.DragLive;
        public static bool WantQuit() => Chrome.WantQuit;
        public static string[] PollDrop() => Chrome.PollDrop() ?? Array.Empty<string>();
        public static string DecodeFileToken(string raw) => IslandPaths.DecodeFileToken(raw);

        public static bool TryPointer(out int x, out int y) => Chrome.TryPointer(out x, out y);

        static void WriteReport()
        {
            LastReport =
                "ok=" + (Applied ? "1" : "0") +
                " backend=" + Backend +
                " platform=" + Application.platform +
                " pid=" + Process.GetCurrentProcess().Id +
                " pos=" + X + "," + Y +
                " size=" + Width + "x" + Height;
            try
            {
                var dir = Application.persistentDataPath;
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "island-apply.txt"), LastReport + "\n");
            }
            catch (Exception e)
            {
                LastReport += " write_err=" + e.GetType().Name;
            }
        }

        static IslandRect[] FallbackScreens()
        {
            var w = Display.main.systemWidth;
            var h = Display.main.systemHeight;
            if (w <= 0) w = 1920;
            if (h <= 0) h = 1080;
            return new[] { new IslandRect(0, 0, w, h) };
        }
    }
}

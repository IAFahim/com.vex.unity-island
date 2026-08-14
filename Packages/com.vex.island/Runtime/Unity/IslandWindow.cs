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
        public static int FrameW { get; private set; } = IslandMetrics.Width;
        public static int FrameH { get; private set; } = IslandMetrics.Height;
        public static int CapX { get; private set; }
        public static int CapY { get; private set; }
        public static bool Applied { get; private set; }
        public static bool Mapped { get; private set; }
        public static bool Covering { get; private set; }
        public static string LastReport { get; private set; } = "";
        public static string Backend { get; private set; } = "";

        static bool _haveShape;
        static bool _shapeOpen;
        static IslandEdge _shapeEdge;
        static int _shapeLx, _shapeLy;
        static bool _sized;
        static IslandRect[] _screens;
        static float _screensAt = -1;

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
            var screens = QueryScreens();
            int px = 0, py = 0;
            TryPointer(out px, out py);
            var edge = IslandLayout.NearerOuter(screens, px);
            var place = IslandLayout.Dock(screens, px, py, edge, IslandSpan.VirtualDesktop,
                IslandMetrics.OpenWidth, Height, TopMargin);
            Cover(place.Bound);
            var flags = FlagBorderless | FlagTopmost | FlagSkipTaskbar;
            Applied = Chrome.Apply(X, Y, FrameW, FrameH, flags);
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
            if (X == x && Y == y)
                return;
            X = x;
            Y = y;
            Chrome.Move(x, y);
        }

        public static void SetVisible(bool visible)
        {
            if (Mapped == visible && Applied)
                return;
            Mapped = visible;
            Chrome.SetVisible(visible);
        }

        public static IslandRect[] QueryScreens()
        {
            var now = Time.unscaledTime;
            if (_screens != null && _screens.Length > 0 && now - _screensAt < 0.5f)
                return _screens;
            var screens = Chrome.QueryScreens();
            if (screens == null || screens.Length == 0)
                screens = FallbackScreens();
            _screens = screens;
            _screensAt = now;
            return screens;
        }

        public static void Present(IslandPlacement cap, bool open)
        {
            int lx, ly;
            IslandLayout.Inside(cap.Bound, cap.X, cap.Y, out lx, out ly);
            CapX = lx;
            CapY = ly;
#if UNITY_EDITOR
            Covering = false;
            PlaceCapsule(cap.X, cap.Y, open, cap.Edge);
#else
            Covering = true;
            Cover(cap.Bound);
            if (!Applied)
            {
                Chrome.Apply(X, Y, FrameW, FrameH, FlagBorderless | FlagTopmost | FlagSkipTaskbar);
                Applied = Chrome.Id != "none";
            }
            ApplyShape(open, cap.Edge, lx, ly, cap.Bound.W, cap.Bound.H);
#endif
        }

        public static void Place(int x, int y, bool open, IslandEdge edge)
        {
            PlaceCapsule(x, y, open, edge);
        }

        static void PlaceCapsule(int x, int y, bool open, IslandEdge edge)
        {
            FrameW = IslandMetrics.OpenWidth;
            FrameH = Height;
            if (!_sized)
            {
                Screen.SetResolution(FrameW, FrameH, FullScreenMode.Windowed);
#if UNITY_EDITOR
                FitEditorView(FrameW, FrameH);
#endif
                _sized = true;
            }
#if !UNITY_EDITOR
            if (!Applied)
            {
                Chrome.Apply(x, y, FrameW, FrameH, FlagBorderless | FlagTopmost | FlagSkipTaskbar);
                Applied = Chrome.Id != "none";
            }
#endif
            Move(x, y);
            ApplyShape(open, edge, 0, 0, FrameW, FrameH);
        }

        public static void Cover(IslandRect bound)
        {
            if (FrameW == bound.W && FrameH == bound.H && X == bound.X && Y == bound.Y && _sized)
                return;
            FrameW = bound.W;
            FrameH = bound.H;
            X = bound.X;
            Y = bound.Y;
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.SetResolution(bound.W, bound.H, FullScreenMode.Windowed);
            _sized = true;
            Chrome.Apply(bound.X, bound.Y, bound.W, bound.H, FlagBorderless | FlagTopmost | FlagSkipTaskbar);
            Applied = Chrome.Id != "none" || Applied;
        }

        public static void ApplyShape()
        {
            ApplyShape(true, IslandEdge.Left);
        }

        public static void ApplyShape(bool open, IslandEdge edge)
        {
            ApplyShape(open, edge, CapX, CapY, FrameW, FrameH);
        }

        public static void ApplyShape(bool open, IslandEdge edge, int localX, int localY, int hostW, int hostH)
        {
            CapX = localX;
            CapY = localY;
            if (_haveShape && _shapeOpen == open && _shapeEdge == edge
                && _shapeLx == localX && _shapeLy == localY
                && FrameW == hostW && FrameH == hostH)
                return;

            if (open && hostW >= IslandMetrics.OpenWidth && hostH >= Height)
            {
                var full = new[] { 0, 0, hostW, hostH };
                Chrome.SetShape(full, 1);
            }
            else if (open)
            {
                var rects = new int[Height * 4];
                var n = IslandShape.BuildRoundedRect(IslandMetrics.OpenWidth, Height, Radius, rects);
                Offset(rects, n, localX, localY);
                if (n > 0)
                    Chrome.SetShape(rects, n);
            }
            else
            {
                var tmp = new int[Height * 4];
                var count = IslandShape.BuildRoundedRect(Width, Height, Radius, tmp);
                var ox = localX + (edge == IslandEdge.Right ? IslandMetrics.OpenWidth - Width : 0);
                Offset(tmp, count, ox, localY);
                Chrome.SetShape(tmp, count);
            }

            _haveShape = true;
            _shapeOpen = open;
            _shapeEdge = edge;
            _shapeLx = localX;
            _shapeLy = localY;
        }

        static void Offset(int[] xywh, int count, int ox, int oy)
        {
            for (var i = 0; i < count; i++)
            {
                xywh[i * 4] += ox;
                xywh[i * 4 + 1] += oy;
            }
        }

#if UNITY_EDITOR
        static void FitEditorView(int w, int h)
        {
            try
            {
                var t = Type.GetType("UnityEditor.PlayModeWindow,UnityEditor");
                var m = t?.GetMethod("SetCustomRenderingResolution");
                m?.Invoke(null, new object[] { (uint)w, (uint)h, "Island" });
            }
            catch
            {
            }
        }
#endif

        public static void ClearShape()
        {
            if (!_haveShape)
                return;
            Chrome.SetShape(null, 0);
            _haveShape = false;
        }

        public static void PlaceDropTarget(int x, int y)
        {
            var w = _shapeOpen ? IslandMetrics.OpenWidth : Width;
            Chrome.Overlay(x, y, w, Height);
        }

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

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Vex.Island
{
    /// <summary>
    /// Borderless, topmost, shaped island chrome. Player builds only —
    /// never touch the Editor host window.
    /// </summary>
    public static class IslandWindow
    {
        public const int Width = 420;
        public const int Height = 88;
        public const int Radius = 28;
        public const int TopMargin = 14;

        public const int FlagBorderless = 1;
        public const int FlagTopmost = 2;
        public const int FlagSkipTaskbar = 4;
        public const int FlagShape = 8;

        public static int X { get; private set; }
        public static int Y { get; private set; }
        public static bool Applied { get; private set; }
        public static string LastReport { get; private set; } = "";
        public static string Backend { get; private set; } = "";

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

            var flags = FlagBorderless | FlagTopmost | FlagSkipTaskbar | FlagShape;
            var screens = QueryScreens();
            int px = 0, py = 0;
            TryPointer(out px, out py);
            var place = IslandLayout.Dock(screens, px, py, IslandEdge.Top, IslandSpan.ActiveMonitor,
                Width, Height, TopMargin);
            X = place.X;
            Y = place.Y;
#if UNITY_STANDALONE_WIN
            Applied = Win32Apply(flags);
#elif UNITY_STANDALONE_LINUX
            Applied = LinuxApply(flags);
#else
            Applied = false;
            LastReport = "unsupported=" + Application.platform;
#endif
            WriteReport();
            return Applied;
#endif
        }

        public static void BeginDrag()
        {
#if UNITY_EDITOR
            return;
#elif UNITY_STANDALONE_WIN
            Win32BeginMove();
#elif UNITY_STANDALONE_LINUX
            Linux_BeginDrag();
#endif
        }

        public static void Drag()
        {
#if UNITY_EDITOR
            return;
#elif UNITY_STANDALONE_LINUX
            Linux_Drag();
#endif
        }

        public static void EndDrag()
        {
#if UNITY_EDITOR
            return;
#elif UNITY_STANDALONE_LINUX
            Linux_EndDrag();
#endif
        }

        public static void Move(int x, int y)
        {
            X = x;
            Y = y;
#if UNITY_EDITOR
            return;
#elif UNITY_STANDALONE_WIN
            Win32Move(x, y);
#elif UNITY_STANDALONE_LINUX
            Linux_Move(x, y);
#endif
        }

        public static void SetVisible(bool visible)
        {
#if UNITY_EDITOR
            return;
#elif UNITY_STANDALONE_WIN
            Win32Visible(visible);
#elif UNITY_STANDALONE_LINUX
            Linux_SetVisible(visible ? 1 : 0);
#endif
        }

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

        public static IslandRect[] QueryScreens()
        {
#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
            var buf = new int[32 * 4];
            var n = Linux_GetScreens(buf, 32);
            if (n <= 0)
                return FallbackScreens();
            var rects = new IslandRect[n];
            for (var i = 0; i < n; i++)
                rects[i] = new IslandRect(buf[i * 4], buf[i * 4 + 1], buf[i * 4 + 2], buf[i * 4 + 3]);
            return rects;
#else
            return FallbackScreens();
#endif
        }

        public static bool TryPointer(out int x, out int y)
        {
            x = 0;
            y = 0;
#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
            return Linux_Pointer(out x, out y) == 1;
#else
            return false;
#endif
        }

        static IslandRect[] FallbackScreens()
        {
            var w = Display.main.systemWidth;
            var h = Display.main.systemHeight;
            if (w <= 0) w = 1920;
            if (h <= 0) h = 1080;
            return new[] { new IslandRect(0, 0, w, h) };
        }

#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
        const string Lib = "island";

        [DllImport(Lib, EntryPoint = "Island_Apply")]
        static extern int Linux_Apply(int pid, int x, int y, int w, int h, int flags);

        [DllImport(Lib, EntryPoint = "Island_Move")]
        static extern int Linux_Move(int x, int y);

        [DllImport(Lib, EntryPoint = "Island_SetVisible")]
        static extern int Linux_SetVisible(int visible);

        [DllImport(Lib, EntryPoint = "Island_SetShape")]
        static extern int Linux_SetShape(int[] xywh, int count);

        [DllImport(Lib, EntryPoint = "Island_BeginDrag")]
        static extern int Linux_BeginDrag();

        [DllImport(Lib, EntryPoint = "Island_Drag")]
        static extern int Linux_Drag();

        [DllImport(Lib, EntryPoint = "Island_EndDrag")]
        static extern int Linux_EndDrag();

        [DllImport(Lib, EntryPoint = "Island_GetScreens")]
        static extern int Linux_GetScreens(int[] xywh, int max);

        [DllImport(Lib, EntryPoint = "Island_Pointer")]
        static extern int Linux_Pointer(out int x, out int y);

        static bool LinuxApply(int flags)
        {
            var pid = Process.GetCurrentProcess().Id;
            var rc = Linux_Apply(pid, X, Y, Width, Height, flags);
            if (rc == 1 && (flags & FlagShape) != 0)
            {
                var rects = new int[Height * 4];
                var n = IslandShape.BuildRoundedRect(Width, Height, Radius, rects);
                if (n > 0)
                    Linux_SetShape(rects, n);
            }

            if (rc == 1)
                Backend = "x11";
            else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                Backend = "wayland";
            else
                Backend = "none";

            LastReport = "linux_rc=" + rc + " backend=" + Backend;
            return rc == 1;
        }
#endif

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        const int GWL_STYLE = -16;
        const int GWL_EXSTYLE = -20;
        const uint WS_POPUP = 0x80000000;
        const uint WS_VISIBLE = 0x10000000;
        const uint WS_EX_LAYERED = 0x00080000;
        const uint WS_EX_TOPMOST = 0x00000008;
        const uint WS_EX_TOOLWINDOW = 0x00000080;
        const uint SWP_FRAMECHANGED = 0x0020;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_SHOWWINDOW = 0x0040;
        const uint SWP_NOACTIVATE = 0x0010;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const int RGN_OR = 2;

        const uint WM_NCLBUTTONDOWN = 0x00A1;
        const int HTCAPTION = 2;

        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
        [DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr ho);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref Margins pMarInset);

        struct Margins
        {
            public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight;
        }

        static IntPtr _hwnd;

        static bool Win32Apply(int flags)
        {
            _hwnd = GetActiveWindow();
            if (_hwnd == IntPtr.Zero)
            {
                LastReport = "win32=no_hwnd";
                return false;
            }

            SetWindowLong(_hwnd, GWL_STYLE, WS_POPUP | WS_VISIBLE);
            uint ex = WS_EX_LAYERED | WS_EX_TOOLWINDOW;
            if ((flags & FlagTopmost) != 0)
                ex |= WS_EX_TOPMOST;
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex);

            var margins = new Margins
            {
                cxLeftWidth = -1,
                cxRightWidth = -1,
                cyTopHeight = -1,
                cyBottomHeight = -1
            };
            DwmExtendFrameIntoClientArea(_hwnd, ref margins);

            SetWindowPos(_hwnd, HWND_TOPMOST, X, Y, Width, Height,
                SWP_FRAMECHANGED | SWP_SHOWWINDOW);

            if ((flags & FlagShape) != 0)
                Win32Shape();

            LastReport = "win32=ok hwnd=" + _hwnd.ToInt64();
            return true;
        }

        static void Win32Shape()
        {
            var rects = new int[Height * 4];
            var n = IslandShape.BuildRoundedRect(Width, Height, Radius, rects);
            if (n <= 0)
                return;
            var acc = CreateRectRgn(0, 0, 0, 0);
            for (var i = 0; i < n; i++)
            {
                var o = i * 4;
                var piece = CreateRectRgn(rects[o], rects[o + 1], rects[o] + rects[o + 2], rects[o + 1] + rects[o + 3]);
                CombineRgn(acc, acc, piece, RGN_OR);
                DeleteObject(piece);
            }

            SetWindowRgn(_hwnd, acc, true);
        }

        static void Win32Move(int x, int y)
        {
            if (_hwnd == IntPtr.Zero)
                return;
            SetWindowPos(_hwnd, HWND_TOPMOST, x, y, Width, Height, SWP_SHOWWINDOW);
        }

        static void Win32Visible(bool visible)
        {
            if (_hwnd == IntPtr.Zero)
                return;
            ShowWindow(_hwnd, visible ? 5 : 0);
        }

        static void Win32BeginMove()
        {
            if (_hwnd == IntPtr.Zero)
                return;
            ReleaseCapture();
            SendMessage(_hwnd, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
        }
#endif
    }
}

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Vex.Island
{
    public sealed class IslandWindowsChrome : IIslandChrome
    {
        const int GWL_STYLE = -16;
        const int GWL_EXSTYLE = -20;
        const uint WS_POPUP = 0x80000000;
        const uint WS_VISIBLE = 0x10000000;
        const uint WS_EX_LAYERED = 0x00080000;
        const uint WS_EX_TOPMOST = 0x00000008;
        const uint WS_EX_TOOLWINDOW = 0x00000080;
        const uint SWP_FRAMECHANGED = 0x0020;
        const uint SWP_SHOWWINDOW = 0x0040;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const int RGN_OR = 2;
        const uint WM_NCLBUTTONDOWN = 0x00A1;
        const int HTCAPTION = 2;

        IntPtr _hwnd;

        public string Id => "win32";
        public bool DragLive => false;
        public bool WantQuit => false;

        public bool Apply(int x, int y, int w, int h, int flags)
        {
            _hwnd = GetActiveWindow();
            if (_hwnd == IntPtr.Zero)
                return false;

            SetWindowLong(_hwnd, GWL_STYLE, WS_POPUP | WS_VISIBLE);
            uint ex = WS_EX_LAYERED | WS_EX_TOOLWINDOW;
            if ((flags & IslandChromeFlags.Topmost) != 0)
                ex |= WS_EX_TOPMOST;
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex);

            var margins = new Margins { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(_hwnd, ref margins);
            SetWindowPos(_hwnd, HWND_TOPMOST, x, y, w, h, SWP_FRAMECHANGED | SWP_SHOWWINDOW);

            if ((flags & IslandChromeFlags.Shape) != 0)
                ApplyWin32Shape(w, h);
            return true;
        }

        public void Move(int x, int y)
        {
            if (_hwnd == IntPtr.Zero)
                return;
            SetWindowPos(_hwnd, HWND_TOPMOST, x, y, IslandMetrics.Width, IslandMetrics.Height, SWP_SHOWWINDOW);
        }

        public void SetVisible(bool visible)
        {
            if (_hwnd == IntPtr.Zero)
                return;
            ShowWindow(_hwnd, visible ? 5 : 0);
        }

        public void SetShape(int[] xywh, int count)
        {
            if (_hwnd == IntPtr.Zero || xywh == null || count <= 0)
                return;
            var acc = CreateRectRgn(0, 0, 0, 0);
            for (var i = 0; i < count; i++)
            {
                var o = i * 4;
                var piece = CreateRectRgn(xywh[o], xywh[o + 1], xywh[o] + xywh[o + 2], xywh[o + 1] + xywh[o + 3]);
                CombineRgn(acc, acc, piece, RGN_OR);
                DeleteObject(piece);
            }

            SetWindowRgn(_hwnd, acc, true);
        }

        public void BeginDrag()
        {
            if (_hwnd == IntPtr.Zero)
                return;
            ReleaseCapture();
            SendMessage(_hwnd, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
        }

        public void Drag() { }
        public void EndDrag() { }

        public bool TryPointer(out int x, out int y)
        {
            x = 0;
            y = 0;
            return false;
        }

        public IslandRect[] QueryScreens() => Array.Empty<IslandRect>();
        public void Overlay(int x, int y, int w, int h) { }
        public void ArmEdge(int x, int y, int w, int h) { }
        public string[] PollDrop() => Array.Empty<string>();

        void ApplyWin32Shape(int w, int h)
        {
            var rects = new int[h * 4];
            var n = IslandShape.BuildRoundedRect(w, h, IslandMetrics.Radius, rects);
            SetShape(rects, n);
        }

        struct Margins
        {
            public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight;
        }

        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
        [DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr ho);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref Margins pMarInset);
    }

    static class IslandWindowsBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            IslandChrome.Register(new IslandWindowsChrome());
#endif
        }
    }
}

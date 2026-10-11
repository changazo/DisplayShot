using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace DisplayShot
{
    /// <summary>Window auto-detection: every visible top-level window (in Z-order) followed by all its
    /// child windows. Built once, before the screen is captured.</summary>
    internal sealed class WindowList
    {
        struct Entry
        {
            public Rectangle Rect;   // overlay client coordinates
            public long Area;
            public int ZIndex;       // 1-based index of the top-level window in Z-order (1 = topmost); children inherit it
        }

        readonly List<Entry> entries = new List<Entry>();
        readonly Point origin;
        int topLevelIndex;

        /// <param name="virtualScreen">Overlay bounds; rectangles are stored relative to its top-left.</param>
        public WindowList(Rectangle virtualScreen)
        {
            origin = virtualScreen.Location;
            Native.EnumWindows(OnTopLevel, IntPtr.Zero);
            // Desktop record: the whole overlay, last resort when nothing else contains the point.
            Entry desktop = new Entry();
            desktop.Rect = new Rectangle(Point.Empty, virtualScreen.Size);
            desktop.Area = (long)virtualScreen.Width * virtualScreen.Height;
            desktop.ZIndex = topLevelIndex;
            entries.Add(desktop);
        }

        bool OnTopLevel(IntPtr hWnd, IntPtr lParam)
        {
            if (!Native.IsWindowVisible(hWnd) || !IsWindowEnabled(hWnd) || IsCloaked(hWnd)) return true;
            if (IsFullyCovered(hWnd)) return true;
            Native.RECT r;
            if (!Native.GetWindowRect(hWnd, out r)) return true;
            if (Native.IsIconic(hWnd)) return true;   // minimized windows sit at (-32000, -32000)
            topLevelIndex++;
            Add(r, topLevelIndex);
            Native.EnumChildWindows(hWnd, OnChild, IntPtr.Zero);   // all descendants, pre-order
            return true;
        }

        bool OnChild(IntPtr hWnd, IntPtr lParam)
        {
            if (!Native.IsWindowVisible(hWnd) || !IsWindowEnabled(hWnd)) return true;
            Native.RECT r;
            if (!Native.GetWindowRect(hWnd, out r)) return true;
            if ((long)(r.Right - r.Left) * (r.Bottom - r.Top) == 0) return true;
            Add(r, topLevelIndex);
            return true;
        }

        void Add(Native.RECT r, int zIndex)
        {
            Entry e = new Entry();
            e.Rect = Rectangle.FromLTRB(r.Left - origin.X, r.Top - origin.Y, r.Right - origin.X, r.Bottom - origin.Y);
            e.Area = (long)e.Rect.Width * e.Rect.Height;
            e.ZIndex = zIndex;
            entries.Add(e);
        }

        /// <summary>Among the rectangles containing the point, those of the topmost top-level window win,
        /// then the smallest area (so a browser's content area beats its frame). Empty if none.</summary>
        public Rectangle HitTest(Point p)
        {
            bool found = false;
            Entry best = new Entry();
            foreach (Entry e in entries)
            {
                if (!e.Rect.Contains(p)) continue;   // right/bottom exclusive, like PtInRect
                if (!found || e.ZIndex < best.ZIndex || (e.ZIndex == best.ZIndex && e.Area < best.Area))
                {
                    best = e;
                    found = true;
                }
            }
            return found ? best.Rect : Rectangle.Empty;
        }

        /// <summary>True when the union of the visible windows above this top-level window covers it completely.</summary>
        static bool IsFullyCovered(IntPtr hWnd)
        {
            if ((Native.GetWindowLong(hWnd, Native.GWL_STYLE) & Native.WS_CHILD) != 0) return false;
            Native.RECT r;
            if (!Native.GetWindowRect(hWnd, out r)) return false;
            IntPtr target = CreateRectRgn(r.Left, r.Top, r.Right, r.Bottom);
            IntPtr acc = CreateRectRgn(0, 0, 0, 0);
            IntPtr diff = CreateRectRgn(0, 0, 0, 0);
            try
            {
                for (IntPtr w = GetWindow(hWnd, GW_HWNDPREV); w != IntPtr.Zero; w = GetWindow(w, GW_HWNDPREV))
                {
                    if (!Native.IsWindowVisible(w) || IsCloaked(w)) continue;
                    Native.RECT wr;
                    if (!Native.GetWindowRect(w, out wr)) continue;
                    IntPtr wrgn = CreateRectRgn(wr.Left, wr.Top, wr.Right, wr.Bottom);
                    CombineRgn(acc, acc, wrgn, RGN_OR);
                    DeleteObject(wrgn);
                    if (CombineRgn(diff, target, acc, RGN_DIFF) == NULLREGION) return true;
                }
                return false;
            }
            finally
            {
                DeleteObject(target); DeleteObject(acc); DeleteObject(diff);
            }
        }

        /// <summary>DWM-cloaked windows (suspended UWP apps, other virtual desktops) are invisible but report
        /// IsWindowVisible = true; skipping them avoids highlighting ghosts.</summary>
        static bool IsCloaked(IntPtr hWnd)
        {
            int cloaked;
            return Native.DwmGetWindowAttribute(hWnd, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0;
        }

        const int GW_HWNDPREV = 3, RGN_OR = 2, RGN_DIFF = 4, NULLREGION = 1;
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, int cmd);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
        [DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr dst, IntPtr a, IntPtr b, int mode);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    }
}

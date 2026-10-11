using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DisplayShot
{
    /// <summary>A 32-bpp DIB section selected into a memory DC. Annotations are drawn with plain GDI
    /// (pens, Rectangle, Ellipse, LineTo) and OnPaint BitBlts from these surfaces, which is much faster
    /// than GDI+ for a desktop-sized image.</summary>
    internal sealed class GdiSurface : IDisposable
    {
        public readonly int Width, Height;
        public IntPtr Hdc { get; private set; }
        IntPtr hBitmap, oldBitmap;

        public GdiSurface(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            BITMAPINFO bmi = new BITMAPINFO();
            bmi.biSize = Marshal.SizeOf(typeof(BITMAPINFO));
            bmi.biWidth = Width;
            bmi.biHeight = -Height;   // top-down
            bmi.biPlanes = 1;
            bmi.biBitCount = 32;
            IntPtr bits;
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            Hdc = Native.CreateCompatibleDC(screen);
            Native.ReleaseDC(IntPtr.Zero, screen);
            hBitmap = CreateDIBSection(Hdc, ref bmi, 0, out bits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero) throw new OutOfMemoryException("CreateDIBSection failed");
            oldBitmap = Native.SelectObject(Hdc, hBitmap);
        }

        public static GdiSurface FromBitmap(Bitmap bmp)
        {
            GdiSurface s = new GdiSurface(bmp.Width, bmp.Height);
            using (Graphics g = Graphics.FromHdc(s.Hdc)) g.DrawImageUnscaled(bmp, 0, 0);
            return s;
        }

        /// <summary>Copies a region of another surface into a new surface (1:1).</summary>
        public static GdiSurface Crop(GdiSurface src, Rectangle r)
        {
            GdiSurface s = new GdiSurface(r.Width, r.Height);
            Native.BitBlt(s.Hdc, 0, 0, r.Width, r.Height, src.Hdc, r.X, r.Y, Native.SRCCOPY);
            return s;
        }

        public void CopyTo(IntPtr hdcDest, int x, int y, int w, int h, int srcX, int srcY)
        {
            Native.BitBlt(hdcDest, x, y, w, h, Hdc, srcX, srcY, Native.SRCCOPY);
        }

        /// <summary>Color of one pixel; Color.Empty outside the surface.</summary>
        public Color GetPixel(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return Color.Empty;
            int c = GetPixel(Hdc, x, y);
            return Color.FromArgb(c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF);
        }

        /// <summary>Snapshot as a GDI+ bitmap (for the clipboard and for saving).</summary>
        public Bitmap ToBitmap()
        {
            Bitmap bmp = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr dst = g.GetHdc();
                Native.BitBlt(dst, 0, 0, Width, Height, Hdc, 0, 0, Native.SRCCOPY);
                g.ReleaseHdc(dst);
            }
            return bmp;
        }

        public void Dispose()
        {
            if (Hdc == IntPtr.Zero) return;
            Native.SelectObject(Hdc, oldBitmap);
            Native.DeleteObject(hBitmap);
            Native.DeleteDC(Hdc);
            Hdc = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
            public int bmiColors;
        }

        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, int usage, out IntPtr bits, IntPtr section, int offset);
        [DllImport("gdi32.dll")] static extern int GetPixel(IntPtr hdc, int x, int y);
    }

    /// <summary>Small GDI drawing helpers.</summary>
    internal static class Gdi
    {
        public const int PS_SOLID = 0, NULL_BRUSH = 5, TRANSPARENT = 1, COLORONCOLOR = 3, HALFTONE = 4, R2_COPYPEN = 13;

        public static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        public static void FillRect(IntPtr hdc, Rectangle r, Color c)
        {
            IntPtr brush = CreateSolidBrush(ColorRef(c));
            Native.RECT rc = new Native.RECT { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
            FillRect(hdc, ref rc, brush);
            Native.DeleteObject(brush);
        }

        /// <summary>Runs <paramref name="draw"/> with a solid pen of the given width/colour and a hollow brush selected.</summary>
        public static void WithPen(IntPtr hdc, int width, Color c, Action draw)
        {
            IntPtr pen = CreatePen(PS_SOLID, width, ColorRef(c));
            IntPtr oldPen = Native.SelectObject(hdc, pen);
            IntPtr oldBrush = Native.SelectObject(hdc, GetStockObject(NULL_BRUSH));
            int oldRop = SetROP2(hdc, R2_COPYPEN);
            try { draw(); }
            finally
            {
                SetROP2(hdc, oldRop);
                Native.SelectObject(hdc, oldBrush);
                Native.SelectObject(hdc, oldPen);
                Native.DeleteObject(pen);
            }
        }

        /// <summary>Filled polygon with a 1-px outline in the same colour (arrowheads).</summary>
        public static void FillPolygon(IntPtr hdc, Point[] pts, Color c)
        {
            IntPtr brush = CreateSolidBrush(ColorRef(c));
            IntPtr pen = CreatePen(PS_SOLID, 1, ColorRef(c));
            IntPtr oldBrush = Native.SelectObject(hdc, brush), oldPen = Native.SelectObject(hdc, pen);
            Native.POINT[] p = new Native.POINT[pts.Length];
            for (int i = 0; i < pts.Length; i++) p[i] = new Native.POINT(pts[i].X, pts[i].Y);
            Polygon(hdc, p, p.Length);
            Native.SelectObject(hdc, oldBrush);
            Native.SelectObject(hdc, oldPen);
            Native.DeleteObject(brush);
            Native.DeleteObject(pen);
        }

        public static void Line(IntPtr hdc, int x1, int y1, int x2, int y2)
        {
            MoveToEx(hdc, x1, y1, IntPtr.Zero);
            LineTo(hdc, x2, y2);
        }

        [DllImport("gdi32.dll")] public static extern IntPtr CreatePen(int style, int width, int color);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateSolidBrush(int color);
        [DllImport("gdi32.dll")] public static extern IntPtr GetStockObject(int obj);
        [DllImport("gdi32.dll")] public static extern int SetROP2(IntPtr hdc, int rop);
        [DllImport("gdi32.dll")] public static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr old);
        [DllImport("gdi32.dll")] public static extern bool LineTo(IntPtr hdc, int x, int y);
        [DllImport("gdi32.dll")] public static extern bool Rectangle(IntPtr hdc, int l, int t, int r, int b);
        [DllImport("gdi32.dll")] public static extern bool Ellipse(IntPtr hdc, int l, int t, int r, int b);
        [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")] public static extern int SetBkMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] public static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr old);
        [DllImport("gdi32.dll")] public static extern bool Polygon(IntPtr hdc, Native.POINT[] pts, int count);
        [DllImport("gdi32.dll")] public static extern int IntersectClipRect(IntPtr hdc, int l, int t, int r, int b);
        [DllImport("gdi32.dll")] public static extern int SaveDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool RestoreDC(IntPtr hdc, int saved);
        [DllImport("user32.dll")] static extern int FillRect(IntPtr hdc, ref Native.RECT rc, IntPtr brush);
    }
}

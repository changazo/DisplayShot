using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace DisplayShot
{
    internal static class Capture
    {
        /// <summary>Copies the whole virtual desktop (every monitor, physical pixels) into a new bitmap.
        /// BitBlt from the screen DC with SRCCOPY | CAPTUREBLT, so layered windows are included.</summary>
        public static Bitmap VirtualDesktop(out Rectangle bounds)
        {
            bounds = Native.VirtualScreen;
            Bitmap bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr dst = g.GetHdc();
                IntPtr src = Native.GetDC(IntPtr.Zero);
                try
                {
                    Native.BitBlt(dst, 0, 0, bounds.Width, bounds.Height, src, bounds.X, bounds.Y,
                                  Native.SRCCOPY | Native.CAPTUREBLT);
                }
                finally
                {
                    Native.ReleaseDC(IntPtr.Zero, src);
                    g.ReleaseHdc(dst);
                }
            }
            return bmp;
        }
    }
}

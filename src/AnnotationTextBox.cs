using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DisplayShot
{
    /// <summary>The text tool's editor: a borderless rich-text box that shows the image under it.
    /// It paints the image below itself and lets RichEdit draw only the text on top
    /// (WS_EX_TRANSPARENT + WM_PRINTCLIENT), and it is moved by dragging a 6-px band along its edges.</summary>
    internal sealed class AnnotationTextBox : RichTextBox
    {
        const int EdgeBand = 6;
        const int WM_PAINT = 0x000F, WM_ERASEBKGND = 0x0014, WM_PRINTCLIENT = 0x0318, PRF_CLIENT = 4;
        const int WM_SETCURSOR = 0x0020, WM_LBUTTONDOWN = 0x0201, WM_MOUSEMOVE = 0x0200, WM_LBUTTONUP = 0x0202;

        /// <summary>Paints what lies under the box into a DC sized like the box's client area.</summary>
        public Action<IntPtr, Rectangle> PaintBackground;
        /// <summary>Rectangle (in parent coordinates) the box may be dragged within: the selection.</summary>
        public Func<Rectangle> DragBounds;
        public bool ReEditing;

        bool dragging;
        Point dragOffset;

        static AnnotationTextBox() { LoadLibrary("msftedit.dll"); }

        public AnnotationTextBox()
        {
            BorderStyle = BorderStyle.None;
            ScrollBars = RichTextBoxScrollBars.None;
            Multiline = true;
            WordWrap = false;
            AcceptsTab = true;
            DetectUrls = false;
            ShortcutsEnabled = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassName = "RICHEDIT50W";   // RichEdit 4.1+ honours WS_EX_TRANSPARENT (text only, no background)
                cp.ExStyle |= Native.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        /// <summary>Draws background + text into <paramref name="hdc"/> at (0,0), sized like the client area.</summary>
        public void RenderTo(IntPtr hdc)
        {
            if (PaintBackground != null) PaintBackground(hdc, Bounds);
            SendMessage(Handle, WM_PRINTCLIENT, hdc, (IntPtr)PRF_CLIENT);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_PAINT:
                    PAINTSTRUCT ps;
                    IntPtr hdc = BeginPaint(Handle, out ps);
                    using (GdiSurface buf = new GdiSurface(ClientSize.Width, ClientSize.Height))
                    {
                        RenderTo(buf.Hdc);
                        buf.CopyTo(hdc, 0, 0, buf.Width, buf.Height, 0, 0);
                    }
                    EndPaint(Handle, ref ps);
                    return;
                case WM_ERASEBKGND:
                    m.Result = (IntPtr)1;
                    return;
                case WM_SETCURSOR:
                    if (dragging || InEdgeBand(PointToClient(Cursor.Position)))
                    {
                        Cursor.Current = Cursors.SizeAll;
                        m.Result = (IntPtr)1;
                        return;
                    }
                    break;
                case WM_LBUTTONDOWN:
                    Point p = new Point((short)((long)m.LParam & 0xFFFF), (short)(((long)m.LParam >> 16) & 0xFFFF));
                    if (InEdgeBand(p))
                    {
                        dragging = true;
                        dragOffset = p;
                        Capture = true;
                        return;
                    }
                    break;
                case WM_MOUSEMOVE:
                    if (dragging) { DragTo(Parent.PointToClient(Cursor.Position)); return; }
                    break;
                case WM_LBUTTONUP:
                    if (dragging) { dragging = false; Capture = false; return; }
                    break;
            }
            base.WndProc(ref m);
        }

        bool InEdgeBand(Point p)
        {
            Size s = ClientSize;
            if (p.X < 0 || p.Y < 0 || p.X >= s.Width || p.Y >= s.Height) return false;
            return p.X <= EdgeBand || p.X >= s.Width - EdgeBand || p.Y <= EdgeBand || p.Y >= s.Height - EdgeBand;
        }

        void DragTo(Point parentPoint)
        {
            Rectangle b = DragBounds != null ? DragBounds() : Parent.ClientRectangle;
            int left = Math.Max(b.Left, Math.Min(parentPoint.X - dragOffset.X, b.Right - Width));
            int top = Math.Max(b.Top, Math.Min(parentPoint.Y - dragOffset.Y, b.Bottom - Height));
            if (left != Left || top != Top)
            {
                Location = new Point(left, top);
                Invalidate();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PAINTSTRUCT
        {
            public IntPtr hdc; public int fErase; public int left, top, right, bottom; public int fRestore, fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] reserved;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string name);
        [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT ps);
        [DllImport("user32.dll")] static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
    }
}

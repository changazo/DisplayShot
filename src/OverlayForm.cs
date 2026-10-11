using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DisplayShot
{
    /// <summary>The full-screen capture overlay. One instance per process: it is created after the screen
    /// has been captured, and the process ends when it closes. Everything that isn't an interactive
    /// control (dimmed screenshot, highlight, border, handles, magnifier) is painted by this form with
    /// GDI, from in-memory surfaces.</summary>
    internal sealed partial class OverlayForm : Form
    {
        // Selection lines are two-tone so they stay visible over both light and dark content.
        internal static readonly Color LineDark = Theme.Grape;
        internal static readonly Color LineLight = Theme.Blush;

        const int DimAlpha = 140;           // dimmed screen = screenshot at 140/255 brightness
        const int AntsDash = 6, AntsInterval = 50;

        readonly Rectangle desktop;         // virtual screen, physical pixels
        readonly GdiSurface pristine;       // the untouched screenshot
        readonly GdiSurface dimmed;         // the dimmed copy shown outside the selection
        readonly WindowList windows;

        enum Mode { Hover, Selected }
        Mode mode = Mode.Hover;

        Rectangle hoverRect;                // window under the cursor
        Rectangle frontRect;                // undimmed area shown right now (hovered window or selection)
        int antsPhase;
        DateTime antsLast = DateTime.MinValue;
        Point lastHoverPoint = new Point(int.MinValue, int.MinValue);
        readonly Timer hoverTimer = new Timer();

        public OverlayForm(Bitmap capture, Rectangle desktop, WindowList windows)
        {
            this.desktop = desktop;
            this.windows = windows;
            pristine = GdiSurface.FromBitmap(capture);
            dimmed = BuildDimmed(pristine);

            Text = Program.OverlayTitle;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = desktop;
            KeyPreview = true;
            BackColor = Color.Black;
            Cursor = Cursors.Default;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);

            hoverTimer.Interval = AntsInterval;
            hoverTimer.Tick += delegate { TimerTick(); };
            InitControls();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;   // no taskbar button, not in Alt+Tab
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = desktop;   // WinForms may have adjusted it before the handle existed
            Program.ForceForeground(Handle);
            hoverTimer.Start();
            HoverTick();
            ShowHintIfFirstRun();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            hoverTimer.Stop();
            DisposeTools();
            pristine.Dispose();
            dimmed.Dispose();
            if (front != null) front.Dispose();
            base.OnFormClosed(e);
        }

        static GdiSurface BuildDimmed(GdiSurface src)
        {
            // AlphaBlend the screenshot with constant alpha 140 over black.
            GdiSurface d = new GdiSurface(src.Width, src.Height);
            Gdi.FillRect(d.Hdc, new Rectangle(0, 0, d.Width, d.Height), Color.Black);
            BLENDFUNCTION bf = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = DimAlpha, AlphaFormat = 0 };
            AlphaBlend(d.Hdc, 0, 0, d.Width, d.Height, src.Hdc, 0, 0, src.Width, src.Height, bf);
            return d;
        }

        // ------------------------------------------------------------------------------------------
        // Hover mode: highlight the window under the cursor (polled every 50 ms)

        void TimerTick()
        {
            if (hint != null && (DateTime.Now - hintShownAt).TotalSeconds > 6) CloseHint();
            if (selecting || moving)
            {
                // While the button is held still, the frame turns into marching ants.
                if ((DateTime.Now - lastPressMove).TotalMilliseconds > AntsInterval) StepAnts();
                return;
            }
            if (mode == Mode.Hover) HoverTick();
        }

        void HoverTick()
        {
            if (mode != Mode.Hover || selecting) return;
            // Keep the keyboard focus (Enter/Esc) if another window grabbed the foreground.
            if (GetForegroundWindow() != Handle)
            {
                Program.ForceForeground(Handle);
                if (hint != null) hint.KeepOnTop();
            }
            Point p = PointToClient(Cursor.Position);
            if (p == lastHoverPoint) return;          // nothing to do while the mouse is still
            lastHoverPoint = p;
            if (!ClientRectangle.Contains(p)) p = Point.Empty;

            Rectangle b = windows.HitTest(p);
            b.Intersect(ClientRectangle);
            if (b != hoverRect)
            {
                // The whole old rectangle goes back to dimmed, the whole new one becomes undimmed.
                Invalidate(Rectangle.Inflate(hoverRect, 2, 2));
                hoverRect = b;
                frontRect = b;
                Invalidate(Rectangle.Inflate(hoverRect, 2, 2));
            }
            MoveMagnifier(p);
            StepAnts();
            MoveHint();
        }

        void StepAnts()
        {
            if ((DateTime.Now - antsLast).TotalMilliseconds <= AntsInterval) return;
            antsPhase = antsPhase < 7 ? antsPhase + 2 : 0;
            antsLast = DateTime.Now;
            InvalidateFrame(frontRect);
        }

        /// <summary>Invalidates the band around a rectangle's edges (borders, ants, handles).</summary>
        void InvalidateFrame(Rectangle r)
        {
            if (r.IsEmpty) return;
            Rectangle o = Rectangle.Inflate(r, 8, 8);
            Invalidate(new Rectangle(o.Left, o.Top, o.Width, 16));
            Invalidate(new Rectangle(o.Left, o.Bottom - 16, o.Width, 16));
            Invalidate(new Rectangle(o.Left, o.Top, 16, o.Height));
            Invalidate(new Rectangle(o.Right - 16, o.Top, 16, o.Height));
        }

        // ------------------------------------------------------------------------------------------
        // Painting

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle clip = e.ClipRectangle;
            IntPtr hdc = e.Graphics.GetHdc();
            try
            {
                dimmed.CopyTo(hdc, clip.X, clip.Y, clip.Width, clip.Height, clip.X, clip.Y);
                Rectangle f = Rectangle.Intersect(frontRect, clip);
                if (!f.IsEmpty)
                {
                    if (front != null && mode == Mode.Selected)
                        front.CopyTo(hdc, f.X, f.Y, f.Width, f.Height, f.X - frontRect.X, f.Y - frontRect.Y);
                    else
                        pristine.CopyTo(hdc, f.X, f.Y, f.Width, f.Height, f.X, f.Y);
                }
                if (mode == Mode.Hover && !selecting) PaintAnts(hdc, frontRect);
                else if (!frontRect.IsEmpty || selecting) PaintSelectionFrame(hdc);
                if (magnifierVisible) PaintMagnifier(hdc);
            }
            finally
            {
                e.Graphics.ReleaseHdc(hdc);
            }
            if (magnifierVisible) PaintMagnifierLabels(e.Graphics);
        }

        /// <summary>Marching ants on the rectangle's 2 edge pixels: a dark base with light 6-px dashes
        /// crawling counter-clockwise as the phase advances.</summary>
        void PaintAnts(IntPtr hdc, Rectangle r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            int W = r.Width, H = r.Height, p = antsPhase, d = AntsDash, ox = r.X, oy = r.Y;
            int save = Gdi.SaveDC(hdc);
            Gdi.IntersectClipRect(hdc, r.Left, r.Top, r.Right, r.Bottom);
            Gdi.FillRect(hdc, new Rectangle(ox, oy, W, 2), LineDark);
            Gdi.FillRect(hdc, new Rectangle(ox, oy + H - 2, W, 2), LineDark);
            Gdi.FillRect(hdc, new Rectangle(ox, oy, 2, H), LineDark);
            Gdi.FillRect(hdc, new Rectangle(ox + W - 2, oy, 2, H), LineDark);
            Gdi.WithPen(hdc, 2, LineLight, delegate
            {
                for (int x = p == 0 ? W : W - p; x > 0; x -= 2 * d) Gdi.Line(hdc, ox + x, oy + 1, ox + x - d, oy + 1);
                for (int y = p; H > y; y += 2 * d) Gdi.Line(hdc, ox + 1, oy + y, ox + 1, oy + y + d);
                for (int x = p; W > x; x += 2 * d) Gdi.Line(hdc, ox + x, oy + H - 1, ox + x + d, oy + H - 1);
                for (int y = H - p; y > 0; y -= 2 * d) Gdi.Line(hdc, ox + W - 1, oy + y, ox + W - 1, oy + y - d);
            });
            Gdi.RestoreDC(hdc, save);
        }

        // ------------------------------------------------------------------------------------------
        // Keyboard

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // While typing in a text box, copy/redo/arrows belong to the box.
            bool typing = ActiveTextBox != null && ActiveTextBox.ContainsFocus;
            Keys key = keyData & Keys.KeyCode;
            bool shift = (keyData & Keys.Shift) != 0;
            switch (keyData)
            {
                case Keys.Escape:
                    Close();   // any mode, even while typing text
                    return true;
                case Keys.Control | Keys.Z:
                    if (mode == Mode.Selected) Undo();
                    return true;
                case Keys.Control | Keys.Y:
                    if (typing) break;
                    Redo();
                    return true;
                case Keys.Control | Keys.C:
                    if (typing) break;
                    if (mode == Mode.Selected) Copy();
                    return true;
                case Keys.Control | Keys.S:
                    if (toolbar.Visible) QuickSave();
                    return true;
                case Keys.Control | Keys.Shift | Keys.S:
                    if (toolbar.Visible) SaveAs();
                    return true;
                case Keys.Enter:
                    if (mode == Mode.Hover && !selecting && !moving)
                    {
                        // Enter in hover mode = click on the hovered window.
                        BeginSelection(PointToClient(Cursor.Position));
                        EndSelection();
                        return true;
                    }
                    if (toolbar.Visible && !typing && (ActiveTextBox == null || !ActiveTextBox.Visible))
                    {
                        QuickSave();   // Enter = Save while the toolbar is shown (and no text is being typed)
                        return true;
                    }
                    break;
            }
            if (!typing && mode == Mode.Selected && (keyData & (Keys.Control | Keys.Alt)) == 0 &&
                (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down))
            {
                int step = shift ? 10 : 1;
                Nudge(key == Keys.Left ? -step : key == Keys.Right ? step : 0, key == Keys.Up ? -step : key == Keys.Down ? step : 0);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ------------------------------------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

        [DllImport("msimg32.dll")]
        static extern bool AlphaBlend(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, BLENDFUNCTION bf);
    }
}

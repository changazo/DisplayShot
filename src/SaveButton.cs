using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DisplayShot
{
    /// <summary>The Save button: a primary square with the floppy glyph, plus a small round badge on its
    /// bottom-right corner that changes the save folder. Clicking the square saves; clicking the badge
    /// (or right-clicking anywhere) raises <see cref="FolderClick"/>.</summary>
    internal sealed class SaveButton : Control
    {
        static readonly ToolTip tips = new ToolTip();

        enum Part { None, Main, Badge }
        float scale = 1f;
        Image glyph;
        Part hover = Part.None, pressed = Part.None;
        string mainHint = "", badgeHint = "";

        public event EventHandler SaveClick, FolderClick;

        public SaveButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, false);
            BackColor = Color.Transparent;
            Scale = 1f;
        }

        /// <summary>Monitor scale (1 = 96 DPI): the square is 32 px at 1, the badge sticks out 7 px.</summary>
        public new float Scale
        {
            get { return scale; }
            set
            {
                scale = value;
                glyph = Assets.Glyph("action-save", SquareSize);
                Size = new Size(SquareSize + Px(7), SquareSize + Px(7));
                Invalidate();
            }
        }

        int Px(float v) { return (int)Math.Round(v * scale); }
        int SquareSize { get { return Px(32); } }
        float BadgeRadius { get { return 7.5f * scale; } }

        public string MainHint { get { return mainHint; } set { mainHint = value; RefreshTip(); } }
        public string BadgeHint { get { return badgeHint; } set { badgeHint = value; RefreshTip(); } }

        PointF BadgeCenter { get { return new PointF(SquareSize - 1.5f * scale, SquareSize - 1.5f * scale); } }

        Part HitTest(Point p)
        {
            PointF c = BadgeCenter;
            float dx = p.X - c.X, dy = p.Y - c.Y, r = BadgeRadius + scale;
            if (dx * dx + dy * dy <= r * r) return Part.Badge;
            if (p.X >= 0 && p.Y >= 0 && p.X < SquareSize && p.Y < SquareSize) return Part.Main;
            return Part.None;
        }

        void RefreshTip() { tips.SetToolTip(this, hover == Part.Badge ? badgeHint : mainHint); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Part h = HitTest(e.Location);
            if (h != hover)
            {
                hover = h;
                Cursor = h == Part.Badge ? Cursors.Hand : Cursors.Default;
                RefreshTip();
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = pressed = Part.None;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = e.Button == MouseButtons.Left ? HitTest(e.Location) : Part.None;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Part up = HitTest(e.Location);
            Part was = pressed;
            pressed = Part.None;
            Invalidate();
            if (e.Button == MouseButtons.Left && up == was)
            {
                if (up == Part.Main && SaveClick != null) SaveClick(this, EventArgs.Empty);
                else if (up == Part.Badge && FolderClick != null) FolderClick(this, EventArgs.Empty);
            }
            else if (e.Button == MouseButtons.Right && up != Part.None && FolderClick != null) FolderClick(this, EventArgs.Empty);
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int sq = SquareSize;

            Color main = pressed == Part.Main ? Theme.Slate : hover == Part.Main ? Theme.GrapeLight : Theme.Grape;
            Theme.FillRounded(g, new Rectangle(0, 0, sq, sq), Math.Max(3, Px(5)), main);
            if (glyph != null) g.DrawImage(glyph, (sq - glyph.Width) / 2, (sq - glyph.Height) / 2, glyph.Width, glyph.Height);

            // Badge: a ring in the toolbar colour cuts it out of the square, then the badge itself.
            PointF c = BadgeCenter;
            float rb = BadgeRadius, rr = rb + 2 * scale;
            using (SolidBrush ring = new SolidBrush(Theme.Background))
                g.FillEllipse(ring, c.X - rr, c.Y - rr, 2 * rr, 2 * rr);
            Color badge = pressed == Part.Badge ? Theme.Grape : hover == Part.Badge ? Theme.Slate : Theme.Charcoal;
            using (SolidBrush b = new SolidBrush(badge))
                g.FillEllipse(b, c.X - rb, c.Y - rb, 2 * rb, 2 * rb);

            // Tiny folder: tab + body, with a fold line.
            float u = scale, x = c.X - 4f * u, y = c.Y - 3.2f * u;
            using (SolidBrush f = new SolidBrush(Theme.Lavender))
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddPolygon(new[]
                {
                    new PointF(x, y), new PointF(x + 3.2f * u, y), new PointF(x + 4.2f * u, y + 1.2f * u),
                    new PointF(x + 8f * u, y + 1.2f * u), new PointF(x + 8f * u, y + 6.4f * u), new PointF(x, y + 6.4f * u)
                });
                g.FillPath(f, p);
            }
            using (Pen line = new Pen(badge, 0.9f * u))
                g.DrawLine(line, c.X - 4f * u, c.Y - 0.6f * u, c.X + 4f * u, c.Y - 0.6f * u);
        }
    }
}

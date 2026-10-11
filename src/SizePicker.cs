using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DisplayShot
{
    /// <summary>Font-size picker for the text tool: a small dark button ("8 pt ▾") that opens a themed menu.</summary>
    internal sealed class SizePicker : Control
    {
        readonly int[] values;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        int value;
        bool hovering;

        public event EventHandler ValueChanged;

        public SizePicker(int[] values)
        {
            this.values = values;
            value = values[0];
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Theme.Background;
            Font = Theme.Ui;

            menu.Renderer = Theme.MenuRenderer();
            menu.ShowImageMargin = false;
            menu.BackColor = Theme.Background;
            foreach (int v in values)
            {
                int size = v;
                ToolStripMenuItem item = new ToolStripMenuItem(v + " pt");
                item.ForeColor = Theme.Lavender;
                item.Font = Theme.Ui;
                item.Click += delegate { Value = size; if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); };
                menu.Items.Add(item);
            }
        }

        public int Value { get { return value; } set { this.value = value; Invalidate(); } }

        protected override void OnFontChanged(EventArgs e)
        {
            foreach (ToolStripItem item in menu.Items) item.Font = Font;
            Invalidate();
            base.OnFontChanged(e);
        }

        protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            foreach (ToolStripMenuItem item in menu.Items) item.Checked = item.Text == value + " pt";
            menu.Show(this, new Point(0, Height));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = ClientRectangle;
            Theme.FillRounded(g, r, 4, hovering ? Theme.Charcoal : Theme.Surface);
            Theme.DrawRounded(g, r, 4, Theme.Slate);
            int pad = Height / 3;
            TextRenderer.DrawText(g, value + " pt", Font, new Rectangle(pad, 0, Width - 3 * pad, Height), Theme.Lavender,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            // chevron
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float u = Height / 23f, cx = Width - 12 * u, cy = Height / 2f;
            using (Pen p = new Pen(Theme.Muted, 1.5f * u))
                g.DrawLines(p, new[] { new PointF(cx - 4 * u, cy - 2 * u), new PointF(cx, cy + 2 * u), new PointF(cx + 4 * u, cy - 2 * u) });
        }
    }
}

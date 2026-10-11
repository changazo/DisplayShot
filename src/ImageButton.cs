using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DisplayShot
{
    /// <summary>Flat toolbar button: a glyph and/or a caption over a rounded background whose colour shows
    /// the state (hover, pressed, selected). A "primary" button is filled with the accent colour.</summary>
    internal class ImageButton : Control
    {
        static readonly ToolTip tips = new ToolTip();

        Image glyph;
        bool hovering, pressing, isSelected, primary;

        public ImageButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
            ForeColor = Theme.Lavender;
            Font = Theme.UiSemibold;
            Size = new Size(32, 32);
        }

        public Image Glyph { get { return glyph; } set { glyph = value; Invalidate(); } }

        /// <summary>Filled with the accent colour (Save, OK).</summary>
        public bool Primary { get { return primary; } set { primary = value; Invalidate(); } }

        /// <summary>Latched "active" state (current tool, current pen).</summary>
        public bool Selected { get { return isSelected; } set { if (isSelected != value) { isSelected = value; Invalidate(); } } }

        public string Hint { get { return tips.GetToolTip(this); } set { tips.SetToolTip(this, value); } }

        protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovering = false; pressing = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { pressing = true; Invalidate(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { pressing = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        Color Background()
        {
            if (!Enabled) return primary ? Theme.Charcoal : Color.Empty;
            if (pressing) return Theme.Slate;
            if (primary) return hovering ? Theme.GrapeLight : Theme.Grape;
            if (isSelected) return Theme.Grape;
            if (hovering) return Theme.Charcoal;
            return Color.Empty;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color bg = Background();
            if (!bg.IsEmpty) Theme.FillRounded(g, ClientRectangle, Math.Max(3, (int)Math.Round(Height * 5 / 32.0)), bg);

            int x = 0;
            if (glyph != null)
            {
                int gx = string.IsNullOrEmpty(Text) ? (Width - glyph.Width) / 2 : 4;
                int gy = (Height - glyph.Height) / 2;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                if (Enabled) g.DrawImage(glyph, gx, gy, glyph.Width, glyph.Height);
                else ControlPaint.DrawImageDisabled(g, glyph, gx, gy, Color.Transparent);
                x = gx + glyph.Width;
            }
            if (!string.IsNullOrEmpty(Text))
            {
                Rectangle r = glyph == null ? ClientRectangle : new Rectangle(x, 0, Width - x - 6, Height);
                TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                                    TextFormatFlags.HorizontalCenter;
                TextRenderer.DrawText(g, Text, Font, r, Enabled ? ForeColor : Theme.Muted, f);
            }
        }
    }

    /// <summary>A small colour square for the annotation palette.</summary>
    internal sealed class SwatchButton : Control
    {
        bool hovering;

        public SwatchButton(Color color)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = color;
            Size = new Size(12, 12);
        }

        protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, ClientRectangle);
            using (Pen p = new Pen(hovering ? Theme.Blush : Theme.Slate))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }
}

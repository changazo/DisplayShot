using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DisplayShot
{
    /// <summary>Colours and fonts. Palette from https://github.com/changazo/page (dark theme first);
    /// UI text uses the page's sans (Segoe UI on Windows), the logo its serif (Palatino Linotype).</summary>
    internal static class Theme
    {
        public static readonly Color Lavender = Rgb(0xEDE5FF);   // text and glyphs
        public static readonly Color Muted = Rgb(0xCDC5DD);      // secondary text
        public static readonly Color Grape = Rgb(0x6B4E71);      // accent: active tool, primary buttons, selection
        public static readonly Color GrapeLight = Rgb(0x7D5F84); // primary button hover
        public static readonly Color Blush = Rgb(0xF5DDDD);      // highlight over images
        public static readonly Color Slate = Rgb(0x53687E);      // pressed buttons, control borders
        public static readonly Color Charcoal = Rgb(0x3A4454);   // hover background, dividers, panel borders
        public static readonly Color Surface = Rgb(0x353D4D);    // inputs
        public static readonly Color Background = Rgb(0x2A313F); // toolbars and windows

        public static readonly Font Ui = new Font("Segoe UI", 12f, GraphicsUnit.Pixel);
        public static readonly Font UiLarge = new Font("Segoe UI", 14f, GraphicsUnit.Pixel);
        public static readonly Font UiSemibold = new Font("Segoe UI Semibold", 13f, GraphicsUnit.Pixel);

        static readonly System.Collections.Generic.Dictionary<string, Font> fonts = new System.Collections.Generic.Dictionary<string, Font>();

        /// <summary>UI font at a monitor scale (1 = 96 DPI), cached.</summary>
        public static Font UiAt(float scale) { return FontAt("Segoe UI", 12, scale); }
        public static Font UiSemiboldAt(float scale) { return FontAt("Segoe UI Semibold", 13, scale); }

        static Font FontAt(string family, float px, float scale)
        {
            string key = family + "@" + px * scale;
            Font f;
            if (!fonts.TryGetValue(key, out f)) fonts[key] = f = new Font(family, px * scale, GraphicsUnit.Pixel);
            return f;
        }

        static Color Rgb(int rgb) { return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        /// <summary>Anti-aliased filled rounded rectangle (button backgrounds).</summary>
        public static void FillRounded(Graphics g, Rectangle r, int radius, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = RoundedPath(r, radius))
            using (SolidBrush b = new SolidBrush(c))
            {
                SmoothingMode old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(b, p);
                g.SmoothingMode = old;
            }
        }

        public static void DrawRounded(Graphics g, Rectangle r, int radius, Color c)
        {
            using (GraphicsPath p = RoundedPath(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), radius))
            using (Pen pen = new Pen(c))
            {
                SmoothingMode old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawPath(pen, p);
                g.SmoothingMode = old;
            }
        }

        /// <summary>Renderer for context menus in the theme colours.</summary>
        public static ToolStripRenderer MenuRenderer() { return new MenuRendererImpl(); }

        sealed class MenuRendererImpl : ToolStripProfessionalRenderer
        {
            public MenuRendererImpl() : base(new MenuColors()) { }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? Lavender : Muted;
                base.OnRenderItemText(e);
            }
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                Rectangle r = new Rectangle(e.ImageRectangle.X + 2, e.Item.ContentRectangle.Y + (e.Item.ContentRectangle.Height - 14) / 2, 14, 14);
                FillRounded(e.Graphics, r, 3, Grape);
                using (Pen p = new Pen(Blush, 1.8f))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.DrawLines(p, new[] { new PointF(r.X + 3.4f, r.Y + 7.3f), new PointF(r.X + 6f, r.Y + 9.8f), new PointF(r.X + 10.8f, r.Y + 4.2f) });
                }
            }
            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using (Pen p = new Pen(Charcoal)) e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
            }
        }

        sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Background; } }
            public override Color MenuBorder { get { return Charcoal; } }
            public override Color MenuItemBorder { get { return Grape; } }
            public override Color MenuItemSelected { get { return Grape; } }
            public override Color MenuItemSelectedGradientBegin { get { return Grape; } }
            public override Color MenuItemSelectedGradientEnd { get { return Grape; } }
            public override Color CheckBackground { get { return Background; } }
            public override Color CheckSelectedBackground { get { return Grape; } }
            public override Color CheckPressedBackground { get { return Grape; } }
            public override Color ImageMarginGradientBegin { get { return Background; } }
            public override Color ImageMarginGradientMiddle { get { return Background; } }
            public override Color ImageMarginGradientEnd { get { return Background; } }
            public override Color SeparatorDark { get { return Charcoal; } }
            public override Color SeparatorLight { get { return Background; } }
        }

        static GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}

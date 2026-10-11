using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DisplayShot
{
    /// <summary>"Copied to the clipboard" message shown after Copy. Modal; closes on OK or after 10 s, and
    /// then remembers "Don't show this again" if it is still ticked (it starts ticked).</summary>
    internal sealed class CopiedForm : Form
    {
        readonly float scale;
        readonly Timer closeTimer = new Timer();
        readonly CheckGlyph check;
        bool dontShowAgain = true;

        public CopiedForm()
        {
            scale = Native.MonitorScale(Cursor.Position);
            Text = "DisplayShot";
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = Settings.CopyMessageOnTop;
            BackColor = Theme.Charcoal;   // 1-px frame around the panel
            ClientSize = new Size(S(380), S(124));

            Panel panel = new BufferedPanel();
            panel.BackColor = Theme.Background;
            panel.SetBounds(1, 1, ClientSize.Width - 2, ClientSize.Height - 2);
            Controls.Add(panel);

            PictureBox logo = new PictureBox();
            logo.Image = Assets.Get("logo.png");
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.SetBounds(S(20), S(20), S(30), S(30));

            Label title = MakeLabel("Copied to the clipboard.", MakeFont(14, true), Theme.Lavender);
            title.Location = new Point(S(64), S(18));
            Label detail = MakeLabel("Paste it anywhere with Ctrl+V.", MakeFont(13, false), Theme.Muted);
            detail.Location = new Point(S(64), S(40));

            check = new CheckGlyph(S(14));
            check.Location = new Point(S(20), S(88));
            check.Checked = dontShowAgain;
            Label info = MakeLabel("Don't show this again", MakeFont(13, false), Theme.Muted);
            info.Location = new Point(S(40), S(86));

            ImageButton ok = new ImageButton();
            ok.Text = "OK";
            ok.Primary = true;
            ok.Font = MakeFont(13, true);
            ok.SetBounds(ClientSize.Width - S(20) - S(75), ClientSize.Height - S(14) - S(28), S(75), S(28));
            ok.Click += delegate { CloseMessage(); };

            panel.Controls.AddRange(new Control[] { logo, title, detail, check, info, ok });

            EventHandler toggle = delegate { dontShowAgain = !dontShowAgain; check.Checked = dontShowAgain; };
            check.Click += toggle;
            info.Click += toggle;
            foreach (Control c in new Control[] { panel, logo, title, detail }) c.MouseDown += DragForm;

            closeTimer.Interval = 10000;
            closeTimer.Tick += delegate { CloseMessage(); };
        }

        int S(int v) { return (int)Math.Round(v * scale); }

        Font MakeFont(float px, bool semibold)
        {
            return new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", px * scale, GraphicsUnit.Pixel);
        }

        static Label MakeLabel(string text, Font font, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.BackColor = Theme.Background;
            l.AutoSize = true;
            l.UseMnemonic = false;
            return l;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;   // centred on the cursor's monitor
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
            closeTimer.Start();
            Activate();
        }

        void CloseMessage()
        {
            closeTimer.Stop();
            if (dontShowAgain) Settings.CopyMessageHidden = true;
            Close();
        }

        /// <summary>Drags the borderless window.</summary>
        void DragForm(object sender, MouseEventArgs e)
        {
            ReleaseCapture();
            SendMessage(Handle, 0x0112 /*WM_SYSCOMMAND*/, (IntPtr)0xF012 /*SC_MOVE|HTCAPTION*/, IntPtr.Zero);
        }

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
    }

    /// <summary>A small check box drawn in the theme colours.</summary>
    internal sealed class CheckGlyph : Control
    {
        bool isChecked;

        public CheckGlyph(int size)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Theme.Background;
            Size = new Size(size, size);
            Cursor = Cursors.Hand;
        }

        public bool Checked { get { return isChecked; } set { isChecked = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = ClientRectangle;
            if (isChecked)
            {
                Theme.FillRounded(g, r, 3, Theme.Grape);
                using (Pen p = new Pen(Theme.Blush, Math.Max(1.5f, Width / 8f)))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    float w = Width, h = Height;
                    g.DrawLines(p, new[] { new PointF(w * 0.24f, h * 0.52f), new PointF(w * 0.43f, h * 0.70f), new PointF(w * 0.77f, h * 0.30f) });
                }
            }
            else Theme.DrawRounded(g, r, 3, Theme.Slate);
        }
    }
}

using System;
using System.Drawing;
using System.Windows.Forms;

namespace DisplayShot
{
    /// <summary>Small notice above the tray in the app's own style, used instead of a Windows balloon (a portable
    /// app has no registered name, so Windows would title the balloon with a generated ID). Closes on a click
    /// or after a few seconds and never takes the focus.</summary>
    internal sealed class TrayToast : Form
    {
        readonly Timer closeTimer = new Timer();

        public TrayToast(string title, string detail, int milliseconds)
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            float scale = Native.MonitorScale(new Point(wa.Right - 1, wa.Bottom - 1));
            Func<int, int> S = v => (int)Math.Round(v * scale);
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Theme.Charcoal;   // 1-px frame

            Panel panel = new BufferedPanel();
            panel.BackColor = Theme.Background;
            Controls.Add(panel);

            PictureBox logo = new PictureBox();
            logo.Image = Assets.Get("logo.png");
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.SetBounds(S(18), S(16), S(28), S(28));

            Label line1 = new Label(), line2 = new Label();
            line1.Text = title;
            line1.Font = new Font("Segoe UI Semibold", 14 * scale, GraphicsUnit.Pixel);
            line1.ForeColor = Theme.Lavender;
            line2.Text = detail;
            line2.Font = new Font("Segoe UI", 13 * scale, GraphicsUnit.Pixel);
            line2.ForeColor = Theme.Muted;
            foreach (Label l in new[] { line1, line2 })
            {
                l.BackColor = Theme.Background;
                l.AutoSize = true;
                l.UseMnemonic = false;
            }
            panel.Controls.AddRange(new Control[] { logo, line1, line2 });
            line1.Location = new Point(S(58), S(14));
            line2.Location = new Point(S(58), line1.Bottom + S(4));

            int w = Math.Max(line1.Right, line2.Right) + S(20), h = line2.Bottom + S(14);
            ClientSize = new Size(w + 2, h + 2);
            panel.SetBounds(1, 1, w, h);
            Location = new Point(wa.Right - Width - S(12), wa.Bottom - Height - S(12));

            foreach (Control c in new Control[] { panel, logo, line1, line2 }) c.Click += delegate { Close(); };
            closeTimer.Interval = milliseconds;
            closeTimer.Tick += delegate { Close(); };
            closeTimer.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | 0x08000000 /*WS_EX_NOACTIVATE*/ | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closeTimer.Dispose();
            base.OnFormClosed(e);
        }
    }
}

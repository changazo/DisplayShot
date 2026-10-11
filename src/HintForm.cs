using System;
using System.Drawing;
using System.Windows.Forms;

namespace DisplayShot
{
    /// <summary>One-time "how to select" hint that follows the cursor during the first seconds of the
    /// very first capture. It never takes the keyboard focus from the overlay.</summary>
    internal sealed class HintForm : Form
    {
        public HintForm()
        {
            float scale = Native.MonitorScale(Cursor.Position);
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
            line1.Text = "Click a window to capture it, or drag to select an area.";
            line1.Font = new Font("Segoe UI Semibold", 14 * scale, GraphicsUnit.Pixel);
            line1.ForeColor = Theme.Lavender;
            line2.Text = "Right-click or press Esc to cancel.";
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

        /// <summary>20 px right of the cursor and below the magnifier, flipped near the right/bottom edge
        /// of the cursor's monitor.</summary>
        public void FollowCursor(Point screenCursor)
        {
            int x = screenCursor.X, y = screenCursor.Y + 140 + 20;
            Rectangle mon = Native.MonitorBounds(screenCursor);
            int left = x > mon.Right - Width ? x - Width : x + 20;
            int top = y > mon.Bottom - Height ? y - 250 : y + 20;
            Location = new Point(left, top);
            if (!Visible) Show();
            KeepOnTop();
        }

        /// <summary>Raises the hint above the (also topmost) overlay without activating it.</summary>
        public void KeepOnTop()
        {
            SetWindowPos(Handle, (IntPtr)(-1) /*HWND_TOPMOST*/, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /*NOSIZE|NOMOVE|NOACTIVATE*/);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }

    internal sealed partial class OverlayForm
    {
        HintForm hint;
        DateTime hintShownAt;

        void ShowHintIfFirstRun()
        {
            if (Settings.SelectionHintDone) return;
            hint = new HintForm();
            hintShownAt = DateTime.Now;
            hint.FollowCursor(Cursor.Position);
        }

        /// <summary>Called by the hover loop when the cursor moves; the hint closes after 6 s.</summary>
        void MoveHint()
        {
            if (hint == null) return;
            if ((DateTime.Now - hintShownAt).TotalSeconds > 6) { CloseHint(); return; }
            hint.FollowCursor(Cursor.Position);
        }

        void CloseHint()
        {
            if (hint == null) return;
            hint.Close();
            hint.Dispose();
            hint = null;
        }
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DisplayShot
{
    /// <summary>Background mode: a resident DisplayShot with a tray icon and the Ctrl+Alt+X hotkey, so a capture
    /// starts instantly. Launching DisplayShot.exe while it runs hands the capture to it.</summary>
    internal static class Background
    {
        public const string WindowTitle = "DisplayShot.Background";
        public const int WM_CAPTURE = 0x8000 + 1, WM_QUIT_BACKGROUND = 0x8000 + 2;   // WM_APP + n
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunValue = "DisplayShot";

        /// <summary>True in the resident process itself.</summary>
        public static bool IsResidentProcess;

        static IntPtr ResidentWindow() { return FindWindow(null, WindowTitle); }

        public static bool IsEnabled
        {
            get { return IsResidentProcess ? !ResidentContext.Current.ExitAfterCapture : ResidentWindow() != IntPtr.Zero; }
        }

        /// <summary>Starts or stops the resident process (from the gear menu of a capture).</summary>
        public static void SetEnabled(bool on)
        {
            if (IsResidentProcess)
            {
                ResidentContext.Current.ExitAfterCapture = !on;   // takes effect when this capture closes
                return;
            }
            IntPtr w = ResidentWindow();
            if (on && w == IntPtr.Zero)
            {
                using (Process p = Process.Start(Application.ExecutablePath, "--background --announce"))
                {
                    try { p.WaitForInputIdle(5000); }   // its window exists once it idles, so IsEnabled is accurate
                    catch (InvalidOperationException) { }
                }
            }
            else if (!on && w != IntPtr.Zero) PostMessage(w, WM_QUIT_BACKGROUND, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Start the background mode when signing in to Windows (per-user Run key).</summary>
        public static bool StartWithWindows
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunValue) != null;
            }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(RunValue, "\"" + Application.ExecutablePath + "\" --background");
                    else k.DeleteValue(RunValue, false);
                }
                if (value) SetEnabled(true);
            }
        }

        /// <summary>Asks a running background instance to capture. False when none is running.</summary>
        public static bool RequestCapture()
        {
            IntPtr w = ResidentWindow();
            if (w == IntPtr.Zero) return false;
            uint pid;
            Native.GetWindowThreadProcessId(w, out pid);
            AllowSetForegroundWindow(pid);   // we were launched by the user, so we may pass the foreground on
            return PostMessage(w, WM_CAPTURE, IntPtr.Zero, IntPtr.Zero);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
    }

    /// <summary>The resident process: tray icon, Ctrl+Alt+X, and one capture overlay at a time.</summary>
    internal sealed class ResidentContext : ApplicationContext
    {
        public static ResidentContext Current;
        public bool ExitAfterCapture;

        const int HotkeyId = 1, MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000, VK_X = 0x58;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly MessageWindow window;
        ToolStripMenuItem miStartup;
        OverlayForm overlay;
        TrayToast toast;

        public ResidentContext(bool announce)
        {
            Current = this;
            window = new MessageWindow(this);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = Theme.MenuRenderer();
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            menu.Font = Theme.UiAt(Native.MonitorScale(Cursor.Position));
            ToolStripMenuItem miCapture = new ToolStripMenuItem("Capture\tCtrl+Alt+X");
            miCapture.Font = new Font(menu.Font, FontStyle.Bold);
            miCapture.Click += delegate { StartCapture(); };
            miStartup = new ToolStripMenuItem("Start with Windows");
            miStartup.Click += delegate { Background.StartWithWindows = !Background.StartWithWindows; };
            ToolStripMenuItem miExit = new ToolStripMenuItem("Exit");
            miExit.Click += delegate { Quit(); };
            menu.Items.AddRange(new ToolStripItem[] { miCapture, new ToolStripSeparator(), miStartup, miExit });
            menu.Opening += delegate { miStartup.Checked = Background.StartWithWindows; };

            tray.Icon = SmallAppIcon();
            tray.Text = "DisplayShot (Ctrl+Alt+X)";
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object o, MouseEventArgs e) { if (e.Button == MouseButtons.Left) StartCapture(); };
            tray.Visible = true;

            bool ok = RegisterHotKey(window.Handle, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_X);
            if (!ok)
                ShowToast("Ctrl+Alt+X is used by another program", "Click the DisplayShot icon in the tray to capture.", 9000);
            else if (announce)
                ShowToast("DisplayShot is running in the background", "Press Ctrl+Alt+X to capture.", 5000);
        }

        void ShowToast(string title, string detail, int milliseconds)
        {
            toast = new TrayToast(title, detail, milliseconds);
            toast.FormClosed += delegate { toast = null; };
            toast.Show();
        }

        /// <summary>Hotkey, tray click or a request from a new DisplayShot.exe: open a capture overlay.</summary>
        void StartCapture()
        {
            if (overlay != null) { Program.ForceForeground(overlay.Handle); return; }
            IntPtr other = FindWindow(null, Program.OverlayTitle);   // a capture from another process is open
            if (other != IntPtr.Zero) { Program.ForceForeground(other); return; }
            if (toast != null)
            {
                toast.Close();   // keep it out of the screenshot
                DwmFlush();      // wait until the desktop is composed without it
            }
            overlay = Program.CreateOverlay();
            overlay.FormClosed += delegate
            {
                overlay.Dispose();
                overlay = null;
                GC.Collect();   // release the desktop-sized bitmaps right away
                if (ExitAfterCapture) Quit();
            };
            overlay.Show();
        }

        void Quit()
        {
            UnregisterHotKey(window.Handle, HotkeyId);
            if (toast != null) toast.Close();
            tray.Visible = false;
            tray.Dispose();
            window.DestroyHandle();
            ExitThread();
        }

        static Icon SmallAppIcon()
        {
            IntPtr[] large = new IntPtr[1], small = new IntPtr[1];
            if (ExtractIconEx(Application.ExecutablePath, 0, large, small, 1) > 0 && small[0] != IntPtr.Zero)
                return Icon.FromHandle(small[0]);
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }

        /// <summary>Hidden window that receives the hotkey and requests from other DisplayShot processes.</summary>
        sealed class MessageWindow : NativeWindow
        {
            readonly ResidentContext owner;

            public MessageWindow(ResidentContext owner)
            {
                this.owner = owner;
                CreateParams cp = new CreateParams();
                cp.Caption = Background.WindowTitle;   // found by FindWindow (hidden, not message-only)
                CreateHandle(cp);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0312 /*WM_HOTKEY*/ || m.Msg == Background.WM_CAPTURE) { owner.StartCapture(); return; }
                if (m.Msg == Background.WM_QUIT_BACKGROUND)
                {
                    if (owner.overlay != null) owner.ExitAfterCapture = true; else owner.Quit();
                    return;
                }
                base.WndProc(ref m);
            }
        }

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, int modifiers, int vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("dwmapi.dll")] static extern int DwmFlush();
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int count);
    }
}

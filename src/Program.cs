using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DisplayShot
{
    internal static class Program
    {
        /// <summary>Overlay window title; a second launch uses it to find and re-activate the running overlay.</summary>
        public const string OverlayTitle = "DisplayShot.Overlay";
        const string MutexName = "DisplayShot.SingleInstance";

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (Array.IndexOf(args, "--background") >= 0)
            {
                // Background mode: tray icon + Ctrl+Alt+X (one resident process at most).
                bool first;
                using (Mutex resident = new Mutex(true, "DisplayShot.Background", out first))
                {
                    if (!first) return;
                    Background.IsResidentProcess = true;
                    Application.Run(new ResidentContext(Array.IndexOf(args, "--announce") >= 0));
                    GC.KeepAlive(resident);
                }
                return;
            }

            // A background instance is running: it captures instantly, so hand the request over.
            if (Background.RequestCapture()) return;

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Already capturing: bring the running overlay forward and quit.
                    IntPtr existing = FindWindow(null, OverlayTitle);
                    if (existing != IntPtr.Zero) ForceForeground(existing);
                    return;
                }
                OverlayForm overlay = CreateOverlay();
                Application.Run(overlay);   // closing the overlay ends the process
                overlay.Dispose();
                GC.KeepAlive(mutex);
            }
        }

        /// <summary>Enumerates the windows, captures the screen and builds the overlay. Both happen before
        /// the overlay exists, so neither contains it.</summary>
        public static OverlayForm CreateOverlay()
        {
            Rectangle desktop = Native.VirtualScreen;
            WindowList windows = new WindowList(desktop);
            Rectangle bounds;
            using (Bitmap capture = Capture.VirtualDesktop(out bounds))
                return new OverlayForm(capture, bounds, windows);
        }

        /// <summary>Activates a window even when another process owns the foreground (briefly attaches
        /// to the foreground thread's input queue, which is what SetForegroundWindow requires).</summary>
        public static void ForceForeground(IntPtr hWnd)
        {
            if (Native.IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
            IntPtr fg = GetForegroundWindow();
            if (fg == hWnd) return;
            uint pid;
            uint fgThread = Native.GetWindowThreadProcessId(fg, out pid);
            uint ourThread = GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != ourThread && AttachThreadInput(ourThread, fgThread, true);
            try
            {
                BringWindowToTop(hWnd);
                Native.SetForegroundWindow(hWnd);
            }
            finally
            {
                if (attached) AttachThreadInput(ourThread, fgThread, false);
            }
        }

        const int SW_RESTORE = 9;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    }
}

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DisplayShot
{
    /// <summary>DisplayShot.ini next to the exe (portable): one-time hints, the quick-save folder and options, and
    /// hidden toolbar items.</summary>
    internal static class Settings
    {
        static readonly string path = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "DisplayShot.ini");

        /// <summary>The "how to select" hint has been seen: the user has started a selection once.</summary>
        public static bool SelectionHintDone
        {
            get { return ReadInt("Hints", "SelectionHintDone", 0) == 1; }
            set { WriteInt("Hints", "SelectionHintDone", value ? 1 : 0); }
        }

        /// <summary>"Don't show this again" was left ticked in the copied-to-clipboard message.</summary>
        public static bool CopyMessageHidden
        {
            get { return ReadInt("Hints", "CopyMessageHidden", 0) == 1; }
            set { WriteInt("Hints", "CopyMessageHidden", value ? 1 : 0); }
        }

        /// <summary>Keep the copied-to-clipboard message above other windows.</summary>
        public static bool CopyMessageOnTop { get { return ReadInt("Hints", "CopyMessageOnTop", 0) == 1; } }

        /// <summary>Folder Quick save writes to ("" = Pictures\Screenshots).</summary>
        public static string SaveFolder
        {
            get { return ReadString("Save", "Folder"); }
            set
            {
                try { WritePrivateProfileString("Save", "Folder", value ?? "", path); }
                catch (Exception) { }
            }
        }

        /// <summary>Save and Save as also put the image on the clipboard (gear menu).</summary>
        public static bool CopyOnSave
        {
            get { return ReadInt("Save", "AlsoCopy", 0) == 1; }
            set { WriteInt("Save", "AlsoCopy", value ? 1 : 0); }
        }

        /// <summary>Toolbar items the user hid with the gear button (comma-separated ids).</summary>
        public static string HiddenButtons
        {
            get { return ReadString("Toolbar", "Hidden"); }
            set
            {
                try { WritePrivateProfileString("Toolbar", "Hidden", value ?? "", path); }
                catch (Exception) { }
            }
        }

        static string ReadString(string section, string key)
        {
            StringBuilder sb = new StringBuilder(1024);
            GetPrivateProfileString(section, key, "", sb, sb.Capacity, path);
            return sb.ToString().Trim();
        }

        static int ReadInt(string section, string key, int def)
        {
            StringBuilder sb = new StringBuilder(64);
            GetPrivateProfileString(section, key, "", sb, sb.Capacity, path);
            int v;
            return int.TryParse(sb.ToString().Trim(), out v) ? v : def;
        }

        static void WriteInt(string section, string key, int value)
        {
            try { WritePrivateProfileString(section, key, value.ToString(), path); }
            catch (Exception) { }   // read-only folder: settings just aren't remembered
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder ret, int size, string file);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool WritePrivateProfileString(string section, string key, string value, string file);
    }
}

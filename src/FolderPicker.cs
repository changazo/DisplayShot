using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DisplayShot
{
    /// <summary>Folder picker using the modern Explorer-style dialog (IFileOpenDialog with FOS_PICKFOLDERS),
    /// falling back to the classic FolderBrowserDialog if COM is unavailable.</summary>
    internal static class FolderPicker
    {
        /// <summary>Returns the chosen folder, or null when cancelled.</summary>
        public static string Pick(IWin32Window owner, string title, string initialFolder)
        {
            try
            {
                IFileOpenDialog dlg = (IFileOpenDialog)new FileOpenDialogRCW();
                try
                {
                    uint opts;
                    dlg.GetOptions(out opts);
                    dlg.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                    dlg.SetTitle(title);
                    if (!string.IsNullOrEmpty(initialFolder) && Directory.Exists(initialFolder))
                    {
                        IShellItem folder;
                        Guid iid = typeof(IShellItem).GUID;
                        if (SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, ref iid, out folder) == 0) dlg.SetFolder(folder);
                    }
                    if (dlg.Show(owner == null ? IntPtr.Zero : owner.Handle) != 0) return null;   // cancelled
                    IShellItem result;
                    dlg.GetResult(out result);
                    string path;
                    result.GetDisplayName(SIGDN_FILESYSPATH, out path);
                    return path;
                }
                finally { Marshal.ReleaseComObject(dlg); }
            }
            catch (COMException) { }
            catch (InvalidCastException) { }

            using (FolderBrowserDialog fb = new FolderBrowserDialog())
            {
                fb.Description = title;
                if (!string.IsNullOrEmpty(initialFolder)) fb.SelectedPath = initialFolder;
                return fb.ShowDialog(owner) == DialogResult.OK ? fb.SelectedPath : null;
            }
        }

        const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800;
        const uint SIGDN_FILESYSPATH = 0x80058000;

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogRCW { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr specs);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint fos);
            void GetOptions(out uint fos);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int place);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string ext);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IntPtr items);
            void GetSelectedItems(out IntPtr items);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr bc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr bc, ref Guid riid, out IShellItem item);
    }
}

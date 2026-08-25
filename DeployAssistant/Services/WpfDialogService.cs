using System.Diagnostics;
using System.Windows;
using DeployAssistant.Services;
#if NETFRAMEWORK
using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
#else
using Microsoft.Win32;
#endif

namespace DeployAssistant.Services.Wpf
{
    public sealed class WpfDialogService : IDialogService
    {
        public DialogChoice Confirm(string title, string message, DialogChoice defaultChoice = DialogChoice.No)
        {
            var defaultBtn = defaultChoice switch
            {
                DialogChoice.Yes => MessageBoxResult.Yes,
                DialogChoice.Cancel => MessageBoxResult.Cancel,
                _ => MessageBoxResult.No
            };
            var result = MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question, defaultBtn);
            return result switch
            {
                MessageBoxResult.Yes => DialogChoice.Yes,
                MessageBoxResult.No => DialogChoice.No,
                _ => DialogChoice.Cancel
            };
        }

        public void Inform(string title, string message)
            => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public string? PickFolder(string title, string? initialPath = null)
        {
#if NETFRAMEWORK
            return PickFolderViaNetFrameworkComDialog(title, initialPath);
#else
            var dlg = new OpenFolderDialog { Title = title };
            if (initialPath != null && initialPath.Length > 0) dlg.InitialDirectory = initialPath;
            return dlg.ShowDialog() == true ? dlg.FolderName : null;
#endif
        }

        public void OpenInShell(string path)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }); }
            catch { /* shell invocation must not crash the app */ }
        }

#if NETFRAMEWORK
        // .NET Framework has no Microsoft.Win32.OpenFolderDialog (that type is .NET 8 WPF only),
        // so this drives the same Win32 IFileOpenDialog directly.
        private static string? PickFolderViaNetFrameworkComDialog(string title, string? initialPath)
        {
            IFileOpenDialog? dialog = null;
            try
            {
                dialog = (IFileOpenDialog)new FileOpenDialogRcw();
                dialog.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_NOCHANGEDIR | FOS_PATHMUSTEXIST);
                if (!string.IsNullOrEmpty(title)) dialog.SetTitle(title);
                if (initialPath != null && initialPath.Length > 0)
                {
                    Guid shellItemIid = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(initialPath, IntPtr.Zero, ref shellItemIid, out IShellItem? folder) == 0 && folder != null)
                    {
                        dialog.SetFolder(folder);
                        Marshal.ReleaseComObject(folder);
                    }
                }

                IntPtr owner = Application.Current?.MainWindow is Window w
                    ? new WindowInteropHelper(w).Handle
                    : IntPtr.Zero;
                if (dialog.Show(owner) != 0) return null;   // non-zero HRESULT = cancelled/failed

                dialog.GetResult(out IShellItem item);
                try
                {
                    item.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr pszPath);
                    try { return Marshal.PtrToStringUni(pszPath); }
                    finally { Marshal.FreeCoTaskMem(pszPath); }
                }
                finally { Marshal.ReleaseComObject(item); }
            }
            catch (COMException)
            {
                return null;
            }
            finally
            {
                if (dialog != null) Marshal.ReleaseComObject(dialog);
            }
        }

        private const uint FOS_NOCHANGEDIR = 0x00000008;
        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const uint FOS_PATHMUSTEXIST = 0x00000800;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, out IShellItem? ppv);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRcw { }

        // Vtable order must match IFileDialog exactly; unused slots are declared but never called.
        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr hwndOwner);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, uint fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }
#endif
    }
}

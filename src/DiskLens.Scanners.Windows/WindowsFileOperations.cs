using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DiskLens.Core.Platform;

namespace DiskLens.Scanners.Windows;

/// <summary>Explorer integration and recycle-bin deletion via the shell.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFileOperations : IFileOperations
{
    public bool SupportsRecycleBin => true;

    public void RevealInFileManager(string path, bool isDirectory)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", isDirectory ? $"\"{path}\"" : $"/select,\"{path}\"") { UseShellExecute = true });
    }

    public Task DeleteAsync(string path, bool isDirectory, CancellationToken ct) => Task.Run(() =>
    {
        // SHFileOperation wants a double-null-terminated list.
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
        };
        var result = SHFileOperationW(ref op);
        if (result != 0) throw new Win32Exception(result, $"Could not delete '{path}' (shell error {result}).");
        if (op.fAnyOperationsAborted) throw new OperationCanceledException("Deletion was aborted.");
    }, ct);

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004, FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040, FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT lpFileOp);
}

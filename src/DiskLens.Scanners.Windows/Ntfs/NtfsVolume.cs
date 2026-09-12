using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace DiskLens.Scanners.Windows.Ntfs;

/// <summary>Raw read access to an NTFS volume (requires administrator rights).</summary>
[SupportedOSPlatform("windows")]
internal sealed class NtfsVolume : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 1, FileShareWrite = 2;
    private const uint OpenExisting = 3;
    private const uint FsctlGetNtfsVolumeData = 0x00090064;

    private readonly SafeFileHandle _handle;

    private NtfsVolume(SafeFileHandle handle, NtfsVolumeData data)
    {
        _handle = handle;
        Data = data;
    }

    public NtfsVolumeData Data { get; }

    /// <summary>Opens e.g. "C:" for raw reading. Throws <see cref="UnauthorizedAccessException"/> without elevation.</summary>
    public static NtfsVolume Open(string driveLetter)
    {
        var path = @"\\.\" + driveLetter.TrimEnd('\\').TrimEnd(':') + ":";
        var handle = CreateFileW(path, GenericRead, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (error == 5) throw new UnauthorizedAccessException($"Opening {path} requires administrator rights.");
            throw new Win32Exception(error, $"Could not open {path}.");
        }

        var data = new NtfsVolumeData();
        unsafe
        {
            if (!DeviceIoControl(handle, FsctlGetNtfsVolumeData, IntPtr.Zero, 0, &data, (uint)sizeof(NtfsVolumeData), out _, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, $"{path} is not an NTFS volume or volume data is unavailable.");
            }
        }
        return new NtfsVolume(handle, data);
    }

    /// <summary>Reads exactly <paramref name="buffer"/>.Length bytes at a sector-aligned byte offset.</summary>
    public void ReadAt(long offset, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = RandomAccess.Read(_handle, buffer[total..], offset + total);
            if (n <= 0) throw new IOException($"Short read at volume offset {offset + total}.");
            total += n;
        }
    }

    public void Dispose() => _handle.Dispose();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr inBuffer, uint inSize, void* outBuffer, uint outSize, out uint returned, IntPtr overlapped);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NtfsVolumeData
{
    public long VolumeSerialNumber;
    public long NumberSectors;
    public long TotalClusters;
    public long FreeClusters;
    public long TotalReserved;
    public uint BytesPerSector;
    public uint BytesPerCluster;
    public uint BytesPerFileRecordSegment;
    public uint ClustersPerFileRecordSegment;
    public long MftValidDataLength;
    public long MftStartLcn;
    public long Mft2StartLcn;
    public long MftZoneStart;
    public long MftZoneEnd;
}

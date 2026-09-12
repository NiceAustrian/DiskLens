using System.Runtime.CompilerServices;
using DiskLens.Core.Platform;

namespace DiskLens.Scanners.Generic;

/// <summary>Volumes as .NET's <see cref="DriveInfo"/> sees them. Portable, but coarse (no device ids, generic kinds).</summary>
public sealed class DriveInfoVolumeProvider : IVolumeProvider
{
    public async IAsyncEnumerable<VolumeInfo> EnumerateAsync([EnumeratorCancellation] CancellationToken ct)
    {
        // DriveInfo can block on unready removable/network drives; keep it off the caller's thread.
        var drives = await Task.Run(() => DriveInfo.GetDrives().Select(Describe).Where(v => v is not null).ToList()!, ct).ConfigureAwait(false);
        foreach (var v in drives) yield return v!;
    }

    private static VolumeInfo? Describe(DriveInfo d)
    {
        try
        {
            var mount = d.RootDirectory.FullName;
            var id = OperatingSystem.IsWindows() ? d.Name.TrimEnd('\\') : d.Name;

            // Pseudo file systems on Linux are not worth scanning.
            if (!OperatingSystem.IsWindows() && IsPseudo(d.DriveFormat, mount)) return null;

            if (!d.IsReady)
                return new VolumeInfo(id, mount, "", "", Map(d.DriveType), 0, 0, IsReady: false);

            return new VolumeInfo(id, mount, d.VolumeLabel, d.DriveFormat, Map(d.DriveType), d.TotalSize, d.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsPseudo(string fs, string mount) =>
        fs is "proc" or "sysfs" or "devtmpfs" or "devpts" or "cgroup" or "cgroup2" or "tmpfs" or "squashfs" or "overlay"
            or "securityfs" or "pstore" or "bpf" or "debugfs" or "tracefs" or "configfs" or "fusectl" or "mqueue" or "hugetlbfs"
            or "binfmt_misc" or "autofs" or "efivarfs" or "nsfs" or "rpc_pipefs"
        || mount.StartsWith("/snap/", StringComparison.Ordinal)
        || mount.StartsWith("/sys", StringComparison.Ordinal)
        || mount.StartsWith("/proc", StringComparison.Ordinal)
        || mount.StartsWith("/dev", StringComparison.Ordinal)
        || mount.StartsWith("/run", StringComparison.Ordinal);

    private static VolumeKind Map(DriveType t) => t switch
    {
        DriveType.Fixed => VolumeKind.Fixed,
        DriveType.Removable => VolumeKind.Removable,
        DriveType.Network => VolumeKind.Network,
        DriveType.CDRom => VolumeKind.Optical,
        DriveType.Ram => VolumeKind.Ram,
        _ => VolumeKind.Unknown,
    };
}

using DiskLens.Core.Platform;

namespace DiskLens.Core.Scanning;

/// <summary>What to scan: a path, and – if known – the volume it lives on.</summary>
public sealed record ScanTarget(string Path, VolumeInfo? Volume = null)
{
    /// <summary>Volume label for a whole volume, otherwise the folder name.</summary>
    public string DisplayName
    {
        get
        {
            if (Volume is { } v && string.Equals(v.MountPath, Path, StringComparison.OrdinalIgnoreCase)) return v.DisplayName;
            var name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));
            return string.IsNullOrEmpty(name) ? Path : name;
        }
    }
}

public sealed record ScanOptions
{
    public static readonly ScanOptions Default = new();

    /// <summary>Follow symlinks / junctions / mount points into their targets. Off by default: it double-counts.</summary>
    public bool FollowReparsePoints { get; init; }

    /// <summary>Degree of parallelism hint for walk-based scanners. 0 = scanner decides.</summary>
    public int MaxParallelism { get; init; }
}

/// <summary>How well a scanner fits a target. Higher is better; the registry picks the highest.</summary>
public enum Suitability
{
    /// <summary>Cannot scan this target at all (wrong file system, wrong OS, ...).</summary>
    NotApplicable = 0,
    /// <summary>Works, but is the generic path. Chosen if nothing better exists.</summary>
    Fallback = 10,
    /// <summary>Natively supports this target.</summary>
    Supported = 20,
    /// <summary>The best possible scanner for this target (e.g. MFT reader on NTFS).</summary>
    Preferred = 30,
}

public sealed record ScanSuitability(Suitability Level, string Reason, bool RequiresElevation = false)
{
    public static ScanSuitability No(string reason) => new(Suitability.NotApplicable, reason);
    public static ScanSuitability Fallback(string reason) => new(Suitability.Fallback, reason);
    public static ScanSuitability Supported(string reason) => new(Suitability.Supported, reason);
    public static ScanSuitability Preferred(string reason) => new(Suitability.Preferred, reason);
}

[Flags]
public enum ScanCapabilities
{
    None            = 0,
    LiveProgress    = 1 << 0,   // delivers entries incrementally
    AllocatedSize   = 1 << 1,   // reports size on disk, not only logical size
    HardLinkAware   = 1 << 2,   // can detect multiple names for one file
    RequiresElevation = 1 << 3, // needs admin/root to work at all
}

/// <summary>
/// A pluggable scan implementation. Register one per file system / access strategy
/// (NTFS MFT, Win32 walk, POSIX walk, SMB, ...). The <see cref="IScannerRegistry"/> asks each
/// scanner how well it fits a target and picks the best one.
/// </summary>
public interface IFileSystemScanner
{
    /// <summary>Stable identifier, e.g. "ntfs-mft".</summary>
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }
    ScanCapabilities Capabilities { get; }

    /// <summary>Cheap check whether – and how well – this scanner can handle the target.</summary>
    ValueTask<ScanSuitability> ProbeAsync(ScanTarget target, CancellationToken ct);

    /// <summary>Runs the scan, delivering everything into <paramref name="sink"/>.</summary>
    Task ScanAsync(ScanTarget target, IScanSink sink, ScanOptions options, CancellationToken ct);
}

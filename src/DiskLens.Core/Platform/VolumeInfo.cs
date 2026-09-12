namespace DiskLens.Core.Platform;

public enum VolumeKind { Unknown, Fixed, Removable, Network, Optical, Ram, Virtual }

/// <summary>A mounted volume / drive as offered on the start screen.</summary>
public sealed record VolumeInfo(
    string Id,              // stable id, e.g. "C:" or "/dev/nvme0n1p2"
    string MountPath,       // root path to scan, e.g. @"C:\" or "/"
    string Label,           // user-visible label, may be empty
    string FileSystem,      // "NTFS", "ext4", "apfs", ...
    VolumeKind Kind,
    long TotalBytes,
    long FreeBytes,
    bool IsReady = true)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public double UsedFraction => TotalBytes > 0 ? (double)UsedBytes / TotalBytes : 0;
    public string DisplayName => string.IsNullOrEmpty(Label) ? Id : $"{Label} ({Id})";
}

/// <summary>Enumerates scannable volumes. Platform modules provide implementations; all are merged.</summary>
public interface IVolumeProvider
{
    IAsyncEnumerable<VolumeInfo> EnumerateAsync(CancellationToken ct);
}

/// <summary>Answers "what file system is this path on?" for scanner probing.</summary>
public interface IFileSystemInspector
{
    /// <summary>Returns the volume containing <paramref name="path"/>, or null if unknown.</summary>
    ValueTask<VolumeInfo?> GetVolumeForPathAsync(string path, CancellationToken ct);
}

/// <summary>How the UI should describe the extra privilege a platform can offer.</summary>
public sealed record ElevationPrompt(string Title, string Message, string ButtonLabel);

/// <summary>
/// Extra privileges that widen what can be scanned: administrator on Windows (raw MFT access),
/// "All files access" on Android, ... The UI shows <see cref="Prompt"/> until <see cref="IsElevated"/>.
/// </summary>
public interface IElevationService
{
    bool IsElevated { get; }
    bool CanRelaunchElevated { get; }
    /// <summary>Text for the banner offering elevation, or null if the platform has nothing to offer.</summary>
    ElevationPrompt? Prompt { get; }
    /// <summary>Restarts the application with elevated privileges (or opens the system dialog that grants them). Returns false if that failed or was declined.</summary>
    bool RelaunchElevated(string[] args);
}

/// <summary>Default for platforms without a specific implementation.</summary>
public sealed class NoElevationService : IElevationService
{
    public bool IsElevated => false;
    public bool CanRelaunchElevated => false;
    public ElevationPrompt? Prompt => null;
    public bool RelaunchElevated(string[] args) => false;
}

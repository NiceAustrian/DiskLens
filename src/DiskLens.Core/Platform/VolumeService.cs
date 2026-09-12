using Microsoft.Extensions.Logging;

namespace DiskLens.Core.Platform;

/// <summary>Merges all registered <see cref="IVolumeProvider"/>s and de-duplicates by mount path.</summary>
public interface IVolumeService
{
    Task<IReadOnlyList<VolumeInfo>> GetVolumesAsync(CancellationToken ct);
    ValueTask<VolumeInfo?> GetVolumeForPathAsync(string path, CancellationToken ct);
}

public sealed class VolumeService(
    IEnumerable<IVolumeProvider> providers,
    IEnumerable<IFileSystemInspector> inspectors,
    ILogger<VolumeService> logger) : IVolumeService
{
    private readonly IVolumeProvider[] _providers = [.. providers];
    private readonly IFileSystemInspector[] _inspectors = [.. inspectors];

    public async Task<IReadOnlyList<VolumeInfo>> GetVolumesAsync(CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<VolumeInfo>();
        foreach (var provider in _providers)
        {
            try
            {
                await foreach (var v in provider.EnumerateAsync(ct).ConfigureAwait(false))
                {
                    if (seen.Add(v.MountPath)) result.Add(v);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Volume provider {Provider} failed", provider.GetType().Name);
            }
        }
        return result;
    }

    public async ValueTask<VolumeInfo?> GetVolumeForPathAsync(string path, CancellationToken ct)
    {
        foreach (var inspector in _inspectors)
        {
            var v = await inspector.GetVolumeForPathAsync(path, ct).ConfigureAwait(false);
            if (v is not null) return v;
        }
        // Fallback: longest mount path that prefixes the path.
        var volumes = await GetVolumesAsync(ct).ConfigureAwait(false);
        var full = Path.GetFullPath(path);
        return volumes
            .Where(v => full.StartsWith(v.MountPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.MountPath.Length)
            .FirstOrDefault();
    }
}

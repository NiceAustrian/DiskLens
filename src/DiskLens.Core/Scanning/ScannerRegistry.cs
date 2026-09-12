using Microsoft.Extensions.Logging;

namespace DiskLens.Core.Scanning;

/// <summary>Result of probing every registered scanner against a target.</summary>
public sealed record ScannerCandidate(IFileSystemScanner Scanner, ScanSuitability Suitability);

public sealed record ScannerSelection(ScannerCandidate Chosen, IReadOnlyList<ScannerCandidate> All)
{
    /// <summary>A better scanner exists but needs elevation – the UI can offer to relaunch.</summary>
    public ScannerCandidate? BetterWithElevation => All
        .Where(c => c.Suitability.RequiresElevation && c.Suitability.Level > Chosen.Suitability.Level)
        .OrderByDescending(c => c.Suitability.Level)
        .FirstOrDefault();
}

public interface IScannerRegistry
{
    IReadOnlyList<IFileSystemScanner> Scanners { get; }

    /// <summary>
    /// Probes all scanners and picks the best applicable one. If <paramref name="preferredId"/> names an
    /// applicable scanner it wins regardless of level.
    /// </summary>
    Task<ScannerSelection> SelectAsync(ScanTarget target, string? preferredId, CancellationToken ct);
}

public sealed class ScannerRegistry(IEnumerable<IFileSystemScanner> scanners, ILogger<ScannerRegistry> logger) : IScannerRegistry
{
    public IReadOnlyList<IFileSystemScanner> Scanners { get; } = [.. scanners];

    public async Task<ScannerSelection> SelectAsync(ScanTarget target, string? preferredId, CancellationToken ct)
    {
        var candidates = new List<ScannerCandidate>(Scanners.Count);
        foreach (var scanner in Scanners)
        {
            ScanSuitability suitability;
            try
            {
                suitability = await scanner.ProbeAsync(target, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Scanner {Scanner} failed to probe {Path}", scanner.Id, target.Path);
                suitability = ScanSuitability.No($"Probe failed: {ex.Message}");
            }
            logger.LogDebug("Probe {Scanner} for {Path}: {Level} ({Reason})", scanner.Id, target.Path, suitability.Level, suitability.Reason);
            candidates.Add(new ScannerCandidate(scanner, suitability));
        }

        var usable = candidates
            .Where(c => c.Suitability.Level != Suitability.NotApplicable && !c.Suitability.RequiresElevation)
            .ToList();

        var chosen = usable.FirstOrDefault(c => c.Scanner.Id == preferredId)
                     ?? usable.OrderByDescending(c => c.Suitability.Level).FirstOrDefault()
                     ?? throw new InvalidOperationException($"No scanner can handle '{target.Path}'.");

        logger.LogInformation("Using scanner {Scanner} for {Path}: {Reason}", chosen.Scanner.Id, target.Path, chosen.Suitability.Reason);
        return new ScannerSelection(chosen, candidates);
    }
}

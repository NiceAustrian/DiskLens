using System.Diagnostics;
using DiskLens.Core.Model;
using Microsoft.Extensions.Logging;

namespace DiskLens.Core.Scanning;

public enum ScanState { Pending, Running, Completed, Cancelled, Failed }

/// <summary>
/// One scan from start to finish. Owns the tree, the cancellation and the outcome. The tree is
/// readable at any time – during the scan it grows live.
/// </summary>
public sealed class ScanSession : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _stopwatch = new();
    private int _state = (int)ScanState.Pending;

    internal ScanSession(ScanTarget target, ScannerSelection selection, ScanOptions options, FsTreeBuilder builder)
    {
        Target = target;
        Selection = selection;
        Options = options;
        Builder = builder;
    }

    public ScanTarget Target { get; }
    public ScannerSelection Selection { get; }
    public IFileSystemScanner Scanner => Selection.Chosen.Scanner;
    public ScanOptions Options { get; }
    public FsTreeBuilder Builder { get; }
    public FsTree Tree => Builder.Tree;

    public ScanState State => (ScanState)Volatile.Read(ref _state);
    public bool IsRunning => State == ScanState.Running;
    public bool IsFinished => State is ScanState.Completed or ScanState.Cancelled or ScanState.Failed;
    public Exception? Error { get; private set; }
    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public long NodesAdded => Builder.NodesAdded;
    public long BytesSeen => Builder.BytesSeen;
    public IReadOnlyList<ScanError> Errors => Builder.Errors;

    /// <summary>Completes when the scan finishes for whatever reason. Never faults.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    public event Action<ScanSession>? StateChanged;

    public void Cancel() => _cts.Cancel();

    internal void Start(ILogger logger)
    {
        Completion = RunAsync(logger);
    }

    private async Task RunAsync(ILogger logger)
    {
        SetState(ScanState.Running);
        _stopwatch.Start();
        try
        {
            await Task.Run(() => Scanner.ScanAsync(Target, Builder, Options, _cts.Token), _cts.Token).ConfigureAwait(false);
            _stopwatch.Stop();
            logger.LogInformation("Scan of {Path} finished: {Nodes:N0} nodes, {Bytes:N0} bytes in {Elapsed}",
                Target.Path, NodesAdded, BytesSeen, Elapsed);
            SetState(ScanState.Completed);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            _stopwatch.Stop();
            logger.LogInformation("Scan of {Path} cancelled after {Elapsed}", Target.Path, Elapsed);
            SetState(ScanState.Cancelled);
        }
        catch (Exception ex)
        {
            _stopwatch.Stop();
            Error = ex;
            logger.LogError(ex, "Scan of {Path} failed", Target.Path);
            SetState(ScanState.Failed);
        }
    }

    private void SetState(ScanState state)
    {
        Volatile.Write(ref _state, (int)state);
        StateChanged?.Invoke(this);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}

public interface IScanService
{
    /// <summary>Selects a scanner and starts scanning immediately. The returned session is already running.</summary>
    Task<ScanSession> StartAsync(ScanTarget target, ScanOptions? options = null, string? preferredScannerId = null, CancellationToken ct = default);
}

public sealed class ScanService(IScannerRegistry registry, ILogger<ScanService> logger) : IScanService
{
    public async Task<ScanSession> StartAsync(ScanTarget target, ScanOptions? options = null, string? preferredScannerId = null, CancellationToken ct = default)
    {
        var selection = await registry.SelectAsync(target, preferredScannerId, ct).ConfigureAwait(false);
        var builder = new FsTreeBuilder(target.Path, target.DisplayName);
        var session = new ScanSession(target, selection, options ?? ScanOptions.Default, builder);
        session.Start(logger);
        return session;
    }
}

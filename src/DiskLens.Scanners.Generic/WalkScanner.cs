using System.IO.Enumeration;
using DiskLens.Core.Model;
using DiskLens.Core.Scanning;
using Microsoft.Extensions.Logging;

namespace DiskLens.Scanners.Generic;

/// <summary>
/// Portable directory walk on top of <see cref="FileSystemEnumerator{T}"/>. Works on every platform
/// and file system .NET can enumerate; used as the fallback when no native scanner fits.
/// Directories are processed by a pool of workers pulling from a shared work queue, so wide trees
/// scale with I/O parallelism while deep ones still make progress.
/// </summary>
public sealed class WalkScanner(ILogger<WalkScanner> logger) : IFileSystemScanner
{
    public string Id => "generic-walk";
    public string DisplayName => "Directory walk";
    public string Description => "Portable enumeration via .NET. Works everywhere, no special privileges.";
    public ScanCapabilities Capabilities => ScanCapabilities.LiveProgress;

    public ValueTask<ScanSuitability> ProbeAsync(ScanTarget target, CancellationToken ct)
    {
        if (!Directory.Exists(target.Path))
            return ValueTask.FromResult(ScanSuitability.No("Path does not exist or is not a directory."));
        return ValueTask.FromResult(ScanSuitability.Fallback("Portable enumeration."));
    }

    public async Task ScanAsync(ScanTarget target, IScanSink sink, ScanOptions options, CancellationToken ct)
    {
        var parallelism = options.MaxParallelism > 0 ? options.MaxParallelism : Math.Clamp(Environment.ProcessorCount * 2, 4, 32);
        var walk = new Walk(sink, options, logger, ct);
        walk.Enqueue(sink.RootId, target.Path);

        var workers = new Task[parallelism];
        for (var i = 0; i < workers.Length; i++)
            workers[i] = Task.Run(walk.WorkerLoop, ct);

        await Task.WhenAll(workers).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>State of one scan run. Work items are (nodeId, fullPath) pairs.</summary>
    private sealed class Walk(IScanSink sink, ScanOptions options, ILogger logger, CancellationToken ct)
    {
        private readonly Stack<(int Node, string Path)> _queue = new();
        private readonly Lock _gate = new();
        private int _inFlight;                      // directories dequeued but not yet finished
        private readonly SemaphoreSlim _signal = new(0);

        public void Enqueue(int node, string path)
        {
            lock (_gate)
            {
                _queue.Push((node, path));
            }
            _signal.Release();
        }

        public void WorkerLoop()
        {
            var files = new List<FileEntry>(256);
            while (true)
            {
                _signal.Wait(ct);
                (int Node, string Path) item;
                lock (_gate)
                {
                    if (_queue.Count == 0)
                    {
                        // Woken by the "all done" broadcast.
                        if (_inFlight == 0) { _signal.Release(); return; }
                        continue;
                    }
                    item = _queue.Pop();
                    _inFlight++;
                }

                try
                {
                    ProcessDirectory(item.Node, item.Path, files);
                }
                finally
                {
                    bool done;
                    lock (_gate)
                    {
                        _inFlight--;
                        done = _inFlight == 0 && _queue.Count == 0;
                    }
                    if (done) _signal.Release();   // wake one worker; each one wakes the next on exit
                }
            }
        }

        private void ProcessDirectory(int node, string path, List<FileEntry> files)
        {
            ct.ThrowIfCancellationRequested();
            files.Clear();
            try
            {
                using var e = new EntryEnumerator(path, EnumerationOptions);
                while (e.MoveNext())
                {
                    var entry = e.Current;
                    if (entry.IsDirectory)
                    {
                        var child = sink.AddDirectory(node, new DirectoryEntry(entry.Name, entry.ModifiedTicks, entry.Flags));
                        if ((entry.Flags & NodeFlags.ReparsePoint) == 0 || options.FollowReparsePoints)
                            Enqueue(child, entry.FullPath);
                    }
                    else
                    {
                        files.Add(new FileEntry(entry.Name, entry.Size, entry.Size, entry.ModifiedTicks, entry.Flags));
                        if (files.Count >= 4096)
                        {
                            sink.AddFiles(node, [.. files]);
                            files.Clear();
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                sink.ReportError(node, new ScanError(ScanErrorKind.AccessDenied, path, ex.Message));
            }
            catch (DirectoryNotFoundException ex)
            {
                sink.ReportError(node, new ScanError(ScanErrorKind.NotFound, path, ex.Message));
            }
            catch (PathTooLongException ex)
            {
                sink.ReportError(node, new ScanError(ScanErrorKind.TooLong, path, ex.Message));
            }
            catch (IOException ex)
            {
                logger.LogDebug(ex, "I/O error enumerating {Path}", path);
                sink.ReportError(node, new ScanError(ScanErrorKind.IoError, path, ex.Message));
            }
            finally
            {
                if (files.Count > 0) sink.AddFiles(node, [.. files]);
            }
        }

        private static readonly EnumerationOptions EnumerationOptions = new()
        {
            RecurseSubdirectories = false,
            AttributesToSkip = 0,                 // we want hidden + system too
            IgnoreInaccessible = false,           // we report them ourselves
            ReturnSpecialDirectories = false,
            BufferSize = 64 * 1024,
        };

        private readonly record struct Entry(string Name, string FullPath, bool IsDirectory, long Size, long ModifiedTicks, NodeFlags Flags);

        private sealed class EntryEnumerator(string directory, EnumerationOptions options)
            : FileSystemEnumerator<Entry>(directory, options)
        {
            protected override Entry TransformEntry(ref FileSystemEntry entry)
            {
                var attrs = entry.Attributes;
                var flags = NodeFlags.None;
                if (entry.IsDirectory)                                  flags |= NodeFlags.Directory;
                if ((attrs & FileAttributes.Hidden) != 0)               flags |= NodeFlags.Hidden;
                if ((attrs & FileAttributes.System) != 0)               flags |= NodeFlags.System;
                if ((attrs & FileAttributes.ReparsePoint) != 0)         flags |= NodeFlags.ReparsePoint;
                if ((attrs & FileAttributes.Compressed) != 0)           flags |= NodeFlags.Compressed;
                if ((attrs & FileAttributes.SparseFile) != 0)           flags |= NodeFlags.Sparse;
                if ((attrs & FileAttributes.Encrypted) != 0)            flags |= NodeFlags.Encrypted;

                return new Entry(
                    entry.FileName.ToString(),
                    entry.IsDirectory ? entry.ToFullPath() : "",
                    entry.IsDirectory,
                    entry.IsDirectory ? 0 : entry.Length,
                    entry.LastWriteTimeUtc.UtcTicks,
                    flags);
            }
        }
    }
}

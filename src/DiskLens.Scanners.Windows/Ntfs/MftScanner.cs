using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DiskLens.Core;
using DiskLens.Core.Model;
using DiskLens.Core.Platform;
using DiskLens.Core.Scanning;
using Microsoft.Extensions.Logging;

namespace DiskLens.Scanners.Windows.Ntfs;

/// <summary>
/// Reads the NTFS Master File Table directly instead of walking directories: one sequential read of
/// $MFT gives every file with size and parent. Needs administrator rights to open the raw volume.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MftScanner(IElevationService elevation, ILogger<MftScanner> logger) : IFileSystemScanner
{
    public string Id => "ntfs-mft";
    public string DisplayName => "NTFS MFT";
    public string Description => "Reads the Master File Table directly. Fastest possible on NTFS; requires administrator rights.";
    public ScanCapabilities Capabilities => ScanCapabilities.LiveProgress | ScanCapabilities.AllocatedSize | ScanCapabilities.HardLinkAware | ScanCapabilities.RequiresElevation;

    public ValueTask<ScanSuitability> ProbeAsync(ScanTarget target, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return ValueTask.FromResult(ScanSuitability.No("Windows only."));
        var root = Path.GetPathRoot(target.Path);
        if (root is null || root.Length < 2 || root[1] != ':') return ValueTask.FromResult(ScanSuitability.No("Not a drive-letter path."));

        string fs;
        try
        {
            fs = target.Volume?.FileSystem ?? new DriveInfo(root).DriveFormat;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ValueTask.FromResult(ScanSuitability.No("Volume not ready."));
        }
        if (!string.Equals(fs, "NTFS", StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult(ScanSuitability.No($"{fs} is not NTFS."));

        return ValueTask.FromResult(elevation.IsElevated
            ? ScanSuitability.Preferred("NTFS volume, administrator rights available.")
            : ScanSuitability.Preferred("NTFS volume – run as administrator to use the MFT reader.") with { RequiresElevation = true });
    }

    public Task ScanAsync(ScanTarget target, IScanSink sink, ScanOptions options, CancellationToken ct)
    {
        var root = Path.GetPathRoot(target.Path)!;
        sink.ReportPhase("Opening volume…");
        using var volume = NtfsVolume.Open(root);
        var reader = new MftReader(volume);

        sink.ReportPhase("Reading MFT…", 0);
        var mft = reader.Read((done, total) => sink.ReportPhase($"Reading MFT · {ByteSize.Format(done)} / {ByteSize.Format(total)}", (double)done / total), ct);
        logger.LogInformation("MFT read: {Records:N0} records, {Bytes:N0} bytes in {Runs} extents", mft.Count, mft.BytesRead, reader.Runs);

        sink.ReportPhase("Building tree…");
        var children = BuildChildIndex(mft);

        // Resolve the start directory for sub-folder scans by walking the path from the root record.
        var start = MftReader.RootRecord;
        var relative = Path.GetRelativePath(root, Path.GetFullPath(target.Path));
        if (relative != ".")
        {
            foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                var next = -1;
                foreach (var c in children.Of(start))
                {
                    if ((mft.Flags[c] & NodeFlags.Directory) != 0 && string.Equals(mft.Name[c], part, StringComparison.OrdinalIgnoreCase)) { next = c; break; }
                }
                if (next < 0) throw new DirectoryNotFoundException($"'{target.Path}' was not found in the MFT.");
                start = next;
            }
        }

        Emit(mft, children, start, sink, options, ct);
        sink.ReportPhase("Done");
        return Task.CompletedTask;
    }

    /// <summary>Compressed sparse-row child lists: for each parent, a contiguous slice of child indices.</summary>
    private static ChildIndex BuildChildIndex(MftReader.Result mft)
    {
        var n = mft.Count;
        var counts = new int[n + 1];
        for (var i = 0; i < n; i++)
        {
            var p = mft.Parent[i];
            if (p >= 0 && p < n && i != MftReader.RootRecord) counts[p + 1]++;
        }
        for (var i = 0; i < n; i++) counts[i + 1] += counts[i];
        var start = (int[])counts.Clone();
        var items = new int[counts[n]];
        for (var i = 0; i < n; i++)
        {
            var p = mft.Parent[i];
            if (p >= 0 && p < n && i != MftReader.RootRecord) items[start[p]++] = i;
        }
        return new ChildIndex(counts, items);
    }

    private readonly record struct ChildIndex(int[] Offsets, int[] Items)
    {
        public ReadOnlySpan<int> Of(int parent) => Items.AsSpan(Offsets[parent], Offsets[parent + 1] - Offsets[parent]);
    }

    private static void Emit(MftReader.Result mft, ChildIndex children, int startRecord, IScanSink sink, ScanOptions options, CancellationToken ct)
    {
        var stack = new Stack<(int Record, int SinkId)>();
        stack.Push((startRecord, sink.RootId));
        var files = new List<FileEntry>(1024);
        var emitted = 0;

        while (stack.Count > 0)
        {
            var (record, sinkId) = stack.Pop();
            if ((++emitted & 0x3FF) == 0) ct.ThrowIfCancellationRequested();
            files.Clear();

            foreach (var c in children.Of(record))
            {
                var flags = mft.Flags[c];
                if ((flags & NodeFlags.Directory) != 0)
                {
                    var id = sink.AddDirectory(sinkId, new DirectoryEntry(mft.Name[c], mft.Modified[c], flags));
                    if ((flags & NodeFlags.ReparsePoint) == 0 || options.FollowReparsePoints)
                        stack.Push((c, id));
                }
                else
                {
                    files.Add(new FileEntry(mft.Name[c], mft.Size[c], mft.Allocated[c], mft.Modified[c], flags));
                }
            }
            if (files.Count > 0) sink.AddFiles(sinkId, CollectionsMarshal.AsSpan(files));
        }
    }
}

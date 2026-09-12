using DiskLens.Core.Model;

namespace DiskLens.Core.Stats;

public sealed record ExtensionStat(string Extension, long TotalSize, int FileCount)
{
    public string Display => Extension.Length == 0 ? "(none)" : "." + Extension;
}

public sealed record TopFile(int Node, long Size);

public sealed record AgeBucket(string Label, TimeSpan MaxAge, long TotalSize, int FileCount);

public sealed record TreeStatistics(
    IReadOnlyList<ExtensionStat> Extensions,
    IReadOnlyList<TopFile> LargestFiles,
    IReadOnlyList<AgeBucket> AgeBuckets,
    long TotalSize,
    int FileCount,
    int DirCount);

/// <summary>Computes aggregate statistics over a subtree. Single pass over the node arrays.</summary>
public static class TreeStatisticsBuilder
{
    private static readonly (string Label, TimeSpan MaxAge)[] AgeRanges =
    [
        ("Today",        TimeSpan.FromDays(1)),
        ("This week",    TimeSpan.FromDays(7)),
        ("This month",   TimeSpan.FromDays(30)),
        ("This year",    TimeSpan.FromDays(365)),
        ("1–2 years",    TimeSpan.FromDays(730)),
        ("2–5 years",    TimeSpan.FromDays(1826)),
        ("Older",        TimeSpan.MaxValue),
    ];

    public static TreeStatistics Compute(FsTree tree, int root, int topFiles = 100, DateTime? now = null, CancellationToken ct = default)
    {
        var nowTicks = (now ?? DateTime.UtcNow).Ticks;
        var count = tree.Count;
        var byExt = new Dictionary<string, (long size, int files)>(StringComparer.OrdinalIgnoreCase);
        var byExtSpan = byExt.GetAlternateLookup<ReadOnlySpan<char>>();
        var ageSize = new long[AgeRanges.Length];
        var ageCount = new int[AgeRanges.Length];
        var top = new PriorityQueue<int, long>(topFiles + 1);   // min-heap on size: smallest at the top

        long total = 0;
        int files = 0, dirs = 0;

        // Nodes are appended in discovery order, so every descendant of `root` has index > root. We
        // still have to test membership, which we do via a depth-bounded parent walk – cheap because
        // most nodes are shallow relative to the root.
        var rootDepth = tree.Depth(root);
        Span<char> nameBuffer = stackalloc char[FsTree.MaxNameChars];
        for (var n = root; n < count; n++)
        {
            if ((n & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            if (n != root && !IsDescendant(tree, n, root, rootDepth)) continue;

            if (tree.IsDirectory(n))
            {
                if (n != root) dirs++;
                continue;
            }

            files++;
            var size = tree.Size(n);
            total += size;

            var ext = tree.ExtensionSpan(n, nameBuffer);
            string key;
            if (ext.IsEmpty) key = "";
            else if (!byExtSpan.TryGetValue(ext, out key!, out _)) key = ext.ToString().ToLowerInvariant();
            ref var acc = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(byExt, key, out _);
            acc.size += size;
            acc.files++;

            var age = nowTicks - tree.ModifiedTicks(n);
            for (var i = 0; i < AgeRanges.Length; i++)
            {
                if (AgeRanges[i].MaxAge == TimeSpan.MaxValue || age < AgeRanges[i].MaxAge.Ticks)
                {
                    ageSize[i] += size;
                    ageCount[i]++;
                    break;
                }
            }

            if (top.Count < topFiles) top.Enqueue(n, size);
            else if (size > top.PeekPriority()) top.DequeueEnqueue(n, size);
        }

        var extensions = byExt
            .Select(kv => new ExtensionStat(kv.Key, kv.Value.size, kv.Value.files))
            .OrderByDescending(e => e.TotalSize)
            .ToList();

        var largest = new List<TopFile>(top.Count);
        while (top.TryDequeue(out var node, out var size)) largest.Add(new TopFile(node, size));
        largest.Reverse();

        var buckets = AgeRanges
            .Select((r, i) => new AgeBucket(r.Label, r.MaxAge, ageSize[i], ageCount[i]))
            .ToList();

        return new TreeStatistics(extensions, largest, buckets, total, files, dirs);
    }

    private static bool IsDescendant(FsTree tree, int node, int ancestor, int ancestorDepth)
    {
        var d = tree.Depth(node);
        if (d <= ancestorDepth) return false;
        for (; d > ancestorDepth; d--)
        {
            if (tree.HasFlag(node, NodeFlags.Deleted)) return false;   // removed after the scan
            node = tree.Parent(node);
        }
        return node == ancestor;
    }

    private static long PeekPriority(this PriorityQueue<int, long> queue)
    {
        queue.TryPeek(out _, out var priority);
        return priority;
    }
}

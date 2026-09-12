using DiskLens.Core.Model;

namespace DiskLens.Core.Scanning;

/// <summary>
/// The one and only <see cref="IScanSink"/> implementation: turns scanner output into an
/// <see cref="FsTree"/>. A single lock serialises appends; per-directory batching keeps the lock
/// from becoming the bottleneck even with many scanner threads.
/// </summary>
public sealed class FsTreeBuilder : IScanSink
{
    private readonly Lock _gate = new();
    private readonly List<ScanError> _errors = [];
    private long _nodesAdded;
    private long _bytesSeen;

    public FsTreeBuilder(string rootPath, string rootName)
    {
        Tree = new FsTree(rootPath, rootName);
    }

    public FsTree Tree { get; }
    public int RootId => FsTree.Root;

    /// <summary>Live counters for progress reporting.</summary>
    public long NodesAdded => Volatile.Read(ref _nodesAdded);
    public long BytesSeen => Volatile.Read(ref _bytesSeen);

    public IReadOnlyList<ScanError> Errors
    {
        get { lock (_gate) return [.. _errors]; }
    }

    public int AddDirectory(int parent, in DirectoryEntry entry)
    {
        int id;
        lock (_gate)
        {
            id = Tree.AppendNode(parent, entry.Name, entry.Flags | NodeFlags.Directory, 0, 0, entry.ModifiedUtcTicks, Tree.Depth(parent) + 1);
            Tree.Link(parent, id);
        }
        Tree.Propagate(parent, 0, 0, 0, 1, entry.ModifiedUtcTicks);
        Interlocked.Increment(ref _nodesAdded);
        return id;
    }

    public void AddFiles(int parent, ReadOnlySpan<FileEntry> entries)
    {
        if (entries.IsEmpty) return;

        long size = 0, allocated = 0, newest = 0;
        lock (_gate)
        {
            var depth = Tree.Depth(parent) + 1;
            foreach (ref readonly var e in entries)
            {
                var id = Tree.AppendNode(parent, e.Name, e.Flags & ~NodeFlags.Directory, e.Size, e.Allocated, e.ModifiedUtcTicks, depth);
                Tree.Link(parent, id);
                size += e.Size;
                allocated += e.Allocated;
                if (e.ModifiedUtcTicks > newest) newest = e.ModifiedUtcTicks;
            }
        }
        Tree.Propagate(parent, size, allocated, entries.Length, 0, newest);
        Interlocked.Add(ref _nodesAdded, entries.Length);
        Interlocked.Add(ref _bytesSeen, size);
    }

    /// <summary>
    /// Removes a node (and its subtree) from the tree after the user deleted it on disk: unlinks it and
    /// subtracts its totals from every ancestor. Only valid once the scan has finished.
    /// </summary>
    public void RemoveSubtree(int node)
    {
        if (node == FsTree.Root) throw new InvalidOperationException("Cannot remove the root.");
        lock (_gate)
        {
            if (Tree.HasFlag(node, NodeFlags.Deleted)) return;
            Tree.AddFlags(node, NodeFlags.Deleted);
            Tree.Unlink(node);
        }
        var parent = Tree.Parent(node);
        var isDir = Tree.IsDirectory(node);
        Tree.Propagate(parent, -Tree.TotalSize(node), -Tree.TotalAllocated(node), -Tree.FileCount(node), -(Tree.DirCount(node) + (isDir ? 1 : 0)), 0);
    }

    private string? _phase;
    private double? _phaseFraction;

    /// <summary>Current scanner phase, if the scanner reports one.</summary>
    public string? Phase => Volatile.Read(ref _phase);
    public double? PhaseFraction => _phaseFraction;

    public void ReportPhase(string phase, double? fraction = null)
    {
        _phaseFraction = fraction;
        Volatile.Write(ref _phase, phase);
    }

    public void ReportError(int directory, ScanError error)
    {
        var flag = error.Kind == ScanErrorKind.AccessDenied ? NodeFlags.AccessDenied : NodeFlags.Error;
        lock (_gate)
        {
            Tree.AddFlags(directory, flag);
            _errors.Add(error);
        }
    }
}

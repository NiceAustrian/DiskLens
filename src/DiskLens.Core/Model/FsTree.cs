using DiskLens.Core.Collections;

namespace DiskLens.Core.Model;

/// <summary>
/// The complete result of a scan: every file and directory as a node in a struct-of-arrays layout.
/// Node 0 is the root. Aggregates (<see cref="TotalSize"/>, counts) are maintained while the tree is
/// being built so that a UI can render the tree live during a scan.
/// </summary>
/// <remarks>
/// Thread model: exactly one writer at a time appends nodes (the builder serialises this); any number
/// of readers may read indices below <see cref="Count"/>. Aggregate columns are updated with
/// interlocked adds and may be momentarily inconsistent between parent and child – acceptable for a
/// live view, and exact once the scan has finished.
/// </remarks>
public sealed class FsTree
{
    // Structural columns ---------------------------------------------------------------------
    internal readonly ChunkedArray<int>       ParentCol      = new();
    internal readonly ChunkedArray<int>       NameIdCol      = new();   // id in Names
    public NamePool Names { get; } = new();
    internal readonly ChunkedArray<NodeFlags> FlagsCol       = new();
    internal readonly ChunkedArray<long>      SizeCol        = new();   // logical size (files), 0 for dirs
    internal readonly ChunkedArray<long>      AllocatedCol   = new();   // size on disk (files), 0 for dirs
    internal readonly ChunkedArray<long>      ModifiedCol    = new();   // UTC ticks
    internal readonly ChunkedArray<int>       FirstChildCol  = new();   // -1 = none
    internal readonly ChunkedArray<int>       NextSiblingCol = new();   // -1 = none
    internal readonly ChunkedArray<int>       DepthCol       = new();

    // Aggregate columns (files: own values; directories: recursive sums) ---------------------
    internal readonly ChunkedArray<long>      TotalSizeCol      = new();
    internal readonly ChunkedArray<long>      TotalAllocatedCol = new();
    internal readonly ChunkedArray<int>       FileCountCol      = new();
    internal readonly ChunkedArray<int>       DirCountCol       = new();
    internal readonly ChunkedArray<long>      NewestModifiedCol = new();

    public const int Root = 0;
    public const int None = -1;

    public FsTree(string rootPath, string rootName)
    {
        RootPath = rootPath;
        AppendNode(None, Names.Intern(rootName), NodeFlags.Directory | NodeFlags.Root, 0, 0, 0, 0);
    }

    /// <summary>Maximum name length we decode without allocating; longer names are truncated in span APIs.</summary>
    public const int MaxNameChars = 512;

    public string RootPath { get; }

    /// <summary>Number of nodes currently published. Safe to read from any thread.</summary>
    public int Count => ParentCol.Count;

    // Accessors ------------------------------------------------------------------------------
    public int       Parent(int node)         => ParentCol[node];
    /// <summary>The node's name as a string. Allocates – prefer <see cref="NameChars"/> in hot loops.</summary>
    public string    Name(int node)           => Names.GetString(NameIdCol[node]);
    public int       NameId(int node)         => NameIdCol[node];

    /// <summary>Decodes the name into <paramref name="buffer"/> (at least <see cref="MaxNameChars"/>) and returns the used slice.</summary>
    public ReadOnlySpan<char> NameChars(int node, Span<char> buffer)
    {
        var n = Names.GetChars(NameIdCol[node], buffer);
        return n < 0 ? Name(node).AsSpan(0, Math.Min(buffer.Length, Name(node).Length)) : buffer[..n];
    }

    /// <summary>Ordinal name comparison without allocating.</summary>
    public int CompareNames(int a, int b) => Names.Compare(NameIdCol[a], NameIdCol[b]);
    public NodeFlags Flags(int node)          => FlagsCol[node];
    public long      Size(int node)           => SizeCol[node];
    public long      Allocated(int node)      => AllocatedCol[node];
    public DateTime  Modified(int node)       => new(ModifiedCol[node], DateTimeKind.Utc);
    public long      ModifiedTicks(int node)  => ModifiedCol[node];
    public int       Depth(int node)          => DepthCol[node];
    public int       FirstChild(int node)     => Volatile.Read(ref FirstChildCol[node]);
    public int       NextSibling(int node)    => NextSiblingCol[node];

    public long      TotalSize(int node)      => Volatile.Read(ref TotalSizeCol[node]);
    public long      TotalAllocated(int node) => Volatile.Read(ref TotalAllocatedCol[node]);
    public int       FileCount(int node)      => Volatile.Read(ref FileCountCol[node]);
    public int       DirCount(int node)       => Volatile.Read(ref DirCountCol[node]);
    public DateTime  NewestModified(int node) => new(Volatile.Read(ref NewestModifiedCol[node]), DateTimeKind.Utc);

    public bool IsDirectory(int node) => (FlagsCol[node] & NodeFlags.Directory) != 0;
    public bool IsFile(int node)      => (FlagsCol[node] & NodeFlags.Directory) == 0;
    public bool HasFlag(int node, NodeFlags flag) => (FlagsCol[node] & flag) != 0;

    /// <summary>Enumerates direct children (unsorted, most-recently-added first).</summary>
    public ChildEnumerator Children(int node) => new(this, FirstChild(node));

    public int ChildCount(int node)
    {
        var n = 0;
        foreach (var _ in Children(node)) n++;
        return n;
    }

    /// <summary>Full path of a node, using the platform separator.</summary>
    public string FullPath(int node)
    {
        if (node == Root) return RootPath;
        var parts = new List<string>();
        for (var n = node; n != Root; n = ParentCol[n]) parts.Add(Name(n));
        parts.Reverse();
        return Path.Join(RootPath, Path.Join([.. parts]));
    }

    /// <summary>File extension in lower-case without the dot, or "" for none / directories. Allocates.</summary>
    public string Extension(int node)
    {
        Span<char> buffer = stackalloc char[MaxNameChars];
        var span = ExtensionSpan(node, buffer);
        return span.IsEmpty ? "" : span.ToString().ToLowerInvariant();
    }

    /// <summary>File extension without the dot in its original case, or empty; decoded into <paramref name="buffer"/>. No allocation.</summary>
    public ReadOnlySpan<char> ExtensionSpan(int node, Span<char> buffer)
    {
        if (IsDirectory(node)) return default;
        var name = NameChars(node, buffer);
        var dot = name.LastIndexOf('.');
        return dot <= 0 || dot == name.Length - 1 ? default : name[(dot + 1)..];
    }

    // Mutation (builder only) ----------------------------------------------------------------
    internal int AppendNode(int parent, int nameId, NodeFlags flags, long size, long allocated, long modifiedTicks, int depth)
    {
        var id = ParentCol.Add(parent);
        NameIdCol.Add(nameId);
        FlagsCol.Add(flags);
        SizeCol.Add(size);
        AllocatedCol.Add(allocated);
        ModifiedCol.Add(modifiedTicks);
        FirstChildCol.Add(None);
        NextSiblingCol.Add(None);
        DepthCol.Add(depth);

        var isDir = (flags & NodeFlags.Directory) != 0;
        TotalSizeCol.Add(isDir ? 0 : size);
        TotalAllocatedCol.Add(isDir ? 0 : allocated);
        FileCountCol.Add(isDir ? 0 : 1);
        DirCountCol.Add(0);
        NewestModifiedCol.Add(modifiedTicks);
        return id;
    }

    /// <summary>Links a freshly appended node into its parent's child list.</summary>
    internal void Link(int parent, int child)
    {
        NextSiblingCol[child] = FirstChildCol[parent];
        Volatile.Write(ref FirstChildCol[parent], child);
    }

    /// <summary>Propagates aggregate deltas from <paramref name="from"/> (inclusive) up to the root.</summary>
    internal void Propagate(int from, long sizeDelta, long allocatedDelta, int fileDelta, int dirDelta, long newestTicks)
    {
        for (var n = from; n != None; n = ParentCol[n])
        {
            if (sizeDelta != 0)      Interlocked.Add(ref TotalSizeCol[n], sizeDelta);
            if (allocatedDelta != 0) Interlocked.Add(ref TotalAllocatedCol[n], allocatedDelta);
            if (fileDelta != 0)      Interlocked.Add(ref FileCountCol[n], fileDelta);
            if (dirDelta != 0)       Interlocked.Add(ref DirCountCol[n], dirDelta);
            if (newestTicks != 0)
            {
                // lock-free max
                long current;
                while ((current = Volatile.Read(ref NewestModifiedCol[n])) < newestTicks
                       && Interlocked.CompareExchange(ref NewestModifiedCol[n], newestTicks, current) != current) { }
            }
        }
    }

    internal void AddFlags(int node, NodeFlags flags) => FlagsCol[node] |= flags;

    /// <summary>Removes <paramref name="node"/> from its parent's child list. The node keeps its data but is unreachable.</summary>
    internal void Unlink(int node)
    {
        var parent = ParentCol[node];
        if (parent == None) return;
        var first = FirstChildCol[parent];
        if (first == node)
        {
            Volatile.Write(ref FirstChildCol[parent], NextSiblingCol[node]);
            return;
        }
        for (var n = first; n != None; n = NextSiblingCol[n])
        {
            if (NextSiblingCol[n] == node)
            {
                NextSiblingCol[n] = NextSiblingCol[node];
                return;
            }
        }
    }

    public struct ChildEnumerator
    {
        private readonly FsTree _tree;
        private int _next;
        public int Current { get; private set; }

        internal ChildEnumerator(FsTree tree, int first)
        {
            _tree = tree;
            _next = first;
            Current = None;
        }

        public bool MoveNext()
        {
            if (_next == None) return false;
            Current = _next;
            _next = _tree.NextSiblingCol[_next];
            return true;
        }

        public readonly ChildEnumerator GetEnumerator() => this;
    }
}

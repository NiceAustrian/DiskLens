using DiskLens.Core.Collections;

namespace DiskLens.Core.Model;

/// <summary>
/// The complete result of a scan: every file and directory as a node in a struct-of-arrays layout.
/// Node 0 is the root. Aggregates (<see cref="TotalSize"/>, counts) are maintained while the tree is
/// being built so that a UI can render the tree live during a scan.
/// </summary>
/// <remarks>
/// <para>Layout: 40 bytes per node plus 32 bytes per directory (aggregates live in a side table
/// indexed by <see cref="DirIndexCol"/>, since 85 % of nodes are files whose "totals" are just their
/// own size). Names are ids into a de-duplicated UTF-8 <see cref="NamePool"/>. Timestamps are
/// seconds since 1970 (1970–2106) – plenty for file dates and half the size of ticks.</para>
/// <para>Thread model: exactly one writer at a time appends nodes (the builder serialises this);
/// any number of readers may read indices below <see cref="Count"/>. Aggregate columns are updated
/// with interlocked adds and may be momentarily inconsistent between parent and child – acceptable
/// for a live view, and exact once the scan has finished.</para>
/// </remarks>
public sealed class FsTree
{
    // Per-node columns (40 B) ----------------------------------------------------------------
    internal readonly ChunkedArray<int>       ParentCol      = new();
    internal readonly ChunkedArray<int>       NameIdCol      = new();   // id in Names
    internal readonly ChunkedArray<NodeFlags> FlagsCol       = new();
    internal readonly ChunkedArray<ushort>    DepthCol       = new();
    internal readonly ChunkedArray<int>       DirIndexCol    = new();   // index into the directory table, -1 for files
    internal readonly ChunkedArray<long>      SizeCol        = new();   // logical size (files), 0 for dirs
    internal readonly ChunkedArray<long>      AllocatedCol   = new();   // size on disk (files), 0 for dirs
    internal readonly ChunkedArray<uint>      ModifiedCol    = new();   // seconds since 1970 UTC
    internal readonly ChunkedArray<int>       NextSiblingCol = new();   // -1 = none

    // Per-directory columns (32 B), indexed by DirIndexCol ------------------------------------
    internal readonly ChunkedArray<int>       DirFirstChild     = new();   // -1 = none
    internal readonly ChunkedArray<long>      DirTotalSize      = new();
    internal readonly ChunkedArray<long>      DirTotalAllocated = new();
    internal readonly ChunkedArray<int>       DirFileCount      = new();
    internal readonly ChunkedArray<int>       DirDirCount       = new();
    internal readonly ChunkedArray<uint>      DirNewestModified = new();

    public NamePool Names { get; } = new();

    public const int Root = 0;
    public const int None = -1;

    /// <summary>Maximum name length we decode without allocating; longer names are truncated in span APIs.</summary>
    public const int MaxNameChars = 512;

    private const long UnixEpochTicks = 621355968000000000L;

    public FsTree(string rootPath, string rootName)
    {
        RootPath = rootPath;
        AppendNode(None, Names.Intern(rootName), NodeFlags.Directory | NodeFlags.Root, 0, 0, 0, 0);
    }

    public string RootPath { get; }

    /// <summary>Number of nodes currently published. Safe to read from any thread.</summary>
    public int Count => ParentCol.Count;

    /// <summary>Number of directories (including the root).</summary>
    public int DirectoryCount => DirFirstChild.Count;

    // Accessors ------------------------------------------------------------------------------
    public int       Parent(int node)         => ParentCol[node];
    public int       NameId(int node)         => NameIdCol[node];
    public NodeFlags Flags(int node)          => FlagsCol[node];
    public long      Size(int node)           => SizeCol[node];
    public long      Allocated(int node)      => AllocatedCol[node];
    public DateTime  Modified(int node)       => FromSeconds(ModifiedCol[node]);
    public long      ModifiedTicks(int node)  => ToTicks(ModifiedCol[node]);
    public int       Depth(int node)          => DepthCol[node];
    public int       NextSibling(int node)    => NextSiblingCol[node];

    public bool IsDirectory(int node) => (FlagsCol[node] & NodeFlags.Directory) != 0;
    public bool IsFile(int node)      => (FlagsCol[node] & NodeFlags.Directory) == 0;
    public bool HasFlag(int node, NodeFlags flag) => (FlagsCol[node] & flag) != 0;

    public int FirstChild(int node)
    {
        var d = DirIndexCol[node];
        return d < 0 ? None : Volatile.Read(ref DirFirstChild[d]);
    }

    /// <summary>Recursive size for directories, own size for files.</summary>
    public long TotalSize(int node)
    {
        var d = DirIndexCol[node];
        return d < 0 ? SizeCol[node] : Volatile.Read(ref DirTotalSize[d]);
    }

    public long TotalAllocated(int node)
    {
        var d = DirIndexCol[node];
        return d < 0 ? AllocatedCol[node] : Volatile.Read(ref DirTotalAllocated[d]);
    }

    /// <summary>Files below a directory; 1 for a file.</summary>
    public int FileCount(int node)
    {
        var d = DirIndexCol[node];
        return d < 0 ? 1 : Volatile.Read(ref DirFileCount[d]);
    }

    /// <summary>Directories below a directory; 0 for a file.</summary>
    public int DirCount(int node)
    {
        var d = DirIndexCol[node];
        return d < 0 ? 0 : Volatile.Read(ref DirDirCount[d]);
    }

    public DateTime NewestModified(int node)
    {
        var d = DirIndexCol[node];
        return FromSeconds(d < 0 ? ModifiedCol[node] : Volatile.Read(ref DirNewestModified[d]));
    }

    /// <summary>The node's name as a string. Allocates – prefer <see cref="NameChars"/> in hot loops.</summary>
    public string Name(int node) => Names.GetString(NameIdCol[node]);

    /// <summary>Decodes the name into <paramref name="buffer"/> (at least <see cref="MaxNameChars"/>) and returns the used slice.</summary>
    public ReadOnlySpan<char> NameChars(int node, Span<char> buffer)
    {
        var n = Names.GetChars(NameIdCol[node], buffer);
        if (n >= 0) return buffer[..n];
        var full = Name(node);
        var len = Math.Min(buffer.Length, full.Length);
        full.AsSpan(0, len).CopyTo(buffer);
        return buffer[..len];
    }

    /// <summary>Ordinal name comparison without allocating.</summary>
    public int CompareNames(int a, int b) => Names.Compare(NameIdCol[a], NameIdCol[b]);

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

    // Time helpers ---------------------------------------------------------------------------
    public static uint ToSeconds(long utcTicks)
    {
        if (utcTicks <= UnixEpochTicks) return 0;
        var s = (utcTicks - UnixEpochTicks) / TimeSpan.TicksPerSecond;
        return s >= uint.MaxValue ? uint.MaxValue : (uint)s;
    }

    private static long ToTicks(uint seconds) => seconds == 0 ? 0 : UnixEpochTicks + seconds * TimeSpan.TicksPerSecond;
    private static DateTime FromSeconds(uint seconds) => new(ToTicks(seconds), DateTimeKind.Utc);

    // Mutation (builder only) ----------------------------------------------------------------
    internal int AppendNode(int parent, int nameId, NodeFlags flags, long size, long allocated, long modifiedTicks, int depth)
    {
        var isDir = (flags & NodeFlags.Directory) != 0;
        var modified = ToSeconds(modifiedTicks);

        var id = ParentCol.Add(parent);
        NameIdCol.Add(nameId);
        FlagsCol.Add(flags);
        DepthCol.Add((ushort)Math.Min(depth, ushort.MaxValue));
        SizeCol.Add(size);
        AllocatedCol.Add(allocated);
        ModifiedCol.Add(modified);
        NextSiblingCol.Add(None);

        if (isDir)
        {
            var d = DirFirstChild.Add(None);
            DirTotalSize.Add(0);
            DirTotalAllocated.Add(0);
            DirFileCount.Add(0);
            DirDirCount.Add(0);
            DirNewestModified.Add(modified);
            DirIndexCol.Add(d);
        }
        else
        {
            DirIndexCol.Add(-1);
        }
        return id;
    }

    /// <summary>Links a freshly appended node into its parent's child list.</summary>
    internal void Link(int parent, int child)
    {
        var d = DirIndexCol[parent];
        NextSiblingCol[child] = DirFirstChild[d];
        Volatile.Write(ref DirFirstChild[d], child);
    }

    /// <summary>Propagates aggregate deltas from directory <paramref name="from"/> (inclusive) up to the root.</summary>
    internal void Propagate(int from, long sizeDelta, long allocatedDelta, int fileDelta, int dirDelta, long newestTicks)
    {
        var newest = ToSeconds(newestTicks);
        for (var n = from; n != None; n = ParentCol[n])
        {
            var d = DirIndexCol[n];
            if (d < 0) continue;
            if (sizeDelta != 0)      Interlocked.Add(ref DirTotalSize[d], sizeDelta);
            if (allocatedDelta != 0) Interlocked.Add(ref DirTotalAllocated[d], allocatedDelta);
            if (fileDelta != 0)      Interlocked.Add(ref DirFileCount[d], fileDelta);
            if (dirDelta != 0)       Interlocked.Add(ref DirDirCount[d], dirDelta);
            if (newest != 0)
            {
                // lock-free max
                uint current;
                while ((current = Volatile.Read(ref DirNewestModified[d])) < newest
                       && Interlocked.CompareExchange(ref DirNewestModified[d], newest, current) != current) { }
            }
        }
    }

    internal void AddFlags(int node, NodeFlags flags) => FlagsCol[node] |= flags;

    /// <summary>Removes <paramref name="node"/> from its parent's child list. The node keeps its data but is unreachable.</summary>
    internal void Unlink(int node)
    {
        var parent = ParentCol[node];
        if (parent == None) return;
        var d = DirIndexCol[parent];
        var first = DirFirstChild[d];
        if (first == node)
        {
            Volatile.Write(ref DirFirstChild[d], NextSiblingCol[node]);
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

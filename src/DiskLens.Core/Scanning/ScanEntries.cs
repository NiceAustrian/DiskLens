using DiskLens.Core.Model;

namespace DiskLens.Core.Scanning;

/// <summary>A file discovered by a scanner. Scanners fill these and hand them to the <see cref="IScanSink"/>.</summary>
public readonly record struct FileEntry(string Name, long Size, long Allocated, long ModifiedUtcTicks, NodeFlags Flags = NodeFlags.None);

/// <summary>A directory discovered by a scanner.</summary>
public readonly record struct DirectoryEntry(string Name, long ModifiedUtcTicks, NodeFlags Flags = NodeFlags.None);

/// <summary>File entry whose name was already interned into <see cref="IScanSink.Names"/> – no string involved.</summary>
public readonly record struct InternedFileEntry(int NameId, long Size, long Allocated, long ModifiedUtcTicks, NodeFlags Flags = NodeFlags.None);

public readonly record struct InternedDirectoryEntry(int NameId, long ModifiedUtcTicks, NodeFlags Flags = NodeFlags.None);

/// <summary>
/// Where scanners deliver their findings. The sink owns node ids; a scanner must obtain the id of a
/// directory from <see cref="AddDirectory"/> before it can add that directory's children.
/// All members are thread-safe so parallel scanners can call them concurrently.
/// </summary>
public interface IScanSink
{
    /// <summary>Id of the scan root, whose children are the entries of the scanned path.</summary>
    int RootId { get; }

    /// <summary>
    /// The name pool of the tree being built. Scanners that produce names in bulk can intern them
    /// here up front (single-threaded) and hand over ids instead of strings.
    /// </summary>
    Collections.NamePool Names { get; }

    int AddDirectory(int parent, in DirectoryEntry entry);
    int AddDirectory(int parent, in InternedDirectoryEntry entry);

    /// <summary>Adds a batch of files that all share the same parent. Cheaper than one call per file.</summary>
    void AddFiles(int parent, ReadOnlySpan<FileEntry> entries);
    void AddFiles(int parent, ReadOnlySpan<InternedFileEntry> entries);

    /// <summary>Marks a directory as (partially) unreadable and records the error.</summary>
    void ReportError(int directory, ScanError error);

    /// <summary>Optional human-readable phase for scanners with distinct stages ("Reading MFT…").</summary>
    void ReportPhase(string phase, double? fraction = null);
}

public enum ScanErrorKind { AccessDenied, NotFound, IoError, TooLong, Other }

public readonly record struct ScanError(ScanErrorKind Kind, string Path, string Message);

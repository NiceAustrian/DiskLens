using System.Runtime.CompilerServices;
using System.Text;

namespace DiskLens.Core.Collections;

/// <summary>
/// Interned, de-duplicated file names stored as UTF-8 in an append-only byte arena. A name costs
/// its UTF-8 length plus one or three length bytes – roughly a third of a .NET string – and identical
/// names (there are a lot: "native", "index.js", ".gitignore") are stored once.
/// </summary>
/// <remarks>
/// Ids are byte offsets into the arena; chunks never move, so readers may resolve ids while a single
/// writer keeps interning. <see cref="Intern(ReadOnlySpan{char})"/> is not thread-safe – callers
/// serialise it (the tree builder does so under its lock).
/// </remarks>
public sealed class NamePool
{
    private const int ChunkShift = 20;                 // 1 MiB chunks
    private const int ChunkSize = 1 << ChunkShift;
    private const int ChunkMask = ChunkSize - 1;

    private byte[][] _chunks = new byte[4][];
    private int _chunkCount;
    private int _tail;                                  // write position inside the last chunk
    private HashSet<int>? _dedupe;
    private HashSet<int>.AlternateLookup<ReadOnlySpan<byte>> _lookup;

    public NamePool()
    {
        _dedupe = new HashSet<int>(new Comparer(this));
        _lookup = _dedupe.GetAlternateLookup<ReadOnlySpan<byte>>();
        NewChunk();
    }

    /// <summary>Bytes stored so far (arena size).</summary>
    public long Bytes => (long)(_chunkCount - 1) * ChunkSize + _tail;
    public int Count { get; private set; }

    /// <summary>Returns the id of <paramref name="name"/>, adding it if new.</summary>
    public int Intern(ReadOnlySpan<char> name)
    {
        Span<byte> utf8 = stackalloc byte[Encoding.UTF8.GetMaxByteCount(Math.Min(name.Length, 1024))];
        if (name.Length > 1024) return InternLong(name);
        var n = Encoding.UTF8.GetBytes(name, utf8);
        return Intern(utf8[..n]);
    }

    /// <summary>Returns the id of an already UTF-8 encoded name, adding it if new.</summary>
    public int Intern(ReadOnlySpan<byte> utf8)
    {
        if (_dedupe is not null && _lookup.TryGetValue(utf8, out var existing)) return existing;
        var id = Append(utf8);
        _dedupe?.Add(id);
        return id;
    }

    private int InternLong(ReadOnlySpan<char> name) => Intern(Encoding.UTF8.GetBytes(name.ToString()));

    /// <summary>The UTF-8 bytes of a name.</summary>
    public ReadOnlySpan<byte> Get(int id)
    {
        var chunk = _chunks[id >> ChunkShift];
        var off = id & ChunkMask;
        int len;
        if (chunk[off] != 0xFF) { len = chunk[off]; off += 1; }
        else { len = chunk[off + 1] | (chunk[off + 2] << 8); off += 3; }
        return chunk.AsSpan(off, len);
    }

    public string GetString(int id) => Encoding.UTF8.GetString(Get(id));

    /// <summary>Decodes into <paramref name="buffer"/>; returns the number of chars written (-1 if too small).</summary>
    public int GetChars(int id, Span<char> buffer)
    {
        var bytes = Get(id);
        return Encoding.UTF8.GetCharCount(bytes) <= buffer.Length ? Encoding.UTF8.GetChars(bytes, buffer) : -1;
    }

    /// <summary>Ordinal comparison of two names by UTF-8 bytes (equals code-point order).</summary>
    public int Compare(int a, int b) => a == b ? 0 : Get(a).SequenceCompareTo(Get(b));

    public bool Equals(int id, ReadOnlySpan<char> name)
    {
        Span<byte> utf8 = stackalloc byte[Encoding.UTF8.GetMaxByteCount(Math.Min(name.Length, 256))];
        if (name.Length > 256) return GetString(id).AsSpan().SequenceEqual(name);
        var n = Encoding.UTF8.GetBytes(name, utf8);
        return Get(id).SequenceEqual(utf8[..n]);
    }

    public bool EqualsIgnoreCase(int id, ReadOnlySpan<char> name)
    {
        Span<char> buffer = stackalloc char[512];
        var n = GetChars(id, buffer);
        return n < 0 ? GetString(id).AsSpan().Equals(name, StringComparison.OrdinalIgnoreCase)
                     : buffer[..n].Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Drops the de-duplication index once no more names will be added; frees ~16 B per distinct name.</summary>
    public void Seal()
    {
        _dedupe = null;
        _lookup = default;
    }

    private int Append(ReadOnlySpan<byte> utf8)
    {
        var header = utf8.Length < 0xFF ? 1 : 3;
        var needed = header + utf8.Length;
        if (_tail + needed > ChunkSize) NewChunk();

        var chunk = _chunks[_chunkCount - 1];
        var id = ((_chunkCount - 1) << ChunkShift) | _tail;
        if (header == 1) chunk[_tail] = (byte)utf8.Length;
        else { chunk[_tail] = 0xFF; chunk[_tail + 1] = (byte)utf8.Length; chunk[_tail + 2] = (byte)(utf8.Length >> 8); }
        utf8.CopyTo(chunk.AsSpan(_tail + header));
        _tail += needed;
        Count++;
        return id;
    }

    private void NewChunk()
    {
        if (_chunkCount == _chunks.Length)
        {
            var grown = new byte[_chunks.Length * 2][];
            Array.Copy(_chunks, grown, _chunks.Length);
            Volatile.Write(ref _chunks, grown);
        }
        _chunks[_chunkCount] = new byte[ChunkSize];
        Volatile.Write(ref _chunkCount, _chunkCount + 1);
        _tail = 0;
    }

    /// <summary>Hashes/compares ids by the bytes they point to, and spans directly – so lookups need no allocation.</summary>
    private sealed class Comparer(NamePool pool) : IEqualityComparer<int>, IAlternateEqualityComparer<ReadOnlySpan<byte>, int>
    {
        public bool Equals(int x, int y) => x == y || pool.Get(x).SequenceEqual(pool.Get(y));
        public int GetHashCode(int id) => Hash(pool.Get(id));
        public bool Equals(ReadOnlySpan<byte> alternate, int other) => pool.Get(other).SequenceEqual(alternate);
        public int GetHashCode(ReadOnlySpan<byte> alternate) => Hash(alternate);
        public int Create(ReadOnlySpan<byte> alternate) => throw new NotSupportedException();   // we add ids explicitly

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Hash(ReadOnlySpan<byte> bytes)
        {
            var h = new HashCode();
            h.AddBytes(bytes);
            return h.ToHashCode();
        }
    }
}

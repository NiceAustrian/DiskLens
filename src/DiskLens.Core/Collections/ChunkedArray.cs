using System.Runtime.CompilerServices;

namespace DiskLens.Core.Collections;

/// <summary>
/// Append-only array made of fixed-size chunks. Chunks never move once allocated, so a reader
/// that has observed <see cref="Count"/> can safely index anything below it while a single writer
/// keeps appending. Random access is two indirections, which is cheap enough for our use.
/// </summary>
public sealed class ChunkedArray<T>
{
    private const int ChunkShift = 16;                 // 65 536 elements per chunk
    private const int ChunkSize = 1 << ChunkShift;
    private const int ChunkMask = ChunkSize - 1;

    private T[][] _chunks = new T[8][];
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _chunks[index >> ChunkShift][index & ChunkMask];
    }

    /// <summary>Appends a value and returns its index. Single writer only.</summary>
    public int Add(in T value)
    {
        var index = _count;
        EnsureCapacity(index + 1);
        _chunks[index >> ChunkShift][index & ChunkMask] = value;
        Volatile.Write(ref _count, index + 1);
        return index;
    }

    private void EnsureCapacity(int required)
    {
        var chunksNeeded = (required + ChunkMask) >> ChunkShift;
        if (chunksNeeded > _chunks.Length)
        {
            var grown = new T[Math.Max(chunksNeeded, _chunks.Length * 2)][];
            Array.Copy(_chunks, grown, _chunks.Length);
            // Publish the new outer array before any reader can see a Count that needs it.
            Volatile.Write(ref _chunks, grown);
        }
        // Chunks are filled in order, so only the last one can be missing.
        _chunks[chunksNeeded - 1] ??= new T[ChunkSize];
    }
}

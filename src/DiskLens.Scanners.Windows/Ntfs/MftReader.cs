using System.Buffers.Binary;
using System.Runtime.Versioning;
using DiskLens.Core.Model;

namespace DiskLens.Scanners.Windows.Ntfs;

/// <summary>
/// Parses the Master File Table into flat per-record arrays: parent index, name, sizes, times and
/// attribute flags. Reading is sequential over the $MFT extents in large blocks, which is what makes
/// this orders of magnitude faster than walking directories.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class MftReader
{
    // Record header offsets
    private const int HdrUsaOffset = 4, HdrUsaCount = 6, HdrFlags = 22, HdrAttrsOffset = 20, HdrBaseRecord = 32;
    private const ushort RecInUse = 1, RecDirectory = 2;

    // Attribute types
    private const uint AttrStandardInfo = 0x10, AttrFileName = 0x30, AttrData = 0x80, AttrEnd = 0xFFFFFFFF;

    // $FILE_NAME namespaces
    private const byte NsPosix = 0, NsWin32 = 1, NsDos = 2, NsWin32AndDos = 3;

    // Attribute flags in $STANDARD_INFORMATION / $FILE_NAME
    private const uint FaHidden = 0x2, FaSystem = 0x4, FaSparse = 0x200, FaReparse = 0x400, FaCompressed = 0x800, FaEncrypted = 0x4000;
    private const uint FaDirectoryIndex = 0x10000000;

    public const int RootRecord = 5;

    /// <summary>Number of $MFT extents seen by the last <see cref="Read"/>.</summary>
    public int Runs { get; private set; }

    private readonly NtfsVolume _volume;
    private readonly int _recordSize;
    private readonly int _bytesPerCluster;
    private readonly long _windowsEpochTicks = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    public MftReader(NtfsVolume volume)
    {
        _volume = volume;
        _recordSize = (int)volume.Data.BytesPerFileRecordSegment;
        _bytesPerCluster = (int)volume.Data.BytesPerCluster;
    }

    /// <summary>Per-record output. Index = MFT record number. Records not in use have Parent == -1.</summary>
    public sealed class Result
    {
        public required int Count;
        public required int[] Parent;          // MFT index of the parent directory, -1 for unused/root
        public required string[] Name;
        public required long[] Size;           // logical size of the unnamed $DATA stream
        public required long[] Allocated;
        public required long[] Modified;       // UTC ticks
        public required NodeFlags[] Flags;
        public required long BytesRead;
    }

    public Result Read(Action<long, long>? progress, CancellationToken ct)
    {
        // 1. Locate the $MFT extents from record 0.
        var record0 = new byte[_recordSize];
        _volume.ReadAt(_volume.Data.MftStartLcn * _bytesPerCluster, record0);
        ApplyFixups(record0);
        var runs = ParseMftDataRuns(record0);
        var mftLength = _volume.Data.MftValidDataLength;
        var count = (int)Math.Min(int.MaxValue, mftLength / _recordSize);

        var result = new Result
        {
            Count = count,
            Parent = new int[count],
            Name = new string[count],
            Size = new long[count],
            Allocated = new long[count],
            Modified = new long[count],
            Flags = new NodeFlags[count],
            BytesRead = 0,
        };
        Array.Fill(result.Parent, -1);

        // 2. Stream the MFT extents in big aligned blocks. Several reads stay in flight (NVMe wants
        //    queue depth), and each block is parsed on all cores while the next ones load.
        const int BlockSize = 16 * 1024 * 1024;
        const int InFlight = 4;
        var chunks = EnumerateChunks(runs, mftLength, BlockSize).ToList();
        Runs = runs.Count;
        var queue = new Queue<(Task<int> Read, byte[] Buffer, long FirstRecord)>();
        var pool = new Stack<byte[]>();
        for (var i = 0; i < InFlight + 1; i++) pool.Push(new byte[BlockSize]);

        long bytesDone = 0;
        var next = 0;
        void Enqueue()
        {
            while (queue.Count < InFlight && next < chunks.Count)
            {
                var (offset, length, firstRecord) = chunks[next++];
                var buffer = pool.Pop();
                queue.Enqueue((ReadChunkAsync(offset, length, buffer), buffer, firstRecord));
            }
        }

        Enqueue();
        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (read, buffer, firstRecord) = queue.Dequeue();
            var length = read.GetAwaiter().GetResult();

            var records = length / _recordSize;
            var recordSize = _recordSize;
            Parallel.For(0, records, new ParallelOptions { CancellationToken = ct }, i =>
            {
                var index = firstRecord + i;
                if (index >= count) return;
                ParseRecord(buffer.AsSpan(i * recordSize, recordSize), (int)index, result);
            });

            pool.Push(buffer);
            Enqueue();
            bytesDone += length;
            progress?.Invoke(bytesDone, mftLength);
        }
        result.BytesRead = bytesDone;
        return result;
    }

    private Task<int> ReadChunkAsync(long offset, int length, byte[] buffer) =>
        Task.Run(() => { _volume.ReadAt(offset, buffer.AsSpan(0, length)); return length; });

    /// <summary>Splits the $MFT extents into (volume offset, byte length, first record index) chunks.</summary>
    private IEnumerable<(long Offset, int Length, long FirstRecord)> EnumerateChunks(List<(long Lcn, long Clusters)> runs, long mftLength, int blockSize)
    {
        long bytesDone = 0;
        foreach (var (lcn, clusters) in runs)
        {
            var runBytes = clusters * (long)_bytesPerCluster;
            var runOffset = lcn * (long)_bytesPerCluster;
            long pos = 0;
            while (pos < runBytes && bytesDone < mftLength)
            {
                var toRead = (int)Math.Min(blockSize, Math.Min(runBytes - pos, mftLength - bytesDone));
                toRead -= toRead % _recordSize;
                if (toRead <= 0) break;
                yield return (runOffset + pos, toRead, bytesDone / _recordSize);
                pos += toRead;
                bytesDone += toRead;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    private void ParseRecord(Span<byte> rec, int index, Result r)
    {
        if (rec.Length < 42 || rec[0] != (byte)'F' || rec[1] != (byte)'I' || rec[2] != (byte)'L' || rec[3] != (byte)'E') return;
        if (!ApplyFixups(rec)) return;

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(rec[HdrFlags..]);
        if ((flags & RecInUse) == 0) return;

        var baseRef = BinaryPrimitives.ReadUInt64LittleEndian(rec[HdrBaseRecord..]);
        var baseIndex = (int)(baseRef & 0xFFFFFFFFFFFF);
        var isExtension = baseIndex != 0;

        var attrOffset = BinaryPrimitives.ReadUInt16LittleEndian(rec[HdrAttrsOffset..]);
        var isDir = (flags & RecDirectory) != 0;

        string? bestName = null;
        var bestNs = byte.MaxValue;
        var parent = -1;
        var nameCount = 0;
        long modified = 0;
        uint attrFlags = 0;
        long size = 0, allocated = 0;
        var haveData = false;

        var pos = (int)attrOffset;
        while (pos + 16 <= rec.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(rec[pos..]);
            if (type == AttrEnd) break;
            var length = BinaryPrimitives.ReadUInt32LittleEndian(rec[(pos + 4)..]);
            if (length < 16 || pos + length > rec.Length) break;
            var attr = rec.Slice(pos, (int)length);
            var nonResident = attr[8] != 0;
            var nameLength = attr[9];

            switch (type)
            {
                case AttrStandardInfo when !nonResident && !isExtension:
                {
                    var (valOff, valLen) = ResidentValue(attr);
                    if (valLen >= 36)
                    {
                        var v = attr.Slice(valOff, valLen);
                        modified = FileTimeToTicks(BinaryPrimitives.ReadInt64LittleEndian(v[8..]));
                        attrFlags = BinaryPrimitives.ReadUInt32LittleEndian(v[32..]);
                    }
                    break;
                }
                case AttrFileName when !nonResident && !isExtension:
                {
                    var (valOff, valLen) = ResidentValue(attr);
                    if (valLen >= 66)
                    {
                        var v = attr.Slice(valOff, valLen);
                        var ns = v[65];
                        var len = v[64];
                        if (66 + len * 2 <= valLen && ns != NsDos)
                        {
                            nameCount++;
                            // Prefer Win32 names; POSIX names are legal too. Keep the first best.
                            var rank = ns == NsWin32AndDos ? (byte)0 : ns == NsWin32 ? (byte)1 : (byte)2;
                            if (rank < bestNs)
                            {
                                bestNs = rank;
                                bestName = string.Create(len, v.Slice(66, len * 2).ToArray(), static (dst, src) =>
                                {
                                    for (var i = 0; i < dst.Length; i++) dst[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(src.AsSpan(i * 2));
                                });
                                parent = (int)(BinaryPrimitives.ReadUInt64LittleEndian(v) & 0xFFFFFFFFFFFF);
                                if (modified == 0) modified = FileTimeToTicks(BinaryPrimitives.ReadInt64LittleEndian(v[16..]));
                                if ((BinaryPrimitives.ReadUInt32LittleEndian(v[56..]) & FaDirectoryIndex) != 0) isDir = true;
                            }
                        }
                    }
                    break;
                }
                case AttrData when nameLength == 0:
                {
                    if (nonResident)
                    {
                        var lowestVcn = BinaryPrimitives.ReadUInt64LittleEndian(attr[16..]);
                        if (lowestVcn == 0)
                        {
                            allocated = BinaryPrimitives.ReadInt64LittleEndian(attr[40..]);
                            size = BinaryPrimitives.ReadInt64LittleEndian(attr[48..]);
                            var cflags = BinaryPrimitives.ReadUInt16LittleEndian(attr[12..]);
                            if ((cflags & 0x0001) != 0 || (cflags & 0x8000) != 0)
                            {
                                // Compressed / sparse: the allocated size reflects what is really used on disk.
                                // The attribute header carries "compressed size" at 64 when the flag is set.
                                if (length >= 72) allocated = BinaryPrimitives.ReadInt64LittleEndian(attr[64..]);
                            }
                            haveData = true;
                        }
                    }
                    else
                    {
                        var (_, valLen) = ResidentValue(attr);
                        size = valLen;
                        allocated = 0;      // lives inside the MFT record itself
                        haveData = true;
                    }
                    break;
                }
            }
            pos += (int)length;
        }

        if (isExtension)
        {
            // Extension records only contribute the unnamed $DATA sizes of fragmented files.
            if (haveData && baseIndex < r.Count)
            {
                r.Size[baseIndex] = size;
                r.Allocated[baseIndex] = allocated;
            }
            return;
        }

        if (bestName is null || index == RootRecord && parent == RootRecord)
        {
            if (index == RootRecord) { r.Name[index] = ""; r.Parent[index] = -1; r.Flags[index] = NodeFlags.Directory | NodeFlags.Root; r.Modified[index] = modified; }
            return;
        }

        var nf = isDir ? NodeFlags.Directory : NodeFlags.None;
        if ((attrFlags & FaHidden) != 0) nf |= NodeFlags.Hidden;
        if ((attrFlags & FaSystem) != 0) nf |= NodeFlags.System;
        if ((attrFlags & FaReparse) != 0) nf |= NodeFlags.ReparsePoint;
        if ((attrFlags & FaCompressed) != 0) nf |= NodeFlags.Compressed;
        if ((attrFlags & FaSparse) != 0) nf |= NodeFlags.Sparse;
        if ((attrFlags & FaEncrypted) != 0) nf |= NodeFlags.Encrypted;
        if (nameCount > 1) nf |= NodeFlags.HardLink;

        r.Name[index] = bestName;
        r.Parent[index] = parent;
        r.Modified[index] = modified;
        r.Flags[index] = nf;
        if (haveData)
        {
            r.Size[index] = size;
            r.Allocated[index] = allocated;
        }
    }

    private static (int Offset, int Length) ResidentValue(ReadOnlySpan<byte> attr)
    {
        var len = (int)BinaryPrimitives.ReadUInt32LittleEndian(attr[16..]);
        var off = BinaryPrimitives.ReadUInt16LittleEndian(attr[20..]);
        if (off + len > attr.Length) return (0, 0);
        return (off, len);
    }

    /// <summary>Undoes the NTFS update-sequence protection: restores the last two bytes of every sector.</summary>
    private static bool ApplyFixups(Span<byte> rec)
    {
        var usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(rec[HdrUsaOffset..]);
        var usaCount = BinaryPrimitives.ReadUInt16LittleEndian(rec[HdrUsaCount..]);
        if (usaCount < 2 || usaOffset + usaCount * 2 > rec.Length) return false;
        var usn = BinaryPrimitives.ReadUInt16LittleEndian(rec[usaOffset..]);
        var sectorSize = rec.Length / (usaCount - 1);
        for (var i = 1; i < usaCount; i++)
        {
            var sectorEnd = i * sectorSize - 2;
            if (sectorEnd + 2 > rec.Length) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(rec[sectorEnd..]) != usn) return false;   // torn record
            rec[sectorEnd] = rec[usaOffset + i * 2];
            rec[sectorEnd + 1] = rec[usaOffset + i * 2 + 1];
        }
        return true;
    }

    private static List<(long Lcn, long Clusters)> ParseMftDataRuns(ReadOnlySpan<byte> rec)
    {
        var pos = (int)BinaryPrimitives.ReadUInt16LittleEndian(rec[HdrAttrsOffset..]);
        while (pos + 16 <= rec.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(rec[pos..]);
            if (type == AttrEnd) break;
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(rec[(pos + 4)..]);
            if (length < 16) break;
            if (type == AttrData && rec[pos + 8] != 0 && rec[pos + 9] == 0)
            {
                var runsOffset = BinaryPrimitives.ReadUInt16LittleEndian(rec[(pos + 32)..]);
                return DecodeRuns(rec.Slice(pos + runsOffset, length - runsOffset));
            }
            pos += length;
        }
        throw new InvalidDataException("$MFT record has no non-resident $DATA attribute.");
    }

    private static List<(long Lcn, long Clusters)> DecodeRuns(ReadOnlySpan<byte> runs)
    {
        var list = new List<(long, long)>();
        long lcn = 0;
        var pos = 0;
        while (pos < runs.Length && runs[pos] != 0)
        {
            var header = runs[pos++];
            var lenSize = header & 0x0F;
            var offSize = header >> 4;
            if (lenSize == 0 || pos + lenSize + offSize > runs.Length) break;

            long len = 0;
            for (var i = 0; i < lenSize; i++) len |= (long)runs[pos + i] << (8 * i);
            pos += lenSize;

            long off = 0;
            for (var i = 0; i < offSize; i++) off |= (long)runs[pos + i] << (8 * i);
            if (offSize > 0 && (runs[pos + offSize - 1] & 0x80) != 0) off -= 1L << (8 * offSize);   // sign-extend
            pos += offSize;

            if (offSize == 0) continue;   // sparse run – not expected in $MFT
            lcn += off;
            list.Add((lcn, len));
        }
        return list;
    }

    private long FileTimeToTicks(long fileTime) => fileTime <= 0 ? 0 : _windowsEpochTicks + fileTime;
}

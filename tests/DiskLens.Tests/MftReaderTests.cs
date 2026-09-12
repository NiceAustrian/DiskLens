using System.Buffers.Binary;
using DiskLens.Scanners.Windows.Ntfs;

namespace DiskLens.Tests;

public class MftReaderTests
{
    [Fact]
    public void DecodeRuns_handles_positive_and_negative_offsets()
    {
        // Run 1: length 0x10 clusters at LCN 0x1000 (header 0x21: 1 length byte, 2 offset bytes)
        // Run 2: length 0x05 clusters at LCN 0x1000 - 0x300 = 0xD00 (offset -0x300 as signed 2 bytes)
        // Run 3: length 0x02 clusters at LCN 0xD00 + 0x20 (header 0x11: 1 length byte, 1 offset byte)
        byte[] runs =
        [
            0x21, 0x10, 0x00, 0x10,
            0x21, 0x05, 0x00, 0xFD,
            0x11, 0x02, 0x20,
            0x00,
        ];

        var result = MftReader.DecodeRuns(runs);

        Assert.Equal(3, result.Count);
        Assert.Equal((0x1000L, 0x10L), result[0]);
        Assert.Equal((0xD00L, 0x05L), result[1]);
        Assert.Equal((0xD20L, 0x02L), result[2]);
    }

    [Fact]
    public void DecodeRuns_skips_sparse_runs_without_offset()
    {
        byte[] runs = [0x21, 0x08, 0x34, 0x12, 0x01, 0x04, 0x11, 0x01, 0x01, 0x00];
        var result = MftReader.DecodeRuns(runs);

        Assert.Equal(2, result.Count);
        Assert.Equal((0x1234L, 0x08L), result[0]);
        Assert.Equal((0x1235L, 0x01L), result[1]);   // relative to the last real LCN, sparse run ignored
    }

    [Fact]
    public void ApplyFixups_restores_sector_tails_and_detects_torn_records()
    {
        var rec = new byte[1024];
        // Update sequence array at offset 48: USN + 2 sector entries (1024 / 512).
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(4), 48);
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(6), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(48), 0xABCD);   // USN
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(50), 0x1111);   // original bytes of sector 1 tail
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(52), 0x2222);   // original bytes of sector 2 tail
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(510), 0xABCD);  // on-disk tails carry the USN
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(1022), 0xABCD);

        Assert.True(MftReader.ApplyFixups(rec));
        Assert.Equal(0x1111, BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(510)));
        Assert.Equal(0x2222, BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(1022)));

        // A torn write leaves a stale USN in one sector.
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(510), 0xABCD);
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(1022), 0x0000);
        Assert.False(MftReader.ApplyFixups(rec));
    }
}

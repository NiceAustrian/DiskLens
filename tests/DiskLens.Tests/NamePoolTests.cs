using DiskLens.Core.Collections;

namespace DiskLens.Tests;

public class NamePoolTests
{
    [Fact]
    public void Interning_deduplicates_and_round_trips()
    {
        var pool = new NamePool();
        var a = pool.Intern("index.js");
        var b = pool.Intern("native");
        var c = pool.Intern("index.js");

        Assert.Equal(a, c);
        Assert.NotEqual(a, b);
        Assert.Equal(2, pool.Count);
        Assert.Equal("index.js", pool.GetString(a));
        Assert.Equal("native", pool.GetString(b));
    }

    [Fact]
    public void Non_ascii_and_long_names_survive()
    {
        var pool = new NamePool();
        var umlaut = "Größe – Übersicht 日本語.txt";
        var longName = new string('x', 300) + ".bin";
        var a = pool.Intern(umlaut);
        var b = pool.Intern(longName);

        Assert.Equal(umlaut, pool.GetString(a));
        Assert.Equal(longName, pool.GetString(b));
        Assert.True(pool.Equals(a, umlaut));
        Assert.False(pool.Equals(a, "Groesse"));

        Span<char> buffer = stackalloc char[512];
        var n = pool.GetChars(b, buffer);
        Assert.Equal(longName.Length, n);
        Assert.True(buffer[..n].SequenceEqual(longName));
    }

    [Fact]
    public void Compare_is_ordinal()
    {
        var pool = new NamePool();
        var a = pool.Intern("apple");
        var b = pool.Intern("Banana");
        var c = pool.Intern("apple");
        Assert.True(pool.Compare(b, a) < 0);   // 'B' (0x42) < 'a' (0x61)
        Assert.Equal(0, pool.Compare(a, c));
    }

    [Fact]
    public void Survives_chunk_boundaries_and_sealing()
    {
        var pool = new NamePool();
        var ids = new List<int>();
        var name = new string('n', 200);
        for (var i = 0; i < 12_000; i++) ids.Add(pool.Intern(name + i));   // ~2.4 MB > one chunk

        pool.Seal();
        Assert.Equal(name + "11999", pool.GetString(ids[^1]));
        Assert.Equal(name + "0", pool.GetString(ids[0]));
        // After sealing, interning still works (just without de-duplication).
        Assert.Equal(name + "5", pool.GetString(pool.Intern(name + "5")));
    }
}

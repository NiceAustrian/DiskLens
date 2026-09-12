using DiskLens.Core.Model;
using DiskLens.Core.Scanning;
using DiskLens.Core.Stats;
using DiskLens.Scanners.Generic;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiskLens.Tests;

public sealed class WalkScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "disklens-test-" + Guid.NewGuid().ToString("N"));

    public WalkScannerTests()
    {
        // root/
        //   a.txt      (100 B)
        //   sub1/      b.bin (2000 B), c.txt (50 B)
        //   sub1/deep/ d.log (7 B)
        //   sub2/      (empty)
        Directory.CreateDirectory(Path.Combine(_root, "sub1", "deep"));
        Directory.CreateDirectory(Path.Combine(_root, "sub2"));
        File.WriteAllBytes(Path.Combine(_root, "a.txt"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_root, "sub1", "b.bin"), new byte[2000]);
        File.WriteAllBytes(Path.Combine(_root, "sub1", "c.txt"), new byte[50]);
        File.WriteAllBytes(Path.Combine(_root, "sub1", "deep", "d.log"), new byte[7]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private async Task<FsTree> ScanAsync()
    {
        var scanner = new WalkScanner(NullLogger<WalkScanner>.Instance);
        var builder = new FsTreeBuilder(_root, "root");
        await scanner.ScanAsync(new ScanTarget(_root), builder, ScanOptions.Default, CancellationToken.None);
        return builder.Tree;
    }

    [Fact]
    public async Task Aggregates_sizes_and_counts_up_to_root()
    {
        var tree = await ScanAsync();

        Assert.Equal(8, tree.Count);                       // root + 3 dirs + 4 files
        Assert.Equal(2157, tree.TotalSize(FsTree.Root));
        Assert.Equal(4, tree.FileCount(FsTree.Root));
        Assert.Equal(3, tree.DirCount(FsTree.Root));

        var sub1 = Find(tree, FsTree.Root, "sub1");
        Assert.Equal(2057, tree.TotalSize(sub1));
        Assert.Equal(3, tree.FileCount(sub1));
        Assert.Equal(1, tree.DirCount(sub1));

        var deep = Find(tree, sub1, "deep");
        Assert.Equal(7, tree.TotalSize(deep));
        Assert.Equal(2, tree.Depth(deep));

        var sub2 = Find(tree, FsTree.Root, "sub2");
        Assert.Equal(0, tree.TotalSize(sub2));
        Assert.Equal(FsTree.None, tree.FirstChild(sub2));
    }

    [Fact]
    public async Task FullPath_and_extension_are_reconstructed()
    {
        var tree = await ScanAsync();
        var sub1 = Find(tree, FsTree.Root, "sub1");
        var deep = Find(tree, sub1, "deep");
        var d = Find(tree, deep, "d.log");

        Assert.Equal(Path.Combine(_root, "sub1", "deep", "d.log"), tree.FullPath(d));
        Assert.Equal("log", tree.Extension(d));
        Assert.Equal("", tree.Extension(deep));
        Assert.True(tree.IsFile(d));
        Assert.True(tree.IsDirectory(deep));
    }

    [Fact]
    public async Task Statistics_group_by_extension_and_rank_largest()
    {
        var tree = await ScanAsync();
        var stats = TreeStatisticsBuilder.Compute(tree, FsTree.Root, topFiles: 2);

        Assert.Equal(2157, stats.TotalSize);
        Assert.Equal(4, stats.FileCount);
        Assert.Equal(3, stats.DirCount);

        Assert.Equal("bin", stats.Extensions[0].Extension);
        Assert.Equal(2000, stats.Extensions[0].TotalSize);
        var txt = stats.Extensions.Single(e => e.Extension == "txt");
        Assert.Equal(150, txt.TotalSize);
        Assert.Equal(2, txt.FileCount);

        Assert.Equal(2, stats.LargestFiles.Count);
        Assert.Equal(2000, stats.LargestFiles[0].Size);
        Assert.Equal(100, stats.LargestFiles[1].Size);

        Assert.Equal(2157, stats.AgeBuckets[0].TotalSize);   // everything was just written → "Today"
    }

    [Fact]
    public async Task Subtree_statistics_only_count_descendants()
    {
        var tree = await ScanAsync();
        var sub1 = Find(tree, FsTree.Root, "sub1");
        var stats = TreeStatisticsBuilder.Compute(tree, sub1);

        Assert.Equal(2057, stats.TotalSize);
        Assert.Equal(3, stats.FileCount);
        Assert.Equal(1, stats.DirCount);
        Assert.DoesNotContain(stats.Extensions, e => e.Extension == "txt" && e.FileCount == 2);
    }

    [Fact]
    public async Task Cancellation_stops_the_scan()
    {
        var scanner = new WalkScanner(NullLogger<WalkScanner>.Instance);
        var builder = new FsTreeBuilder(_root, "root");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scanner.ScanAsync(new ScanTarget(_root), builder, ScanOptions.Default, cts.Token));
    }

    private static int Find(FsTree tree, int parent, string name)
    {
        foreach (var c in tree.Children(parent))
            if (tree.Name(c) == name) return c;
        throw new Xunit.Sdk.XunitException($"'{name}' not found under node {parent}");
    }
}

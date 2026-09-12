using DiskLens.Presentation.Shell;
using DiskLens.Core;
using DiskLens.Scanners.Generic;
using DiskLens.Scanners.Posix;
using DiskLens.Scanners.Windows;
using DiskLens.UI.Elements;
using DiskLens.UI.Hosting;
using DiskLens.UI.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

static class Program
{
    /// <summary>STA: the Explorer context menu (shell COM) requires an apartment-threaded UI thread.</summary>
    [STAThread]
    static int Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss.fff "; });
        builder.Logging.SetMinimumLevel(args.Contains("--debug") ? LogLevel.Debug : LogLevel.Information);

        builder.Services
            .AddDiskLensCore()
            .AddGenericScanners();

        if (OperatingSystem.IsWindows())
            builder.Services.AddWindowsScanners();
        else
            builder.Services.AddPosixScanners();

        // --window WxH: initial size (handy for checking the phone layout on a desktop)
        var winArg = args.SkipWhile(a => a != "--window").Skip(1).FirstOrDefault()?.Split('x');
        var winW = winArg?.Length == 2 && int.TryParse(winArg[0], out var pw) ? pw : 1360;
        var winH = winArg?.Length == 2 && int.TryParse(winArg[1], out var ph) ? ph : 860;
        builder.Services.AddSingleton(new WindowConfig("DiskLens", winW, winH) { IconPng = DiskLens.Presentation.PresentationAssets.IconPng });
        builder.Services.AddSingleton(sp => new UiRoot(args.Contains("--light") ? Theme.Light : Theme.Dark));
        builder.Services.AddSingleton(sp => new AppWindow(
            sp.GetRequiredService<WindowConfig>(),
            sp.GetRequiredService<UiRoot>(),
            sp.GetRequiredService<ILogger<AppWindow>>()));
        builder.Services.AddSingleton<AppShell>();

        using var host = builder.Build();

        // Headless benchmark: DiskLens --bench <path> [--scanner <id>]
        if (args.Contains("--bench"))
        {
            if (OperatingSystem.IsWindows()) ConsoleAttach.AttachToParent();
            var path = args.FirstOrDefault(a => !a.StartsWith('-') && Directory.Exists(a)) ?? Directory.GetCurrentDirectory();
            var scannerIdx = Array.IndexOf(args, "--scanner");
            var scannerId = scannerIdx >= 0 && scannerIdx + 1 < args.Length ? args[scannerIdx + 1] : null;
            var scans = host.Services.GetRequiredService<DiskLens.Core.Scanning.IScanService>();
            var volumes = host.Services.GetRequiredService<DiskLens.Core.Platform.IVolumeService>();
            var volume = volumes.GetVolumeForPathAsync(path, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            var session = scans.StartAsync(new DiskLens.Core.Scanning.ScanTarget(path, volume), preferredScannerId: scannerId).GetAwaiter().GetResult();
            while (!session.IsFinished)
            {
                Thread.Sleep(500);
                Console.WriteLine($"  {session.Phase ?? ""} {session.NodesAdded:N0} nodes, {DiskLens.Core.ByteSize.Format(session.BytesSeen)}");
            }
            var tree = session.Tree;
            Console.WriteLine($"{session.State}: {session.Scanner.Id} · {tree.FileCount(0):N0} files · {tree.DirCount(0):N0} dirs · {DiskLens.Core.ByteSize.Format(tree.TotalSize(0))} · {session.Elapsed.TotalSeconds:0.00}s · {session.Errors.Count} errors");
            if (session.Error is not null) Console.WriteLine(session.Error);
            Console.WriteLine($"Working set: {Environment.WorkingSet / 1024 / 1024} MB, GC heap: {GC.GetTotalMemory(false) / 1024 / 1024} MB");

            // Name statistics: how much do strings cost and how many are duplicates?
            long chars = 0, nonAscii = 0;
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < tree.Count; i++)
            {
                var n = tree.Name(i);
                chars += n.Length;
                foreach (var c in n) if (c > 127) { nonAscii++; break; }
                distinct.Add(n);
            }
            Console.WriteLine($"Names: {tree.Count:N0} nodes, avg {chars / (double)tree.Count:0.0} chars, {distinct.Count:N0} distinct ({100.0 * distinct.Count / tree.Count:0.0}%), {nonAscii:N0} non-ASCII, pool {tree.Names.Bytes / 1024 / 1024} MB");
            Console.WriteLine($"Columns: ~{(tree.Count * 40L + tree.DirectoryCount * 32L) / 1024 / 1024} MB (40 B/node + 32 B/dir, {tree.DirectoryCount:N0} dirs)");
            return 0;
        }

        var window = host.Services.GetRequiredService<AppWindow>();
        var shell = host.Services.GetRequiredService<AppShell>();
        // First positional argument = path to scan; option values (--window WxH, --scanner id) are skipped.
        string? initialPath = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--window" or "--scanner") { i++; continue; }
            if (!args[i].StartsWith('-')) { initialPath = args[i]; break; }
        }
        shell.Attach(window, initialPath);
        window.Run();
        return 0;
    }
}

static partial class ConsoleAttach
{
    [System.Runtime.InteropServices.LibraryImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool AttachConsole(int pid);

    /// <summary>WinExe apps have no console; attach to the parent's so --bench output is visible.</summary>
    public static void AttachToParent()
    {
        if (AttachConsole(-1))
        {
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdout);
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
    }
}

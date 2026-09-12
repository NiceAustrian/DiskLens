using DiskLens.App.Shell;
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

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss.fff "; });
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services
    .AddDiskLensCore()
    .AddGenericScanners();

if (OperatingSystem.IsWindows())
    builder.Services.AddWindowsScanners();
else
    builder.Services.AddPosixScanners();

builder.Services.AddSingleton(new WindowConfig("DiskLens", 1360, 860));
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
    var volume = await volumes.GetVolumeForPathAsync(path, CancellationToken.None);
    var session = await scans.StartAsync(new DiskLens.Core.Scanning.ScanTarget(path, volume), preferredScannerId: scannerId);
    while (!session.IsFinished)
    {
        await Task.Delay(500);
        Console.WriteLine($"  {session.Phase ?? ""} {session.NodesAdded:N0} nodes, {DiskLens.Core.ByteSize.Format(session.BytesSeen)}");
    }
    var tree = session.Tree;
    Console.WriteLine($"{session.State}: {session.Scanner.Id} · {tree.FileCount(0):N0} files · {tree.DirCount(0):N0} dirs · {DiskLens.Core.ByteSize.Format(tree.TotalSize(0))} · {session.Elapsed.TotalSeconds:0.00}s · {session.Errors.Count} errors");
    if (session.Error is not null) Console.WriteLine(session.Error);
    Console.WriteLine($"Working set: {Environment.WorkingSet / 1024 / 1024} MB");
    return;
}

var window = host.Services.GetRequiredService<AppWindow>();
var shell = host.Services.GetRequiredService<AppShell>();
shell.Attach(window, args.FirstOrDefault(a => !a.StartsWith('-')));
window.Run();

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

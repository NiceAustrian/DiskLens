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
builder.Services.AddSingleton(sp => new UiRoot(Theme.Dark));
builder.Services.AddSingleton(sp => new AppWindow(
    sp.GetRequiredService<WindowConfig>(),
    sp.GetRequiredService<UiRoot>(),
    sp.GetRequiredService<ILogger<AppWindow>>()));
builder.Services.AddSingleton<AppShell>();

using var host = builder.Build();

var window = host.Services.GetRequiredService<AppWindow>();
var shell = host.Services.GetRequiredService<AppShell>();
shell.Attach(window);
window.Run();

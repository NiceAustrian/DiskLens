using DiskLens.App.Controls;
using DiskLens.App.Shell;
using DiskLens.Core.Platform;
using DiskLens.Core.Scanning;
using DiskLens.UI.Elements;
using DiskLens.UI.Layout;
using DiskLens.UI.Rendering;
using DiskLens.UI.Widgets;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace DiskLens.App.Views;

/// <summary>Start screen: drive tiles plus a free-form folder input.</summary>
public sealed class HomeView : Element
{
    private readonly AppShell _shell;
    private readonly WrapPanel _tiles = new() { CellWidth = 250, CellHeight = 128, Gap = 14 };
    private readonly Label _status = new("Looking for drives…") { StyleSelector = t => t.Small };
    private readonly TextBox _path = new() { Placeholder = "Or type a folder path… (e.g. C:\\Users)", Icon = Icon.Folder, UseMonospace = true };

    public HomeView(AppShell shell)
    {
        _shell = shell;

        var scroll = Add(new ScrollView());
        var page = scroll.Add(new Column { Padding = new Thickness(32, 28), Gap = 18, MaxWidth = 1200 });

        page.Add(new Label("Drives") { StyleSelector = t => t.Heading });
        page.Add(_status);
        page.Add(_tiles);

        page.Add(new Label("Folder") { StyleSelector = t => t.Heading, Margin = new Thickness(0, 16, 0, 0) });
        var row = page.Add(new Row { Gap = 10 });
        _path.Flex = 1;
        row.Add(_path);
        var go = row.Add(new Button("Scan folder", Icon.Search) { Style = ButtonStyle.Primary });
        go.Activated += () => ScanPath(_path.Text);
        _path.Submitted += ScanPath;

        _ = LoadVolumesAsync();
    }

    private async Task LoadVolumesAsync()
    {
        try
        {
            var volumes = await _shell.Volumes.GetVolumesAsync(CancellationToken.None);
            _shell.Post(() =>
            {
                _tiles.ClearChildren();
                foreach (var v in volumes.OrderBy(v => v.Kind != VolumeKind.Fixed).ThenBy(v => v.MountPath))
                {
                    var tile = new DriveTile(v);
                    tile.Clicked += _ => { if (v.IsReady) _shell.StartScan(new ScanTarget(v.MountPath, v)); };
                    _tiles.Add(tile);
                }
                _status.Text = volumes.Count == 0 ? "No drives found." : $"{volumes.Count} volume{(volumes.Count == 1 ? "" : "s")}";
                _status.IsVisible = volumes.Count == 0;
            });
        }
        catch (Exception ex)
        {
            _shell.LoggerFactory.CreateLogger<HomeView>().LogError(ex, "Volume enumeration failed");
            _shell.Post(() => _status.Text = "Could not enumerate drives: " + ex.Message);
        }
    }

    private void ScanPath(string path)
    {
        path = path.Trim().Trim('"');
        if (path.Length == 0) return;
        if (!Directory.Exists(path))
        {
            _status.IsVisible = true;
            _status.Text = $"Folder not found: {path}";
            return;
        }
        _ = StartAsync(Path.GetFullPath(path));
    }

    private async Task StartAsync(string path)
    {
        var volume = await _shell.Volumes.GetVolumeForPathAsync(path, CancellationToken.None);
        _shell.Post(() => _shell.StartScan(new ScanTarget(path, volume)));
    }
}

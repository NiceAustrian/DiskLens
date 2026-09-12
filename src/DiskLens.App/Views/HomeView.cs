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

        if (!shell.Elevation.IsElevated && shell.Elevation.CanRelaunchElevated)
            page.Add(BuildElevationBanner());

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

    private Element BuildElevationBanner()
    {
        var card = new Box { Padding = new Thickness(16, 12), CornerRadius = 12 };
        card.Background = null;
        var row = card.Add(new Row { Gap = 14 });
        row.Add(new IconGlyph(Icon.Shield) { FixedWidth = 22, FixedHeight = 22 });
        var text = row.Add(new Column { Gap = 2, Flex = 1, CrossAlign = CrossAlign.Start });
        text.Add(new Label("Faster scans available") { StyleSelector = t => t.BodyMedium });
        text.Add(new Label("As administrator, DiskLens reads the NTFS master file table directly – a whole drive in seconds instead of minutes.") { StyleSelector = t => t.Small });
        var btn = row.Add(new Button("Restart as administrator", Icon.Shield) { Style = ButtonStyle.Primary });
        btn.Activated += () =>
        {
            if (_shell.Elevation.RelaunchElevated([])) _shell.Exit();
        };
        card.Tag = "banner";
        return new BannerFrame(card);
    }

    /// <summary>Accent-tinted frame for the banner card.</summary>
    private sealed class BannerFrame : Element
    {
        public BannerFrame(Element content) => Add(content);

        protected override void OnDraw(SKCanvas canvas)
        {
            var t = Theme;
            var rr = new SKRoundRect(Bounds, 12);
            using var paint = new SKPaint { IsAntialias = true, Color = t.AccentSoft };
            canvas.DrawRoundRect(rr, paint);
            paint.Color = t.Accent.WithAlpha(0x50);
            paint.IsStroke = true;
            canvas.DrawRoundRect(new SKRoundRect(SKRect.Inflate(Bounds, -0.5f, -0.5f), 11.5f), paint);
        }
    }

    private sealed class IconGlyph(Icon icon) : Element
    {
        protected override void OnDraw(SKCanvas canvas) => Icons.Draw(canvas, icon, Bounds.MidX, Bounds.MidY, 22, Theme.Accent, 1.6f);
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

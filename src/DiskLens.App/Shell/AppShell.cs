using DiskLens.App.Views;
using DiskLens.Core.Platform;
using DiskLens.Core.Scanning;
using DiskLens.UI.Animation;
using DiskLens.UI.Elements;
using DiskLens.UI.Hosting;
using DiskLens.UI.Input;
using DiskLens.UI.Layout;
using DiskLens.UI.Rendering;
using DiskLens.UI.Widgets;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace DiskLens.App.Shell;

/// <summary>
/// The application frame: title bar with navigation, a content host that fades between views, and
/// the services views need. Views are plain elements that receive the shell in their constructor.
/// </summary>
public sealed class AppShell
{
    private readonly Stack _contentHost = new();
    private readonly Label _title = new("DiskLens") { StyleSelector = t => t.Title };
    private readonly Label _subtitle = new("") { StyleSelector = t => t.Small, Ellipsis = Ellipsis.Middle };
    private readonly IconButton _back = new(Icon.ArrowLeft, "Back to drives");
    private readonly IconButton _themeToggle = new(Icon.Sun, "Toggle theme");
    private Element? _current;
    private AppWindow? _window;

    public AppShell(
        IVolumeService volumes,
        IScanService scans,
        IElevationService elevation,
        ILoggerFactory loggerFactory,
        UiRoot root)
    {
        Volumes = volumes;
        Scans = scans;
        Elevation = elevation;
        LoggerFactory = loggerFactory;
        Root = root;

        _back.Activated += () => ShowHome();
        _themeToggle.Activated += ToggleTheme;
    }

    public IVolumeService Volumes { get; }
    public IScanService Scans { get; }
    public IElevationService Elevation { get; }
    public ILoggerFactory LoggerFactory { get; }
    public UiRoot Root { get; }

    /// <summary>Runs <paramref name="action"/> on the UI thread.</summary>
    public void Post(Action action) => _window?.Post(action);

    public void Attach(AppWindow window, string? initialPath = null)
    {
        _window = window;
        Root.SetContent(BuildFrame());
        Root.KeyDown += OnGlobalKey;
        ShowHome();
        if (initialPath is not null && Directory.Exists(initialPath))
        {
            var path = Path.GetFullPath(initialPath);
            _ = Volumes.GetVolumeForPathAsync(path, CancellationToken.None).AsTask()
                .ContinueWith(t => Post(() => StartScan(new ScanTarget(path, t.IsCompletedSuccessfully ? t.Result : null))));
        }
    }

    private Element BuildFrame()
    {
        var titleBlock = new Column { Gap = 1, CrossAlign = CrossAlign.Start };
        titleBlock.Add(_title);
        titleBlock.Add(_subtitle);

        var bar = new Row { Gap = 10, Padding = new Thickness(16, 10), FixedHeight = 58 };
        bar.Add(_back);
        bar.Add(titleBlock);
        bar.Add(new Spacer());
        bar.Add(_themeToggle);

        var frame = new Column();
        frame.Add(new TitleBarBackground { FixedHeight = 58, Content = bar });
        _contentHost.Flex = 1;
        _contentHost.ClipsChildren = true;
        frame.Add(_contentHost);
        return frame;
    }

    public void ShowHome()
    {
        _back.IsVisible = false;
        SetTitle("DiskLens", "Pick a drive or folder to analyse");
        Navigate(new HomeView(this));
    }

    public async void StartScan(ScanTarget target)
    {
        try
        {
            var session = await Scans.StartAsync(target);
            Post(() =>
            {
                _back.IsVisible = true;
                SetTitle(target.DisplayName, target.Path);
                Navigate(new ScanView(this, session));
            });
        }
        catch (Exception ex)
        {
            LoggerFactory.CreateLogger<AppShell>().LogError(ex, "Could not start scan of {Path}", target.Path);
            Post(() => SetTitle("DiskLens", $"Could not scan {target.Path}: {ex.Message}"));
        }
    }

    public void SetTitle(string title, string subtitle)
    {
        _title.Text = title;
        _subtitle.Text = subtitle;
    }

    private void Navigate(Element view)
    {
        var old = _current;
        _current = view;
        view.Opacity = 0;
        _contentHost.Add(view);

        var fade = new Tween(0);
        fade.Completed += () => { if (old is not null) _contentHost.Remove(old); };
        Root.Animator.Run(new FadeIn(fade, view, old));
        fade.To(1, 0.22f, Easing.OutCubic);
        Root.Animator.Run(fade);
    }

    private void ToggleTheme()
    {
        var dark = !Root.Theme.IsDark;
        Root.SetTheme(dark ? Theme.Dark : Theme.Light);
        _themeToggle.Icon = dark ? Icon.Sun : Icon.Moon;
    }

    private void OnGlobalKey(KeyEvent e)
    {
        if (e.Handled) return;
        if (e.Key == Key.Escape && _back.IsVisible) { ShowHome(); e.Handled = true; }
    }

    /// <summary>Applies a tween to the incoming view's opacity (and fades the outgoing one).</summary>
    private sealed class FadeIn(Tween t, Element incoming, Element? outgoing) : IAnimation
    {
        public bool Tick(float dt)
        {
            incoming.Opacity = t.Value;
            if (outgoing is not null) outgoing.Opacity = 1 - t.Value;
            incoming.Invalidate();
            return t.IsActive;
        }
    }

    /// <summary>Title bar chrome: subtle gradient and a hairline at the bottom.</summary>
    private sealed class TitleBarBackground : Element
    {
        public required Element Content { init => Add(value); }

        protected override void OnDraw(SKCanvas canvas)
        {
            using var paint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, Bounds.Top), new SKPoint(0, Bounds.Bottom),
                    [Theme.Surface, Theme.Background], SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(Bounds, paint);
            using var line = new SKPaint { Color = Theme.Border };
            canvas.DrawRect(Bounds.Left, Bounds.Bottom - 1, Bounds.Width, 1, line);
        }
    }
}

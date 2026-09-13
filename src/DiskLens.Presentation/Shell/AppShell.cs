using DiskLens.Presentation.Views;
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

namespace DiskLens.Presentation.Shell;

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
    private readonly Row _bar = new() { Gap = 10, Padding = new Thickness(12, 0, 0, 0), FixedHeight = TitleBarHeight };
    private Element? _current;
    private IAppHost? _window;
    private ScanSession? _session;
    private CaptionButtons? _captionButtons;

    private const float TitleBarHeight = 54;

    public AppShell(
        IVolumeService volumes,
        IScanService scans,
        IElevationService elevation,
        IFileOperations files,
        INativeContextMenu nativeMenu,
        ILoggerFactory loggerFactory,
        UiRoot root)
    {
        Volumes = volumes;
        Scans = scans;
        Elevation = elevation;
        Files = files;
        NativeMenu = nativeMenu;
        LoggerFactory = loggerFactory;
        Root = root;

        _back.Activated += () => ShowHome();
        _themeToggle.Activated += ToggleTheme;
        _themeToggle.Icon = root.Theme.IsDark ? Icon.Sun : Icon.Moon;
    }

    public IVolumeService Volumes { get; }
    public IScanService Scans { get; }
    public IElevationService Elevation { get; }
    public IFileOperations Files { get; }
    public INativeContextMenu NativeMenu { get; }

    /// <summary>
    /// Shows the platform file menu for <paramref name="path"/> at a logical window point, with our own
    /// items on top. Blocks while open; returns the chosen custom item index or -1.
    /// </summary>
    public int ShowNativeMenu(string path, SKPoint at, IReadOnlyList<NativeMenuItem> customItems)
    {
        if (_window is null || !NativeMenu.IsSupported) return -1;
        var window = new NativeWindow(_window.NativeHandle, f => _window.Chrome.AddMessageFilter((h, m, w, l) => f(h, m, w, l)));
        var chosen = NativeMenu.Show(path, window, (int)(at.X * _window.Scale), (int)(at.Y * _window.Scale), customItems);
        Root.RequestRedraw();
        return chosen;
    }
    public ILoggerFactory LoggerFactory { get; }
    public UiRoot Root { get; }

    /// <summary>Runs <paramref name="action"/> on the UI thread.</summary>
    public void Post(Action action) => _window?.Post(action);

    /// <summary>The host this shell is attached to (null before <see cref="Attach"/>).</summary>
    public IAppHost? Host => _window;

    /// <summary>Touch hosts get larger targets and different gestures.</summary>
    public bool IsTouch => _window?.IsTouch ?? false;

    /// <summary>Whether Escape/Back has something to do (close a popup, leave the scan view). Safe to read from any thread.</summary>
    public bool CanGoBack => _back.IsVisible || Root.Overlay.Children.Count > 0;

    public void Exit() => _window?.Close();

    public void Attach(IAppHost window, string? initialPath = null)
    {
        _window = window;
        Root.SetContent(BuildFrame());
        Root.KeyDown += OnGlobalKey;
        window.Loaded += OnWindowLoaded;
        ShowHome();
        if (initialPath is not null && Directory.Exists(initialPath))
        {
            var path = Path.GetFullPath(initialPath);
            _ = Volumes.GetVolumeForPathAsync(path, CancellationToken.None).AsTask()
                .ContinueWith(t => Post(() => StartScan(new ScanTarget(path, t.IsCompletedSuccessfully ? t.Result : null))));
        }
    }

    /// <summary>With a custom frame, our title bar becomes the window caption: draggable, with OS buttons.</summary>
    private void OnWindowLoaded()
    {
        var chrome = _window!.Chrome;
        if (!chrome.IsCustomFrame) return;
        _captionButtons = new CaptionButtons(chrome) { FixedHeight = TitleBarHeight };
        _bar.Add(_captionButtons);
        chrome.CaptionHitTest = CaptionHitTest;
        Root.RequestLayout();
    }

    private CaptionHit CaptionHitTest(SKPoint p)
    {
        if (p.Y >= TitleBarHeight) return CaptionHit.Client;
        var el = Root.HitTest(p);
        if (el is CaptionButtons cb) return cb.HitAt(p);
        // Anything interactive (buttons, popups) stays client; passive chrome is draggable caption.
        return el is null or TitleBarBackground or Flex or Label or AppIcon ? CaptionHit.Caption : CaptionHit.Client;
    }

    private Element BuildFrame()
    {
        var titleBlock = new Column { Gap = 1, CrossAlign = CrossAlign.Start, Flex = 1, MainAlign = MainAlign.Center };
        titleBlock.Add(_title);
        titleBlock.Add(_subtitle);

        _back.Margin = new Thickness(0, 0, 0, 0);
        _bar.Add(new AppIcon { FixedWidth = 22, FixedHeight = 22, Margin = new Thickness(4, 0, 0, 0) });
        _bar.Add(_back);
        _bar.Add(titleBlock);
        _bar.Add(_themeToggle);
        _themeToggle.Margin = new Thickness(0, 0, 8, 0);

        var frame = new SafeAreaColumn(Root);
        frame.Add(new TitleBarBackground { FixedHeight = TitleBarHeight, Content = _bar });
        _contentHost.Flex = 1;
        _contentHost.ClipsChildren = true;
        frame.Add(_contentHost);
        return frame;
    }

    public void ShowHome()
    {
        EndCurrentScan();
        _back.IsVisible = false;
        SetTitle("DiskLens", "Pick a drive or folder to analyse");
        Navigate(new HomeView(this));
    }

    /// <summary>Starts a scan and shows it. Fire-and-forget safe: failures are logged and shown, never thrown.</summary>
    public void StartScan(ScanTarget target) => _ = StartScanAsync(target);

    private async Task StartScanAsync(ScanTarget target)
    {
        try
        {
            EndCurrentScan();
            var session = await Scans.StartAsync(target);
            Post(() =>
            {
                _session = session;
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

    /// <summary>A scan nobody is looking at any more should not keep hammering the disk.</summary>
    private void EndCurrentScan()
    {
        var old = _session;
        _session = null;
        if (old is null) return;
        old.Cancel();
        _ = old.Completion.ContinueWith(_ => old.Dispose());
    }

    /// <summary>Shows a dismissable notice above the content; the button copies <paramref name="details"/> to the clipboard.</summary>
    public void ShowNotice(string title, string details, string copyLabel)
    {
        var first = details.Split('\n').Skip(2).FirstOrDefault(l => l.Length > 0) ?? "";
        var box = new Box { Padding = new Thickness(14, 10), CornerRadius = 10, Margin = new Thickness(12, 8, 12, 0) };
        var column = box.Add(new Column { Gap = 8, CrossAlign = CrossAlign.Stretch });
        column.Add(new Label(title) { StyleSelector = t => t.BodyMedium });
        column.Add(new WrappedLabel(TrimTo(details, 600)) { StyleSelector = t => t.MonoSmall });
        var buttons = column.Add(new Row { Gap = 8, MainAlign = MainAlign.End });
        var copy = buttons.Add(new Button(copyLabel, Icon.Copy) { Style = ButtonStyle.Primary });
        var close = buttons.Add(new Button("Dismiss"));
        var frame = new NoticeFrame(box);
        copy.Activated += () => Root.Clipboard?.SetText(details);
        close.Activated += () => _contentHost.Parent?.Remove(frame);
        // Above the content host, below the title bar.
        if (_contentHost.Parent is { } parent) parent.Insert(1, frame);
        _ = first;
    }

    private static string TrimTo(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private sealed class NoticeFrame : Element
    {
        public NoticeFrame(Element content) => Add(content);

        protected override void OnDraw(SKCanvas canvas)
        {
            var t = Theme;
            var r = Children[0].Bounds;
            using var paint = new SKPaint { IsAntialias = true, Color = t.Danger.WithAlpha(0x22) };
            canvas.DrawRoundRect(new SKRoundRect(r, 10), paint);
            paint.Color = t.Danger.WithAlpha(0x60); paint.IsStroke = true;
            canvas.DrawRoundRect(new SKRoundRect(SKRect.Inflate(r, -0.5f, -0.5f), 9.5f), paint);
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
        _window?.Chrome.SetDarkMode(dark);
    }

    /// <summary>The app icon, drawn from the embedded PNG.</summary>
    private sealed class AppIcon : Element
    {
        private static readonly Lazy<SKImage?> Image = new(() => SKImage.FromEncodedData(PresentationAssets.IconPng));

        public AppIcon() => IsHitTestVisible = true;

        protected override void OnDraw(SKCanvas canvas)
        {
            if (Image.Value is not { } img) return;
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(img, Bounds, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }
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

    /// <summary>The outer frame keeps content out from under status/gesture bars on phones.</summary>
    private sealed class SafeAreaColumn(UiRoot root) : Flex(Axis.Vertical)
    {
        protected override void ArrangeContent(SKRect bounds)
        {
            Padding = root.SafeInsets;
            base.ArrangeContent(bounds);
        }

        protected override void OnDraw(SKCanvas canvas)
        {
            // Paint the inset strips in the title bar colour so the status bar blends in.
            if (root.SafeInsets.Top > 0)
            {
                using var paint = new SKPaint { Color = Theme.Surface };
                canvas.DrawRect(Bounds.Left, Bounds.Top, Bounds.Width, root.SafeInsets.Top, paint);
            }
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

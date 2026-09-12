using Android.Content;
using Android.OS;
using Android.Views;
using DiskLens.UI.Elements;
using DiskLens.UI.Hosting;
using DiskLens.UI.Input;
using DiskLens.UI.Widgets;
using SkiaSharp;
using SkiaSharp.Views.Android;

namespace DiskLens.Droid;

/// <summary>
/// Hosts the <see cref="UiRoot"/> in a GL surface. Everything that touches the root runs on the GL
/// thread (input is queued there); the persistent offscreen scene makes partial repaints possible
/// even though the swap chain does not preserve buffers.
/// </summary>
public sealed class DiskLensView : SKGLSurfaceView, IAppHost
{
    private readonly UiRoot _root;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly Queue<Action> _pending = new();
    private readonly Lock _pendingGate = new();
    private readonly float _scale;
    private SKSurface? _scene;
    private GRRecordingContext? _sceneContext;
    private int _sceneW, _sceneH;
    private Java.Lang.Runnable? _wakeup;

    // Touch state (UI thread)
    private float _downX, _downY;
    private bool _moved;
    private Java.Lang.Runnable? _longPress;
    private readonly float _touchSlop;

    public DiskLensView(Context context, UiRoot root) : base(context)
    {
        _root = root;
        _scale = context.Resources?.DisplayMetrics?.Density ?? 1f;
        _touchSlop = ViewConfiguration.Get(context)?.ScaledTouchSlop ?? 16;
        RenderMode = Android.Opengl.Rendermode.WhenDirty;
        Focusable = true;
        FocusableInTouchMode = true;

        Chrome = new NativeChrome(() => { }, () => { }, Close, () => false);
        _root.Clipboard = new AndroidClipboard(context);
        PaintSurface += OnPaintSurface;
    }

    // IAppHost ----------------------------------------------------------------------------------
    public float Scale => _scale;
    public IWindowChrome Chrome { get; }
    public nint NativeHandle => 0;
    public bool IsTouch => true;
    public event Action? Loaded;

    /// <summary>Runs on the GL thread before the next frame (hides View.Post, which targets the UI thread).</summary>
    public new void Post(Action action)
    {
        lock (_pendingGate) _pending.Enqueue(action);
        RequestRender();
    }

    public void Close() => (Context as Activity)?.Finish();

    // Rendering (GL thread) -------------------------------------------------------------------
    private void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        var ctx = e.Surface.Context;
        var w = e.Info.Width;
        var h = e.Info.Height;
        var fresh = false;
        if (_scene is null || _sceneContext != ctx || _sceneW != w || _sceneH != h)
        {
            _scene?.Dispose();
            _scene = SKSurface.Create(ctx, budgeted: true, new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
            _sceneContext = ctx;
            _sceneW = w; _sceneH = h;
            _root.Resize(new SKSize(w / _scale, h / _scale), _scale);
            if (!_loadedRaised) { _loadedRaised = true; Loaded?.Invoke(); }
            fresh = true;
        }

        while (true)
        {
            Action? action;
            lock (_pendingGate) { if (!_pending.TryDequeue(out action)) break; }
            try { action(); }
            catch (Exception ex) { Android.Util.Log.Error("DiskLens", "Posted action failed: " + ex); }
        }

        if (_scene is null) return;
        if (_root.Tick() || fresh)
        {
            var c = _scene.Canvas;
            c.Save();
            c.Scale(_scale);
            _root.Render(c);
            c.Restore();
        }
        _scene.Draw(e.Surface.Canvas, 0, 0, null);

        if (_root.NeedsRedraw) RequestRender();
        else if (_root.NextWakeupIn is { } seconds) ArmWakeup(seconds);
    }

    private bool _loadedRaised;

    private void ArmWakeup(double seconds)
    {
        if (_wakeup is not null) _handler.RemoveCallbacks(_wakeup);
        _wakeup ??= new Java.Lang.Runnable(() => { _root.RequestRedraw(); RequestRender(); });
        _handler.PostDelayed(_wakeup, (long)Math.Max(1, seconds * 1000));
    }

    // Input (UI thread → GL thread) -----------------------------------------------------------
    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null) return false;
        var p = new SKPoint(e.GetX() / _scale, e.GetY() / _scale);
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _downX = e.GetX(); _downY = e.GetY();
                _moved = false;
                RunOnGl(() => _root.DispatchPointerDown(p, PointerButton.Left, Modifiers.None, isTouch: true));
                _longPress ??= new Java.Lang.Runnable(OnLongPress);
                _handler.PostDelayed(_longPress, ViewConfiguration.LongPressTimeout);
                return true;
            case MotionEventActions.Move:
                if (!_moved && (Math.Abs(e.GetX() - _downX) > _touchSlop || Math.Abs(e.GetY() - _downY) > _touchSlop))
                {
                    _moved = true;
                    if (_longPress is not null) _handler.RemoveCallbacks(_longPress);
                }
                RunOnGl(() => _root.DispatchPointerMove(p, Modifiers.None));
                return true;
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                if (_longPress is not null) _handler.RemoveCallbacks(_longPress);
                var cancel = e.ActionMasked == MotionEventActions.Cancel;
                RunOnGl(() => _root.DispatchPointerUp(p, PointerButton.Left, Modifiers.None, cancelled: cancel));
                RunOnGl(() => _root.DispatchPointerLeave());
                return true;
        }
        return base.OnTouchEvent(e);
    }

    private void OnLongPress()
    {
        if (_moved) return;
        PerformHapticFeedback(FeedbackConstants.LongPress);
        var p = new SKPoint(_downX / _scale, _downY / _scale);
        RunOnGl(() => _root.DispatchLongPress(p));
    }

    /// <summary>Sends a key to the root on the GL thread. Returns whether the shell will handle it (best effort).</summary>
    public bool DispatchKeySync(Key key)
    {
        RunOnGl(() => _root.DispatchKeyDown(key, Modifiers.None, false));
        return true;
    }

    private void RunOnGl(Action a)
    {
        QueueEvent(new Java.Lang.Runnable(a));
        RequestRender();
    }

    private sealed class AndroidClipboard(Context context) : IClipboard
    {
        private ClipboardManager? Manager => context.GetSystemService(Context.ClipboardService) as ClipboardManager;
        public string? GetText() => Manager?.PrimaryClip?.GetItemAt(0)?.Text;
        public void SetText(string text) { if (Manager is { } m) m.PrimaryClip = ClipData.NewPlainText("DiskLens", text); }
    }
}

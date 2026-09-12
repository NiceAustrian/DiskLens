using System.Numerics;
using System.Runtime.InteropServices;
using DiskLens.UI.Elements;
using DiskLens.UI.Input;
using DiskLens.UI.Rendering;
using Microsoft.Extensions.Logging;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SkiaSharp;
using Key = DiskLens.UI.Input.Key;
using SilkKey = Silk.NET.Input.Key;

namespace DiskLens.UI.Hosting;

public sealed record WindowConfig(string Title, int Width = 1280, int Height = 800, int MinWidth = 720, int MinHeight = 480);

/// <summary>
/// Hosts a <see cref="UiRoot"/> in a native window: GLFW window + OpenGL context via Silk.NET, Skia
/// rendering straight into the default framebuffer, and input translation. Idle frames are skipped
/// so the app sits at ~0 % CPU when nothing moves.
/// </summary>
public sealed class AppWindow : IDisposable
{
    private readonly WindowConfig _config;
    private readonly ILogger _logger;
    private readonly Queue<Action> _pending = new();
    private readonly Lock _pendingGate = new();

    private IWindow? _window;
    private GL? _gl;
    private IInputContext? _input;
    private GRContext? _grContext;
    private GRBackendRenderTarget? _renderTarget;
    private SKSurface? _surface;
    private float _scale = 1;
    private Vector2D<int> _framebufferSize;
    private Modifiers _modifiers;
    private bool _surfaceDirty = true;

    public AppWindow(WindowConfig config, UiRoot root, ILogger logger)
    {
        _config = config;
        Root = root;
        _logger = logger;
        root.CursorChanged += SetCursor;
    }

    public UiRoot Root { get; }
    public float Scale => _scale;
    public int ThreadId { get; private set; }

    /// <summary>Queues work for the UI thread and wakes the loop.</summary>
    public void Post(Action action)
    {
        lock (_pendingGate) _pending.Enqueue(action);
        Root.RequestRedraw();
    }

    public void SetTitle(string title)
    {
        if (_window is not null) _window.Title = title;
    }

    public void Run()
    {
        var options = WindowOptions.Default with
        {
            Title = _config.Title,
            Size = new Vector2D<int>(_config.Width, _config.Height),
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3)),
            VSync = true,
            ShouldSwapAutomatically = false,
            IsEventDriven = false,
            PreferredStencilBufferBits = 8,
            PreferredBitDepth = new Vector4D<int>(8, 8, 8, 8),
            Samples = 0,
            WindowBorder = WindowBorder.Resizable,
            TransparentFramebuffer = false,
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.FramebufferResize += OnFramebufferResize;
        _window.Closing += OnClosing;
        _window.Run();
    }

    public void Close() => _window?.Close();

    // Lifecycle -----------------------------------------------------------------------------------
    private void OnLoad()
    {
        ThreadId = Environment.CurrentManagedThreadId;
        var window = _window!;
        _gl = window.CreateOpenGL();
        var glInterface = GRGlInterface.Create(name => window.GLContext!.TryGetProcAddress(name, out var addr) ? addr : IntPtr.Zero);
        _grContext = GRContext.CreateGl(glInterface, new GRContextOptions { AvoidStencilBuffers = false });
        if (_grContext is null) throw new InvalidOperationException("Could not create a Skia GL context.");

        _scale = DetectScale();
        _framebufferSize = window.FramebufferSize;
        window.Size = new Vector2D<int>((int)(_config.Width * _scale), (int)(_config.Height * _scale));
        _logger.LogInformation("Window ready: {W}x{H} @ {Scale:0.00}x, GL {Version}", window.Size.X, window.Size.Y, _scale, _gl.GetStringS(StringName.Version));

        _input = window.CreateInput();
        foreach (var mouse in _input.Mice)
        {
            mouse.MouseMove += (_, pos) => Root.DispatchPointerMove(ToLogical(pos), _modifiers);
            mouse.MouseDown += (_, btn) => Root.DispatchPointerDown(ToLogical(mouse.Position), Map(btn), _modifiers);
            mouse.MouseUp += (_, btn) => Root.DispatchPointerUp(ToLogical(mouse.Position), Map(btn), _modifiers);
            mouse.Scroll += (_, wheel) => Root.DispatchPointerWheel(ToLogical(mouse.Position), new SKPoint(wheel.X, wheel.Y), _modifiers);
        }
        foreach (var kb in _input.Keyboards)
        {
            kb.KeyDown += (_, key, _) => { UpdateModifier(key, true); Root.DispatchKeyDown(Map(key), _modifiers, false); };
            kb.KeyUp += (_, key, _) => UpdateModifier(key, false);
            kb.KeyChar += (_, c) => { if (!char.IsControl(c)) Root.DispatchTextInput(c); };
        }

        if (_input.Keyboards.Count > 0) Root.Clipboard = new KeyboardClipboard(_input.Keyboards[0]);

        Root.Resize(new SKSize(window.Size.X / _scale, window.Size.Y / _scale), _scale);
        _surfaceDirty = true;
    }

    private sealed class KeyboardClipboard(IKeyboard keyboard) : Widgets.IClipboard
    {
        public string? GetText() => keyboard.ClipboardText;
        public void SetText(string text) => keyboard.ClipboardText = text;
    }

    private void OnFramebufferResize(Vector2D<int> size)
    {
        _framebufferSize = size;
        var newScale = DetectScale();
        if (Math.Abs(newScale - _scale) > 0.01f) _scale = newScale;
        Root.Resize(new SKSize(size.X / _scale, size.Y / _scale), _scale);
        _surfaceDirty = true;
    }

    private void OnRender(double _)
    {
        // Pump posted actions first; they usually mark something dirty.
        while (true)
        {
            Action? action;
            lock (_pendingGate) { if (!_pending.TryDequeue(out action)) break; }
            try { action(); }
            catch (Exception ex) { _logger.LogError(ex, "Posted UI action failed"); }
        }

        var redraw = Root.Tick() || _surfaceDirty;
        if (!redraw)
        {
            Thread.Sleep(8);   // idle: don't spin
            return;
        }

        if (_surfaceDirty || _surface is null) RecreateSurface();
        var canvas = _surface!.Canvas;
        canvas.Save();
        canvas.Scale(_scale);
        Root.Render(canvas);
        canvas.Restore();
        canvas.Flush();
        _grContext!.Flush();
        _window!.SwapBuffers();
    }

    private void RecreateSurface()
    {
        _surface?.Dispose();
        _renderTarget?.Dispose();
        var w = Math.Max(1, _framebufferSize.X);
        var h = Math.Max(1, _framebufferSize.Y);
        _gl!.Viewport(0, 0, (uint)w, (uint)h);
        var info = new GRGlFramebufferInfo(0, (uint)InternalFormat.Rgba8);
        _renderTarget = new GRBackendRenderTarget(w, h, 0, 8, info);
        _surface = SKSurface.Create(_grContext, _renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
                   ?? throw new InvalidOperationException("Could not create the Skia surface.");
        _surfaceDirty = false;
    }

    private void OnClosing()
    {
        _surface?.Dispose();
        _renderTarget?.Dispose();
        _grContext?.Dispose();
        _input?.Dispose();
        _gl?.Dispose();
        _surface = null; _renderTarget = null; _grContext = null; _input = null; _gl = null;
    }

    public void Dispose()
    {
        _window?.Dispose();
    }

    // Helpers -------------------------------------------------------------------------------------
    private SKPoint ToLogical(Vector2 p) => new(p.X / _scale, p.Y / _scale);

    private float DetectScale()
    {
        // GLFW reports the framebuffer in pixels and the window in screen coordinates; on macOS and
        // Wayland these differ. On Windows GLFW is DPI-aware so both are pixels and we ask the OS.
        var fb = _window!.FramebufferSize;
        var win = _window.Size;
        if (win.X > 0 && fb.X != win.X) return (float)fb.X / win.X;
        if (OperatingSystem.IsWindows() && _window.Native?.Win32 is { } win32)
        {
            var dpi = GetDpiForWindow(win32.Hwnd);
            if (dpi > 0) return dpi / 96f;
        }
        return 1f;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    private void SetCursor(CursorKind kind)
    {
        if (_input is null) return;
        var standard = kind switch
        {
            CursorKind.Hand => StandardCursor.Hand,
            CursorKind.IBeam => StandardCursor.IBeam,
            CursorKind.ResizeHorizontal => StandardCursor.HResize,
            CursorKind.ResizeVertical => StandardCursor.VResize,
            CursorKind.Crosshair => StandardCursor.Crosshair,
            _ => StandardCursor.Arrow,
        };
        foreach (var mouse in _input.Mice)
        {
            mouse.Cursor.Type = CursorType.Standard;
            mouse.Cursor.StandardCursor = standard;
        }
    }

    private void UpdateModifier(SilkKey key, bool down)
    {
        var m = key switch
        {
            SilkKey.ShiftLeft or SilkKey.ShiftRight => Modifiers.Shift,
            SilkKey.ControlLeft or SilkKey.ControlRight => Modifiers.Control,
            SilkKey.AltLeft or SilkKey.AltRight => Modifiers.Alt,
            SilkKey.SuperLeft or SilkKey.SuperRight => Modifiers.Super,
            _ => Modifiers.None,
        };
        if (m == Modifiers.None) return;
        _modifiers = down ? _modifiers | m : _modifiers & ~m;
    }

    private static PointerButton Map(MouseButton b) => b switch
    {
        MouseButton.Left => PointerButton.Left,
        MouseButton.Right => PointerButton.Right,
        MouseButton.Middle => PointerButton.Middle,
        _ => PointerButton.None,
    };

    private static Key Map(SilkKey k) => k switch
    {
        SilkKey.Escape => Key.Escape,
        SilkKey.Enter or SilkKey.KeypadEnter => Key.Enter,
        SilkKey.Space => Key.Space,
        SilkKey.Tab => Key.Tab,
        SilkKey.Backspace => Key.Backspace,
        SilkKey.Delete => Key.Delete,
        SilkKey.Left => Key.Left,
        SilkKey.Right => Key.Right,
        SilkKey.Up => Key.Up,
        SilkKey.Down => Key.Down,
        SilkKey.Home => Key.Home,
        SilkKey.End => Key.End,
        SilkKey.PageUp => Key.PageUp,
        SilkKey.PageDown => Key.PageDown,
        SilkKey.A => Key.A,
        SilkKey.C => Key.C,
        SilkKey.F => Key.F,
        SilkKey.V => Key.V,
        SilkKey.X => Key.X,
        SilkKey.Z => Key.Z,
        SilkKey.Y => Key.Y,
        SilkKey.F5 => Key.F5,
        _ => Key.Unknown,
    };
}

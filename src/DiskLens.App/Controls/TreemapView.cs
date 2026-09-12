using DiskLens.App.Views;
using DiskLens.Core;
using DiskLens.Core.Layout;
using DiskLens.Core.Model;
using DiskLens.UI.Animation;
using DiskLens.UI.Elements;
using DiskLens.UI.Input;
using DiskLens.UI.Rendering;
using SkiaSharp;

namespace DiskLens.App.Controls;

/// <summary>
/// Nested squarified treemap. Directories are drawn as labelled frames, files as shaded tiles
/// coloured by type. Hover highlights, click selects (and reveals in the tree), double-click zooms in,
/// right-click opens the context menu, Backspace zooms out. Layout is recomputed lazily when data, size or zoom change.
/// </summary>
public sealed class TreemapView : Element
{
    private readonly ScanViewModel _vm;
    private readonly List<Item> _items = [];
    private readonly Dictionary<int, int> _itemOf = [];
    private readonly Tween _zoomFade = new(1);
    private readonly SKPaint _fill = new() { IsAntialias = false };
    private readonly SKPaint _stroke = new() { IsAntialias = false, IsStroke = true, StrokeWidth = 1 };
    private readonly SKPaint _ring = new() { IsAntialias = true, IsStroke = true };
    private readonly SKPaint _glow = new() { IsAntialias = true, IsStroke = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3) };
    private SKPicture? _oldPicture;
    private bool _dirty = true;
    private SKRect _layoutBounds;
    private int _hoverItem = -1;

    // Uniform grid over the layout for O(1) hit-testing: each cell lists the items covering it.
    private const int CellSize = 48;
    private List<int>[] _grid = [];
    private int _gridCols, _gridRows;

    private const float MinTile = 2.5f;      // px – smaller tiles are not laid out
    private const float DirHeader = 16f;     // label strip height inside directory frames
    private const float DirPad = 2f;
    private const int MaxItems = 80_000;

    /// <summary>One laid-out rectangle. Colour and label are resolved once here, not per frame.</summary>
    private readonly record struct Item(int Node, SKRect Rect, int Depth, bool IsDir, bool HasHeader, SKColor Color, string? Label);

    public TreemapView(ScanViewModel vm)
    {
        _vm = vm;
        ClipsChildren = true;
        CanFocus = true;
        vm.DataChanged += () => { _dirty = true; Invalidate(); };
        vm.ZoomChanged += _ => BeginZoomTransition();
        vm.SelectionChanged += _ => Invalidate();
        vm.HoverChanged += _ => Invalidate();
    }

    // Layout -------------------------------------------------------------------------------------
    private void EnsureLayout()
    {
        if (!_dirty && _layoutBounds == Bounds) return;
        _dirty = false;
        _layoutBounds = Bounds;
        _items.Clear();
        _itemOf.Clear();
        _hoverItem = -1;

        var root = _vm.ZoomRoot;
        var bounds = new LayoutRect(Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height);
        if (bounds.IsEmpty) return;
        LayoutChildren(root, bounds, 1);
        BuildGrid();
    }

    private void BuildGrid()
    {
        _gridCols = Math.Max(1, (int)MathF.Ceiling(Bounds.Width / CellSize));
        _gridRows = Math.Max(1, (int)MathF.Ceiling(Bounds.Height / CellSize));
        var cells = _gridCols * _gridRows;
        if (_grid.Length != cells) _grid = new List<int>[cells];
        for (var i = 0; i < cells; i++) _grid[i]?.Clear();

        for (var i = 0; i < _items.Count; i++)
        {
            var r = _items[i].Rect;
            var c0 = Math.Clamp((int)((r.Left - Bounds.Left) / CellSize), 0, _gridCols - 1);
            var c1 = Math.Clamp((int)((r.Right - Bounds.Left - 0.01f) / CellSize), 0, _gridCols - 1);
            var r0 = Math.Clamp((int)((r.Top - Bounds.Top) / CellSize), 0, _gridRows - 1);
            var r1 = Math.Clamp((int)((r.Bottom - Bounds.Top - 0.01f) / CellSize), 0, _gridRows - 1);
            for (var row = r0; row <= r1; row++)
                for (var col = c0; col <= c1; col++)
                    (_grid[row * _gridCols + col] ??= []).Add(i);
        }
    }

    private void LayoutChildren(int dir, LayoutRect rect, int depth)
    {
        if (_items.Count >= MaxItems || rect.Width < MinTile || rect.Height < MinTile) return;
        var tree = _vm.Tree;
        var kids = _vm.SortedChildren(dir);
        if (kids.Length == 0) return;

        // Weights: directories by total, files by own size. Trailing zeros are skipped by the layout.
        var count = 0;
        Span<double> weights = kids.Length <= 512 ? stackalloc double[kids.Length] : new double[kids.Length];
        for (var i = 0; i < kids.Length; i++)
        {
            var w = (double)tree.TotalSize(kids[i]);
            if (w <= 0) break;                   // sorted descending: the rest is zero too
            weights[i] = w;
            count++;
        }
        if (count == 0) return;

        var rects = count <= 512 ? stackalloc LayoutRect[count] : new LayoutRect[count];
        SquarifiedTreemap.Layout(weights[..count], rect, rects);

        for (var i = 0; i < count; i++)
        {
            var r = rects[i];
            if (r.Width < MinTile || r.Height < MinTile) continue;
            var node = kids[i];
            var isDir = tree.IsDirectory(node);
            var hasHeader = isDir && r.Width >= 48 && r.Height >= DirHeader + 12;
            var color = isDir ? default : FileColors.ColorOfExtension(tree.ExtensionSpan(node));
            var label = hasHeader || (!isDir && r.Width >= 56 && r.Height >= 30) ? tree.Name(node) : null;
            _itemOf[node] = _items.Count;
            _items.Add(new Item(node, new SKRect(r.X, r.Y, r.Right, r.Bottom), depth, isDir, hasHeader, color, label));
            if (isDir)
            {
                var inner = new LayoutRect(
                    r.X + DirPad, r.Y + (hasHeader ? DirHeader : DirPad),
                    r.Width - 2 * DirPad, r.Height - (hasHeader ? DirHeader + DirPad : 2 * DirPad));
                LayoutChildren(node, inner, depth + 1);
            }
        }
    }

    // Drawing ------------------------------------------------------------------------------------
    protected override void OnDraw(SKCanvas canvas)
    {
        EnsureLayout();
        var t = Theme;

        using var bg = new SKPaint { Color = t.Background };
        canvas.DrawRect(Bounds, bg);

        if (_items.Count == 0)
        {
            var msg = _vm.Session.IsRunning ? "Scanning…" : "Nothing to show";
            TextRender.Draw(canvas, msg, Bounds.MidX, Bounds.MidY, t.BodySecondary, TextAlign.Center);
            return;
        }

        var paint = _fill;
        var stroke = _stroke;
        paint.Shader = null;
        var labelStyle = t.Caption.With(t.IsDark ? t.Text.WithAlpha(0xD0) : t.Text);
        var tree = _vm.Tree;

        foreach (var item in _items)
        {
            var r = item.Rect;
            if (item.IsDir)
            {
                // Frame: slightly lighter with depth so nesting reads.
                var lift = (byte)Math.Min(40, 6 + item.Depth * 5);
                paint.Color = t.IsDark ? new SKColor(255, 255, 255, lift) : new SKColor(0, 0, 0, (byte)(lift / 2));
                canvas.DrawRect(r, paint);
                stroke.Color = t.IsDark ? new SKColor(0, 0, 0, 0x90) : new SKColor(0, 0, 0, 0x30);
                canvas.DrawRect(new SKRect(r.Left + 0.5f, r.Top + 0.5f, r.Right - 0.5f, r.Bottom - 0.5f), stroke);
                if (item.HasHeader)
                {
                    var name = item.Label!;
                    var size = ByteSize.Format(tree.TotalSize(item.Node));
                    var avail = r.Width - 8;
                    var sizeW = labelStyle.Measure(size);
                    var nameW = labelStyle.Measure(name);
                    if (nameW + sizeW + 10 <= avail)
                    {
                        TextRender.Draw(canvas, name, r.Left + 4, r.Top + DirHeader / 2, labelStyle);
                        TextRender.Draw(canvas, size, r.Right - 4, r.Top + DirHeader / 2, labelStyle.With(t.TextSecondary), TextAlign.Right);
                    }
                    else
                    {
                        TextRender.DrawEllipsized(canvas, name, r.Left + 4, r.Top + DirHeader / 2, avail, labelStyle);
                    }
                }
            }
            else
            {
                DrawTile(canvas, paint, stroke, item, tree, t);
            }
        }

        // Selection and hover rings on top of everything.
        DrawRing(canvas, _vm.Selected, t.Accent, 2f);
        var hoverNode = _hoverItem >= 0 ? _items[_hoverItem].Node : _vm.Hovered;
        if (hoverNode != _vm.Selected) DrawRing(canvas, hoverNode, t.Text.WithAlpha(0xC0), 1.5f);

        // Zoom transition: previous picture fades out over the new layout.
        if (_oldPicture is not null && _zoomFade.Value < 1)
        {
            using var fade = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * (1 - _zoomFade.Value))) };
            canvas.SaveLayer(fade);
            canvas.DrawPicture(_oldPicture);
            canvas.Restore();
        }
        else if (_oldPicture is not null)
        {
            _oldPicture.Dispose();
            _oldPicture = null;
        }
    }

    private static void DrawTile(SKCanvas canvas, SKPaint paint, SKPaint stroke, in Item item, FsTree tree, Theme t)
    {
        var r = item.Rect;
        var color = item.Color;
        if (r.Width < 6 || r.Height < 6)
        {
            paint.Shader = null;
            paint.Color = color;
            canvas.DrawRect(r, paint);
            return;
        }

        // "Cushion-lite": diagonal gradient from a lighter top-left to a darker bottom-right, plus an
        // inner highlight line, gives tiles a soft bevel without per-pixel work.
        var light = Mix(color, SKColors.White, 0.22f);
        var dark = Mix(color, SKColors.Black, 0.30f);
        paint.Color = SKColors.White;
        paint.Shader = SKShader.CreateLinearGradient(new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Bottom), [light, color, dark], [0f, 0.45f, 1f], SKShaderTileMode.Clamp);
        canvas.DrawRect(r, paint);
        paint.Shader = null;

        stroke.Color = new SKColor(0, 0, 0, t.IsDark ? (byte)0x70 : (byte)0x40);
        canvas.DrawRect(new SKRect(r.Left + 0.5f, r.Top + 0.5f, r.Right - 0.5f, r.Bottom - 0.5f), stroke);
        if (r.Width >= 12 && r.Height >= 12)
        {
            stroke.Color = new SKColor(255, 255, 255, 0x38);
            canvas.DrawLine(r.Left + 1.5f, r.Top + 1.5f, r.Right - 1.5f, r.Top + 1.5f, stroke);
            canvas.DrawLine(r.Left + 1.5f, r.Top + 1.5f, r.Left + 1.5f, r.Bottom - 1.5f, stroke);
        }

        // Label for big tiles
        if (item.Label is { } name)
        {
            var style = t.Caption.With(new SKColor(0, 0, 0, 0xB0));
            var style2 = t.Caption.With(SKColors.White.WithAlpha(0xF0));
            var cx = r.Left + 6;
            var cy = r.Top + 11;
            var w = r.Width - 12;
            TextRender.DrawEllipsized(canvas, name, cx + 0.5f, cy + 0.5f, w, style);   // faux shadow for contrast
            TextRender.DrawEllipsized(canvas, name, cx, cy, w, style2);
            if (r.Height >= 46)
                TextRender.DrawEllipsized(canvas, ByteSize.Format(tree.Size(item.Node)), cx, cy + 14, w, t.Caption.With(SKColors.White.WithAlpha(0xB0)));
        }
    }

    private void DrawRing(SKCanvas canvas, int node, SKColor color, float width)
    {
        if (node < 0 || !_itemOf.TryGetValue(node, out var idx)) return;
        var r = _items[idx].Rect;
        _ring.StrokeWidth = width; _ring.Color = color;
        canvas.DrawRect(SKRect.Inflate(r, -width / 2, -width / 2), _ring);
        _glow.StrokeWidth = width + 3; _glow.Color = color.WithAlpha(0x40);
        canvas.DrawRect(r, _glow);
    }

    private void BeginZoomTransition()
    {
        // Snapshot the current frame into a picture so we can crossfade to the new layout.
        if (_items.Count > 0 && Bounds.Width > 0)
        {
            using var recorder = new SKPictureRecorder();
            var c = recorder.BeginRecording(Bounds);
            _dirty = false;                  // draw current layout once more into the recorder
            OnDrawInternal(c);
            _oldPicture?.Dispose();
            _oldPicture = recorder.EndRecording();
            _zoomFade.Jump(0);
            Animate(_zoomFade.To(1, 0.28f, Easing.OutCubic));
        }
        _dirty = true;
        Invalidate();
    }

    // Used only to render the pre-zoom snapshot without the transition overlay.
    private void OnDrawInternal(SKCanvas canvas)
    {
        var saved = _oldPicture;
        _oldPicture = null;
        OnDraw(canvas);
        _oldPicture = saved;
    }

    // Input --------------------------------------------------------------------------------------
    private int HitItem(SKPoint p)
    {
        if (_grid.Length == 0 || !Bounds.Contains(p)) return -1;
        var col = Math.Clamp((int)((p.X - Bounds.Left) / CellSize), 0, _gridCols - 1);
        var row = Math.Clamp((int)((p.Y - Bounds.Top) / CellSize), 0, _gridRows - 1);
        var cell = _grid[row * _gridCols + col];
        if (cell is null) return -1;
        // Items are stored parent-before-children, so the highest index is the deepest.
        var hit = -1;
        foreach (var i in cell)
        {
            if (i > hit && _items[i].Rect.Contains(p)) hit = i;
        }
        return hit;
    }

    protected override void OnPointerMove(PointerEvent e)
    {
        var hit = HitItem(e.Position);
        if (hit != _hoverItem)
        {
            _hoverItem = hit;
            _vm.Hovered = hit >= 0 ? _items[hit].Node : FsTree.None;
            Tooltip = hit >= 0 ? TooltipFor(_items[hit].Node) : null;
            Invalidate();
        }
        base.OnPointerMove(e);
    }

    private string TooltipFor(int node)
    {
        var tree = _vm.Tree;
        var size = ByteSize.Format(tree.TotalSize(node));
        var path = tree.FullPath(node);
        return tree.IsDirectory(node)
            ? $"{path}  —  {size}, {tree.FileCount(node):N0} files"
            : $"{path}  —  {size}";
    }

    protected override void OnPointerExit()
    {
        _hoverItem = -1;
        _vm.Hovered = FsTree.None;
        Tooltip = null;
        base.OnPointerExit();
    }

    protected override void OnClick(PointerEvent e)
    {
        if (e.Button == PointerButton.Right)
        {
            var target = _hoverItem >= 0 ? _items[_hoverItem].Node : _vm.ZoomRoot;
            _vm.Selected = target;
            _vm.RequestContextMenu(target, e.Position);
            e.Handled = true;
            return;
        }
        if (_hoverItem >= 0)
        {
            var node = _items[_hoverItem].Node;
            _vm.Selected = node;
            _vm.Reveal(node);
            Focus();
            e.Handled = true;
        }
        base.OnClick(e);
    }

    protected override void OnDoubleClick(PointerEvent e)
    {
        if (_hoverItem >= 0)
        {
            var node = _items[_hoverItem].Node;
            var tree = _vm.Tree;
            _vm.ZoomRoot = tree.IsDirectory(node) ? node : tree.Parent(node);
            e.Handled = true;
        }
        base.OnDoubleClick(e);
    }

    protected override void OnKeyDown(KeyEvent e)
    {
        if (e.Key == Key.Backspace) { _vm.ZoomOut(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    private static SKColor Mix(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red + (b.Red - a.Red) * t), (byte)(a.Green + (b.Green - a.Green) * t), (byte)(a.Blue + (b.Blue - a.Blue) * t), 255);
}

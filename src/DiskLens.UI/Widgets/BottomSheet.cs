using DiskLens.UI.Animation;
using DiskLens.UI.Elements;
using DiskLens.UI.Input;
using DiskLens.UI.Layout;
using SkiaSharp;

namespace DiskLens.UI.Widgets;

/// <summary>
/// A panel sliding up from the bottom edge over a dimmed backdrop – the phone idiom for secondary
/// content. Tap the backdrop, press Escape/Back or drag the handle down to dismiss.
/// </summary>
public sealed class BottomSheet : Popup
{
    private readonly float _heightFraction;
    private readonly Tween _slide = new(1);     // 1 = fully off-screen, 0 = resting
    private float _dragStartY, _dragOffset;
    private bool _dragging;

    public BottomSheet(float heightFraction = 0.6f)
    {
        _heightFraction = heightFraction;
        DimBackground = true;
        Content = Add(new Box { ClipsChildren = true });
        Animate(_slide.To(0, 0.28f, Easing.OutCubic));
    }

    /// <summary>Put the sheet's content here.</summary>
    public Box Content { get; }

    private const float HandleHeight = 26;

    protected override void ArrangeContent(SKRect bounds)
    {
        var h = bounds.Height * _heightFraction;
        var top = bounds.Bottom - h + (_slide.Value * h) + _dragOffset;
        Content.Arrange(new SKRect(bounds.Left, top + HandleHeight, bounds.Right, bounds.Bottom + _slide.Value * h + _dragOffset));
        _sheet = new SKRect(bounds.Left, top, bounds.Right, bounds.Bottom + h);   // tall enough to cover the slide
    }

    private SKRect _sheet;

    protected override void OnDraw(SKCanvas canvas)
    {
        base.OnDraw(canvas);
        if (_slide.IsActive || _dragging) InvalidateLayout();

        var t = Theme;
        var r = _sheet;
        using var paint = new SKPaint { IsAntialias = true, Color = t.Shadow, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 12) };
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(r.Left, r.Top - 4, r.Right, r.Bottom), t.RadiusLarge), paint);
        paint.MaskFilter = null;
        paint.Color = t.Surface;
        canvas.DrawRoundRect(new SKRoundRect(r, t.RadiusLarge), paint);
        paint.Color = t.BorderStrong;
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(r.MidX - 20, r.Top + 10, r.MidX + 20, r.Top + 14), 2), paint);
    }

    protected internal override void OnPointerDown(PointerEvent e)
    {
        if (_sheet.Contains(e.Position))
        {
            // Drag on the handle strip pulls the sheet down.
            if (e.Position.Y < _sheet.Top + HandleHeight + 8)
            {
                _dragging = true;
                _dragStartY = e.Position.Y;
                Root?.CapturePointer(this);
            }
            e.Handled = true;   // inside the sheet: never dismiss
            return;
        }
        base.OnPointerDown(e);
    }

    protected internal override void OnPointerMove(PointerEvent e)
    {
        if (_dragging)
        {
            _dragOffset = Math.Max(0, e.Position.Y - _dragStartY);
            InvalidateLayout();
            e.Handled = true;
            return;
        }
        base.OnPointerMove(e);
    }

    protected internal override void OnPointerUp(PointerEvent e)
    {
        if (_dragging)
        {
            _dragging = false;
            if (_dragOffset > 80) { Close(); return; }
            _dragOffset = 0;
            InvalidateLayout();
            e.Handled = true;
            return;
        }
        base.OnPointerUp(e);
    }
}

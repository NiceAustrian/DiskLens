using DiskLens.UI.Animation;
using DiskLens.UI.Elements;
using DiskLens.UI.Input;
using SkiaSharp;

namespace DiskLens.UI.Widgets;

/// <summary>
/// Vertical scrolling container with a smooth-scrolling wheel, an overlay scrollbar that fades in
/// on use and drag support on the thumb. The single child is measured with unbounded height.
/// </summary>
public class ScrollView : Element
{
    private readonly Tween _offset = new(0);
    private readonly Tween _barOpacity = new(0);
    private float _contentHeight;
    private float _dragStartY, _dragStartOffset;
    private bool _dragging;
    private bool _barHovered;

    // Touch: finger drag with fling
    private bool _touchDown, _touchDragging;
    private float _touchStartY, _touchStartOffset, _touchLastY;
    private double _touchLastTime;
    private float _touchVelocity;          // logical px / s, positive = content moving up (offset increasing)
    private Fling? _fling;
    private const float TouchSlop = 10;

    public ScrollView()
    {
        ClipsChildren = true;
    }

    public float ScrollOffset => _offset.Value;
    public float ContentHeight => _contentHeight;
    public float MaxOffset => Math.Max(0, _contentHeight - Bounds.Height);
    public float WheelStep { get; set; } = 60;

    /// <summary>Called when the visible range changes; lets virtualised children react.</summary>
    public event Action? Scrolled;

    public void ScrollTo(float offset, bool animated = true)
    {
        offset = Math.Clamp(offset, 0, MaxOffset);
        if (animated) Animate(_offset.To(offset, 0.18f));
        else _offset.Jump(offset);
        Invalidate();
        ShowBar();
        InvalidateLayout();
    }

    public void ScrollIntoView(float top, float bottom)
    {
        var viewTop = _offset.Target;
        var viewBottom = viewTop + Bounds.Height;
        if (top < viewTop) ScrollTo(top);
        else if (bottom > viewBottom) ScrollTo(bottom - Bounds.Height);
    }

    protected override SKSize MeasureContent(SKSize available)
    {
        var child = Children.FirstOrDefault();
        if (child is null) return SKSize.Empty;
        var s = child.Measure(new SKSize(available.Width, float.PositiveInfinity));
        return new SKSize(s.Width, Math.Min(available.Height, s.Height));
    }

    protected override void ArrangeContent(SKRect bounds)
    {
        var child = Children.FirstOrDefault();
        if (child is null) return;
        var s = child.Measure(new SKSize(bounds.Width, float.PositiveInfinity));
        _contentHeight = s.Height;
        if (_offset.Target > MaxOffset) _offset.Jump(MaxOffset);
        var top = bounds.Top - _offset.Value;
        child.Arrange(new SKRect(bounds.Left, top, bounds.Right, top + Math.Max(s.Height, bounds.Height)));
        Scrolled?.Invoke();
    }

    protected override void OnDrawOverlay(SKCanvas canvas)
    {
        // Keep the child positioned while the offset animates.
        if (_offset.IsActive) InvalidateLayout();

        if (MaxOffset <= 0 || _barOpacity.Value <= 0.01f) return;
        var track = TrackRect();
        var thumb = ThumbRect();
        using var paint = new SKPaint { IsAntialias = true };
        var alpha = _barOpacity.Value;
        var thumbAlpha = (byte)((_barHovered || _dragging ? 0xA0 : 0x60) * alpha);
        paint.Color = Theme.Text.WithAlpha(thumbAlpha);
        canvas.DrawRoundRect(new SKRoundRect(thumb, thumb.Width / 2), paint);
        _ = track;
    }

    private SKRect TrackRect() => new(Bounds.Right - 10, Bounds.Top + 2, Bounds.Right - 2, Bounds.Bottom - 2);

    private SKRect ThumbRect()
    {
        var track = TrackRect();
        var visibleFrac = Math.Min(1, Bounds.Height / Math.Max(1, _contentHeight));
        var thumbH = Math.Max(24, track.Height * visibleFrac);
        var scrollFrac = MaxOffset > 0 ? _offset.Value / MaxOffset : 0;
        var y = track.Top + (track.Height - thumbH) * scrollFrac;
        var w = _barHovered || _dragging ? 8 : 5;
        return new SKRect(track.Right - w, y, track.Right, y + thumbH);
    }

    private void ShowBar()
    {
        Animate(_barOpacity.To(1, 0.1f));
    }

    private void HideBarLater()
    {
        if (_barHovered || _dragging) return;
        Animate(_barOpacity.To(0, 0.6f));
    }

    protected internal override void OnWheel(PointerEvent e)
    {
        if (MaxOffset <= 0) { base.OnWheel(e); return; }
        var target = Math.Clamp(_offset.Target - e.ScrollDelta.Y * WheelStep, 0, MaxOffset);
        Animate(_offset.To(target, 0.16f, Easing.OutCubic));
        ShowBar();
        e.Handled = true;
        InvalidateLayout();
    }

    protected internal override void OnPointerMove(PointerEvent e)
    {
        if (_touchDown)
        {
            var now = Environment.TickCount64 / 1000.0;
            if (!_touchDragging && Math.Abs(e.Position.Y - _touchStartY) > TouchSlop && MaxOffset > 0)
            {
                _touchDragging = true;
                Root?.TakeOverPointer(this);
                ShowBar();
            }
            if (_touchDragging)
            {
                var dt = now - _touchLastTime;
                if (dt > 0.001) _touchVelocity = (float)((_touchLastY - e.Position.Y) / dt);
                _touchLastY = e.Position.Y;
                _touchLastTime = now;
                _offset.Jump(Math.Clamp(_touchStartOffset + (_touchStartY - e.Position.Y), 0, MaxOffset));
                InvalidateLayout();
                e.Handled = true;
                return;
            }
        }
        if (_dragging)
        {
            var track = TrackRect();
            var thumbH = ThumbRect().Height;
            var frac = (e.Position.Y - _dragStartY) / Math.Max(1, track.Height - thumbH);
            _offset.Jump(Math.Clamp(_dragStartOffset + frac * MaxOffset, 0, MaxOffset));
            InvalidateLayout();
            e.Handled = true;
            return;
        }
        var overBar = MaxOffset > 0 && TrackRect().Contains(e.Position);
        if (overBar != _barHovered)
        {
            _barHovered = overBar;
            if (overBar) ShowBar(); else HideBarLater();
            Invalidate();
        }
        base.OnPointerMove(e);
    }

    protected internal override void OnPointerDown(PointerEvent e)
    {
        if (e.IsTouch && e.Button == PointerButton.Left)
        {
            // Remember where the finger went down; a drag beyond the slop takes over from the child.
            if (_fling is not null) { Root?.Animator.Stop(_fling); _fling = null; }
            _touchDown = true;
            _touchDragging = false;
            _touchStartY = _touchLastY = e.Position.Y;
            _touchStartOffset = _offset.Target;
            _touchLastTime = Environment.TickCount64 / 1000.0;
            _touchVelocity = 0;
            // do not mark handled: the child gets its press as usual
            base.OnPointerDown(e);
            return;
        }
        if (e.Button == PointerButton.Left && MaxOffset > 0 && TrackRect().Contains(e.Position))
        {
            var thumb = ThumbRect();
            if (!thumb.Contains(e.Position))
            {
                // Page towards the click.
                var page = Bounds.Height * 0.9f;
                ScrollTo(_offset.Target + (e.Position.Y < thumb.Top ? -page : page));
            }
            _dragging = true;
            _dragStartY = e.Position.Y;
            _dragStartOffset = _offset.Target;
            Root?.CapturePointer(this);
            e.Handled = true;
            return;
        }
        base.OnPointerDown(e);
    }

    protected internal override void OnPointerUp(PointerEvent e)
    {
        if (_touchDown)
        {
            _touchDown = false;
            if (_touchDragging)
            {
                _touchDragging = false;
                if (Math.Abs(_touchVelocity) > 50)
                {
                    _fling = new Fling(this, _touchVelocity);
                    Animate(_fling);
                }
                else HideBarLater();
                e.Handled = true;
                return;
            }
        }
        if (_dragging)
        {
            _dragging = false;
            HideBarLater();
            e.Handled = true;
        }
        base.OnPointerUp(e);
    }

    protected internal override void OnPointerExit()
    {
        _barHovered = false;
        HideBarLater();
        base.OnPointerExit();
    }

    /// <summary>Decelerating scroll after a finger lift.</summary>
    private sealed class Fling(ScrollView view, float velocity) : IAnimation
    {
        private float _v = velocity;

        public bool Tick(float dt)
        {
            _v *= MathF.Pow(0.05f, dt);        // ~95 % of the speed gone after one second
            var next = view._offset.Value + _v * dt;
            var clamped = Math.Clamp(next, 0, view.MaxOffset);
            view._offset.Jump(clamped);
            view.InvalidateLayout();
            if (Math.Abs(_v) < 20 || clamped != next)
            {
                view._fling = null;
                view.HideBarLater();
                return false;
            }
            return true;
        }
    }

    public override Element? HitTest(SKPoint p)
    {
        if (!IsVisible || !IsHitTestVisible || !Bounds.Contains(p)) return null;
        // The scrollbar track belongs to us, not the child.
        if (MaxOffset > 0 && TrackRect().Contains(p)) return this;
        return base.HitTest(p);
    }
}

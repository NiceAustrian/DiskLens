using DiskLens.UI.Animation;
using DiskLens.UI.Elements;
using SkiaSharp;

namespace DiskLens.UI.Widgets;

/// <summary>Determinate or indeterminate progress bar. Indeterminate mode shows a sweeping highlight.</summary>
public sealed class ProgressBar : Element
{
    private readonly Tween _value = new(0);
    private float _phase;
    private bool _indeterminate;

    public ProgressBar()
    {
        IsHitTestVisible = false;
    }

    public float Thickness { get; set; } = 6;
    public SKColor? Color { get; set; }
    public SKColor? TrackColor { get; set; }

    /// <summary>0..1. Animated.</summary>
    public float Value
    {
        get => _value.Target;
        set => Animate(_value.To(Math.Clamp(value, 0, 1), 0.3f));
    }

    public bool IsIndeterminate
    {
        get => _indeterminate;
        set
        {
            if (_indeterminate == value) return;
            _indeterminate = value;
            if (value) Animate(new Sweep(this));
            Invalidate();
        }
    }

    protected override SKSize MeasureContent(SKSize available) => new(available.Width, Thickness);

    protected override void OnDraw(SKCanvas canvas)
    {
        var rect = new SKRect(Bounds.Left, Bounds.MidY - Thickness / 2, Bounds.Right, Bounds.MidY + Thickness / 2);
        var rr = new SKRoundRect(rect, Thickness / 2);
        using var paint = new SKPaint { IsAntialias = true, Color = TrackColor ?? Theme.SurfaceActive };
        canvas.DrawRoundRect(rr, paint);

        var color = Color ?? Theme.Accent;
        canvas.Save();
        canvas.ClipRoundRect(rr, antialias: true);
        if (_indeterminate)
        {
            var w = rect.Width * 0.35f;
            var x = rect.Left - w + (rect.Width + w) * _phase;
            paint.Color = SKColors.White;
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(x, 0), new SKPoint(x + w, 0),
                [color.WithAlpha(0), color, color.WithAlpha(0)], SKShaderTileMode.Clamp);
            canvas.DrawRect(rect, paint);
        }
        else
        {
            paint.Color = color;
            canvas.DrawRect(rect.Left, rect.Top, rect.Width * _value.Value, rect.Height, paint);
        }
        canvas.Restore();
    }

    private sealed class Sweep(ProgressBar bar) : IAnimation
    {
        public bool Tick(float dt)
        {
            if (!bar._indeterminate) return false;
            bar._phase = (bar._phase + dt * 0.7f) % 1f;
            bar.Invalidate();
            return true;
        }
    }
}

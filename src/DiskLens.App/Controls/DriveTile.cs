using DiskLens.Core;
using DiskLens.Core.Platform;
using DiskLens.UI.Animation;
using DiskLens.UI.Elements;
using DiskLens.UI.Input;
using DiskLens.UI.Rendering;
using SkiaSharp;

namespace DiskLens.App.Controls;

/// <summary>A drive card: icon, label, file system, usage bar and free space. Lifts on hover.</summary>
public sealed class DriveTile : Element
{
    private readonly VolumeInfo _volume;
    private readonly Tween _hover = new(0);
    private readonly Tween _fill = new(0);
    private bool _fillStarted;

    public DriveTile(VolumeInfo volume)
    {
        _volume = volume;
        Cursor = volume.IsReady ? CursorKind.Hand : CursorKind.Arrow;
        Tooltip = volume.IsReady ? $"Scan {volume.MountPath}" : "Drive not ready";
    }

    protected override void OnDraw(SKCanvas canvas)
    {
        if (!_fillStarted)
        {
            _fillStarted = true;
            Animate(_fill.To((float)_volume.UsedFraction, 0.9f, Easing.OutQuint));
        }

        var t = Theme;
        var hover = _hover.Value;
        var rect = Bounds;
        rect.Offset(0, -2 * hover);
        var rr = new SKRoundRect(rect, t.RadiusLarge);

        using var paint = new SKPaint { IsAntialias = true };

        // Shadow grows with hover
        paint.Color = t.Shadow.WithAlpha((byte)(t.Shadow.Alpha * (0.5f + 0.5f * hover)));
        paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8 + 6 * hover);
        var shadowRect = rect; shadowRect.Offset(0, 4 + 4 * hover);
        canvas.DrawRoundRect(new SKRoundRect(shadowRect, t.RadiusLarge), paint);
        paint.MaskFilter = null;

        // Surface
        paint.Color = t.Surface;
        canvas.DrawRoundRect(rr, paint);
        if (hover > 0)
        {
            paint.Color = t.SurfaceHover.WithAlpha((byte)(t.SurfaceHover.Alpha * hover));
            canvas.DrawRoundRect(rr, paint);
        }
        paint.Color = hover > 0 ? Blend(t.Border, t.Accent.WithAlpha(0x80), hover) : t.Border;
        paint.IsStroke = true; paint.StrokeWidth = 1;
        canvas.DrawRoundRect(new SKRoundRect(SKRect.Inflate(rect, -0.5f, -0.5f), t.RadiusLarge - 0.5f), paint);
        paint.IsStroke = false;

        const float pad = 18;
        var x = rect.Left + pad;
        var y = rect.Top + pad;

        // Icon in a tinted circle
        var accent = _volume.Kind switch
        {
            VolumeKind.Network => t.AccentAlt,
            VolumeKind.Removable => t.Warning,
            _ => t.Accent,
        };
        paint.Color = accent.WithAlpha(0x22);
        canvas.DrawCircle(x + 18, y + 18, 18, paint);
        Icons.Draw(canvas, IconFor(_volume.Kind), x + 18, y + 18, 18, accent, 1.6f);

        // Title + subtitle
        var textX = x + 48;
        var textW = rect.Right - pad - textX;
        var title = _volume.IsReady ? _volume.DisplayName : $"{_volume.Id} (not ready)";
        TextRender.DrawEllipsized(canvas, title, textX, y + 10, textW, t.Title);
        var sub = _volume.IsReady ? $"{_volume.FileSystem}  ·  {_volume.MountPath}" : KindName(_volume.Kind);
        TextRender.DrawEllipsized(canvas, sub, textX, y + 30, textW, t.Small);

        if (!_volume.IsReady) return;

        // Usage bar
        var barY = rect.Bottom - pad - 22;
        var barRect = new SKRect(x, barY, rect.Right - pad, barY + 6);
        paint.Color = t.SurfaceActive;
        canvas.DrawRoundRect(new SKRoundRect(barRect, 3), paint);
        var fillW = barRect.Width * _fill.Value;
        if (fillW > 0)
        {
            var used = _volume.UsedFraction;
            var barColor = used > 0.9 ? t.Danger : used > 0.75 ? t.Warning : accent;
            paint.Color = SKColors.White;   // shader alpha is modulated by the paint colour
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(barRect.Left, 0), new SKPoint(barRect.Right, 0),
                [barColor.WithAlpha(0xC0), barColor], SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(barRect.Left, barRect.Top, barRect.Left + fillW, barRect.Bottom), 3), paint);
            paint.Shader = null;
        }

        // Numbers
        var (usedNum, usedUnit) = ByteSize.FormatParts(_volume.UsedBytes);
        var numStyle = t.MonoSecondary.With(t.Text);
        var unitStyle = t.MonoSmall;
        var ny = rect.Bottom - pad - 2;
        TextRender.DrawBaseline(canvas, usedNum, x, ny, numStyle);
        var nx = x + numStyle.Measure(usedNum) + 3;
        TextRender.DrawBaseline(canvas, usedUnit + " used", nx, ny, unitStyle);
        var free = ByteSize.Format(_volume.FreeBytes) + " free";
        TextRender.DrawBaseline(canvas, free, rect.Right - pad, ny, unitStyle, TextAlign.Right);
    }

    protected override void OnPointerEnter()
    {
        base.OnPointerEnter();
        if (_volume.IsReady) Animate(_hover.To(1, 0.15f));
    }

    protected override void OnPointerExit()
    {
        base.OnPointerExit();
        Animate(_hover.To(0, 0.25f));
    }

    private static Icon IconFor(VolumeKind kind) => kind switch
    {
        VolumeKind.Network => Icon.Network,
        VolumeKind.Removable => Icon.Usb,
        VolumeKind.Optical => Icon.Disc,
        _ => Icon.Drive,
    };

    private static string KindName(VolumeKind kind) => kind switch
    {
        VolumeKind.Network => "Network drive",
        VolumeKind.Removable => "Removable drive",
        VolumeKind.Optical => "Optical drive",
        VolumeKind.Ram => "RAM disk",
        _ => "Drive",
    };

    private static SKColor Blend(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red + (b.Red - a.Red) * t), (byte)(a.Green + (b.Green - a.Green) * t),
        (byte)(a.Blue + (b.Blue - a.Blue) * t), (byte)(a.Alpha + (b.Alpha - a.Alpha) * t));
}

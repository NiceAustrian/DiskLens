using DiskLens.UI.Elements;
using DiskLens.UI.Input;
using SkiaSharp;

namespace DiskLens.UI.Widgets;

/// <summary>
/// A tall element of uniform rows that only draws the rows inside the canvas clip. Put it inside a
/// <see cref="ScrollView"/>. Subclasses render rows and react to row-level input; nothing is
/// allocated per row, so a million rows cost nothing until they scroll into view.
/// </summary>
public abstract class VirtualRows : Element
{
    private int _rowCount;
    private int _hoverRow = -1;

    public float RowHeight { get; set; } = 28;

    public int RowCount
    {
        get => _rowCount;
        set
        {
            if (_rowCount == value) return;
            _rowCount = value;
            InvalidateLayout();
        }
    }

    public int HoverRow => _hoverRow;
    public int SelectedRow { get; set; } = -1;

    public SKRect RowRect(int index) => new(Bounds.Left, Bounds.Top + index * RowHeight, Bounds.Right, Bounds.Top + (index + 1) * RowHeight);

    public int RowAt(float y)
    {
        var i = (int)MathF.Floor((y - Bounds.Top) / RowHeight);
        return i >= 0 && i < _rowCount ? i : -1;
    }

    protected ScrollView? Scroller => Parent as ScrollView;

    public void EnsureRowVisible(int index)
    {
        if (index < 0 || Scroller is null) return;
        var top = index * RowHeight;
        Scroller.ScrollIntoView(top, top + RowHeight);
    }

    protected override SKSize MeasureContent(SKSize available) => new(available.Width, _rowCount * RowHeight);

    protected override void OnDraw(SKCanvas canvas)
    {
        if (_rowCount == 0) return;
        var clip = canvas.LocalClipBounds;
        var first = Math.Max(0, (int)MathF.Floor((clip.Top - Bounds.Top) / RowHeight));
        var last = Math.Min(_rowCount - 1, (int)MathF.Ceiling((clip.Bottom - Bounds.Top) / RowHeight));
        for (var i = first; i <= last; i++)
        {
            DrawRow(canvas, i, RowRect(i), i == _hoverRow, i == SelectedRow);
        }
    }

    protected abstract void DrawRow(SKCanvas canvas, int index, SKRect rect, bool hovered, bool selected);

    protected virtual void OnRowClick(int index, PointerEvent e) { }
    protected virtual void OnRowDoubleClick(int index, PointerEvent e) { }
    protected virtual void OnRowHoverChanged(int index) { }

    protected internal override void OnPointerMove(PointerEvent e)
    {
        SetHover(RowAt(e.Position.Y));
        base.OnPointerMove(e);
    }

    protected internal override void OnPointerExit()
    {
        SetHover(-1);
        base.OnPointerExit();
    }

    protected internal override void OnClick(PointerEvent e)
    {
        var row = RowAt(e.Position.Y);
        if (row >= 0) OnRowClick(row, e);
        base.OnClick(e);
    }

    protected internal override void OnDoubleClick(PointerEvent e)
    {
        var row = RowAt(e.Position.Y);
        if (row >= 0) OnRowDoubleClick(row, e);
        base.OnDoubleClick(e);
    }

    private void SetHover(int row)
    {
        if (row == _hoverRow) return;
        _hoverRow = row;
        OnRowHoverChanged(row);
        Invalidate();
    }
}

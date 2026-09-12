using DiskLens.App.Views;
using DiskLens.Core;
using DiskLens.Core.Model;
using DiskLens.UI.Elements;
using DiskLens.UI.Input;
using DiskLens.UI.Layout;
using DiskLens.UI.Rendering;
using DiskLens.UI.Widgets;
using SkiaSharp;

namespace DiskLens.App.Controls;

/// <summary>
/// TreeSize-style directory tree: a virtualised flat list of the expanded nodes with size, share
/// bar, item count and date columns. Children are sorted by size, largest first.
/// </summary>
public sealed class TreeList : Element
{
    private readonly ScanViewModel _vm;
    private readonly Rows _rows;
    private readonly ScrollView _scroll;
    private readonly Header _header;

    // Column widths (right-aligned block); the name column takes the rest.
    private const float ColSize = 92, ColBar = 96, ColItems = 72, ColDate = 92;
    private const float RowH = 26;
    private const float Indent = 16;

    public TreeList(ScanViewModel vm)
    {
        _vm = vm;
        CanFocus = true;

        var column = Add(new Column());
        _header = column.Add(new Header(this) { FixedHeight = 28 });
        _scroll = column.Add(new ScrollView { Flex = 1 });
        _rows = _scroll.Add(new Rows(this) { RowHeight = RowH });

        vm.DataChanged += OnDataChanged;
        vm.SelectionChanged += n => { _rows.SelectedRow = _rows.IndexOf(n); Invalidate(); };
        vm.RevealRequested += Reveal;
        vm.HoverChanged += _ => Invalidate();

        _rows.Expand(FsTree.Root);
        _rows.Rebuild();
    }

    private void OnDataChanged() => _rows.Rebuild();

    private void Reveal(int node)
    {
        for (var n = _vm.Tree.Parent(node); n != FsTree.None; n = _vm.Tree.Parent(n)) _rows.Expand(n);
        _rows.Rebuild();
        var row = _rows.IndexOf(node);
        _rows.SelectedRow = row;
        if (row >= 0) _rows.EnsureRowVisible(row);
        Focus();
    }

    protected override void OnKeyDown(KeyEvent e)
    {
        var sel = _rows.SelectedRow;
        var tree = _vm.Tree;
        switch (e.Key)
        {
            case Key.Down: Select(Math.Min(_rows.RowCount - 1, sel + 1)); break;
            case Key.Up: Select(Math.Max(0, sel - 1)); break;
            case Key.Home: Select(0); break;
            case Key.End: Select(_rows.RowCount - 1); break;
            case Key.PageDown: Select(Math.Min(_rows.RowCount - 1, sel + (int)(_scroll.Height / RowH) - 1)); break;
            case Key.PageUp: Select(Math.Max(0, sel - (int)(_scroll.Height / RowH) + 1)); break;
            case Key.Right when sel >= 0:
                var n = _rows[sel];
                if (tree.IsDirectory(n) && !_rows.IsExpanded(n)) { _rows.Expand(n); _rows.Rebuild(); }
                else if (tree.IsDirectory(n) && sel + 1 < _rows.RowCount) Select(sel + 1);
                break;
            case Key.Left when sel >= 0:
                var m = _rows[sel];
                if (tree.IsDirectory(m) && _rows.IsExpanded(m)) { _rows.Collapse(m); _rows.Rebuild(); }
                else if (tree.Parent(m) != FsTree.None) Select(_rows.IndexOf(tree.Parent(m)));
                break;
            case Key.Enter when sel >= 0:
                var z = _rows[sel];
                _vm.ZoomRoot = tree.IsDirectory(z) ? z : tree.Parent(z);
                break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    private void Select(int row)
    {
        if (row < 0 || row >= _rows.RowCount) return;
        _rows.SelectedRow = row;
        _vm.Selected = _rows[row];
        _rows.EnsureRowVisible(row);
    }

    // ---------------------------------------------------------------------------------------------
    private sealed class Header(TreeList owner) : Element
    {
        protected override void OnDraw(SKCanvas canvas)
        {
            var t = Theme;
            using var bg = new SKPaint { Color = t.Surface };
            canvas.DrawRect(Bounds, bg);
            using var line = new SKPaint { Color = t.Border };
            canvas.DrawRect(Bounds.Left, Bounds.Bottom - 1, Bounds.Width, 1, line);

            var style = t.Caption;
            var y = Bounds.MidY;
            var right = Bounds.Right - 12;
            TextRender.Draw(canvas, "Name", Bounds.Left + 12, y, style);
            TextRender.Draw(canvas, "Modified", right, y, style, TextAlign.Right); right -= ColDate;
            TextRender.Draw(canvas, "Items", right, y, style, TextAlign.Right); right -= ColItems;
            TextRender.Draw(canvas, "Share", right, y, style, TextAlign.Right); right -= ColBar;
            TextRender.Draw(canvas, "Size", right, y, style, TextAlign.Right);
            _ = owner;
        }
    }

    // ---------------------------------------------------------------------------------------------
    private sealed class Rows(TreeList owner) : VirtualRows
    {
        private readonly List<int> _flat = [];
        private readonly Dictionary<int, int> _rowOf = [];
        private readonly HashSet<int> _expanded = [];
        private readonly SKPaint _paint = new() { IsAntialias = true };

        private ScanViewModel Vm => owner._vm;
        private FsTree Tree => owner._vm.Tree;

        public int this[int row] => _flat[row];
        public int IndexOf(int node) => _rowOf.TryGetValue(node, out var r) ? r : -1;
        public bool IsExpanded(int node) => _expanded.Contains(node);
        public void Expand(int node) => _expanded.Add(node);
        public void Collapse(int node) => _expanded.Remove(node);

        public void Rebuild()
        {
            var selectedNode = SelectedRow >= 0 && SelectedRow < _flat.Count ? _flat[SelectedRow] : Vm.Selected;
            _flat.Clear();
            _rowOf.Clear();
            var stack = new Stack<int>();
            stack.Push(FsTree.Root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                _rowOf[n] = _flat.Count;
                _flat.Add(n);
                if (_expanded.Contains(n))
                {
                    var kids = Vm.SortedChildren(n);
                    for (var i = kids.Length - 1; i >= 0; i--) stack.Push(kids[i]);
                }
            }
            RowCount = _flat.Count;
            SelectedRow = IndexOf(selectedNode);
            Invalidate();
        }

        protected override void DrawRow(SKCanvas canvas, int index, SKRect rect, bool hovered, bool selected)
        {
            var t = Theme;
            var tree = Tree;
            var node = _flat[index];
            var isDir = tree.IsDirectory(node);
            var depth = tree.Depth(node);
            var isZoomRoot = node == Vm.ZoomRoot;
            var isVmHover = node == Vm.Hovered && !hovered;

            var paint = _paint;
            if (selected)
            {
                paint.Color = t.Selection;
                canvas.DrawRoundRect(new SKRoundRect(SKRect.Inflate(rect, -4, -1), t.RadiusSmall), paint);
            }
            else if (hovered || isVmHover)
            {
                paint.Color = t.SurfaceHover;
                canvas.DrawRoundRect(new SKRoundRect(SKRect.Inflate(rect, -4, -1), t.RadiusSmall), paint);
            }

            var x = rect.Left + 12 + depth * Indent;
            var cy = rect.MidY;

            // Chevron
            if (isDir && tree.FirstChild(node) != FsTree.None)
            {
                Icons.Draw(canvas, _expanded.Contains(node) ? Icon.ChevronDown : Icon.ChevronRight, x + 6, cy, 12, t.TextMuted, 1.6f);
            }
            x += 16;

            // Icon
            Span<char> nameBuffer = stackalloc char[FsTree.MaxNameChars];
            var ext = tree.ExtensionSpan(node, nameBuffer);
            var category = isDir ? FileCategory.Directory : FileColors.Categorize(ext);
            var iconColor = isDir ? (isZoomRoot ? t.Accent : t.TextSecondary) : FileColors.ColorOfExtension(ext);
            Icons.Draw(canvas, isDir ? (_expanded.Contains(node) ? Icon.FolderOpen : Icon.Folder) : Icon.File, x + 8, cy, 15, iconColor, 1.5f);
            x += 24;

            // Right-aligned columns, measured from the right edge
            var right = rect.Right - 12;
            var mono = t.MonoSecondary;

            if (tree.ModifiedTicks(node) > 0)
                TextRender.Draw(canvas, tree.Modified(node).ToLocalTime().ToString("yyyy-MM-dd"), right, cy, t.MonoSmall, TextAlign.Right);
            right -= ColDate;

            var items = isDir ? tree.FileCount(node) + tree.DirCount(node) : 0;
            if (isDir) TextRender.Draw(canvas, items.ToString("N0"), right, cy, t.MonoSmall, TextAlign.Right);
            right -= ColItems;

            // Share bar relative to the parent
            var parent = tree.Parent(node);
            var parentTotal = parent == FsTree.None ? tree.TotalSize(node) : tree.TotalSize(parent);
            var share = parentTotal > 0 ? (float)tree.TotalSize(node) / parentTotal : 0;
            var barRect = new SKRect(right - ColBar + 12, cy - 4, right - 8, cy + 4);
            paint.Color = t.SurfaceActive;
            canvas.DrawRoundRect(new SKRoundRect(barRect, 2), paint);
            if (share > 0)
            {
                paint.Color = isDir ? t.Accent.WithAlpha(0xB0) : FileColors.ColorOf(category).WithAlpha(0xB0);
                var w = Math.Max(2, barRect.Width * share);
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(barRect.Left, barRect.Top, barRect.Left + w, barRect.Bottom), 2), paint);
            }
            right -= ColBar;

            TextRender.Draw(canvas, ByteSize.Format(tree.TotalSize(node)), right, cy, mono.With(t.Text), TextAlign.Right);
            right -= ColSize;

            // Name fills the remaining width
            var nameStyle = isDir ? t.BodyMedium : t.Body;
            if (tree.HasFlag(node, NodeFlags.AccessDenied)) nameStyle = nameStyle.With(t.Warning);
            else if (tree.HasFlag(node, NodeFlags.ReparsePoint)) nameStyle = nameStyle.With(t.TextMuted);
            var name = node == FsTree.Root ? tree.Name(node) : tree.Name(node);
            TextRender.DrawEllipsized(canvas, name, x, cy, right - x - 8, nameStyle);
            if (tree.HasFlag(node, NodeFlags.AccessDenied))
                Icons.Draw(canvas, Icon.Warning, x + Math.Min(nameStyle.Measure(name), right - x - 8) + 12, cy, 12, t.Warning, 1.5f);
        }

        protected override void OnRowClick(int index, PointerEvent e)
        {
            var node = _flat[index];
            if (e.Button == PointerButton.Right)
            {
                SelectedRow = index;
                Vm.Selected = node;
                Vm.RequestContextMenu(node, e.Position, native: (e.Modifiers & Modifiers.Shift) != 0);
                e.Handled = true;
                return;
            }
            var depth = Tree.Depth(node);
            var chevronX = Bounds.Left + 12 + depth * Indent;
            if (e.Button == PointerButton.Left && Tree.IsDirectory(node) && e.Position.X >= chevronX - 2 && e.Position.X < chevronX + 16)
            {
                Toggle(node);
                e.Handled = true;
                return;
            }
            SelectedRow = index;
            Vm.Selected = node;
            owner.Focus();
        }

        protected override void OnRowDoubleClick(int index, PointerEvent e)
        {
            var node = _flat[index];
            if (Tree.IsDirectory(node))
            {
                Toggle(node);
                Vm.ZoomRoot = node;
            }
            e.Handled = true;
        }

        protected override void OnRowHoverChanged(int index)
        {
            Vm.Hovered = index >= 0 ? _flat[index] : FsTree.None;
        }

        private void Toggle(int node)
        {
            if (!_expanded.Remove(node)) _expanded.Add(node);
            Rebuild();
        }
    }
}

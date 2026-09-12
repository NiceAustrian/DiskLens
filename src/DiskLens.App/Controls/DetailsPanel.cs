using DiskLens.App.Views;
using DiskLens.Core;
using DiskLens.Core.Model;
using DiskLens.Core.Stats;
using DiskLens.UI.Animation;
using DiskLens.UI.Elements;
using DiskLens.UI.Input;
using DiskLens.UI.Rendering;
using DiskLens.UI.Widgets;
using SkiaSharp;

namespace DiskLens.App.Controls;

/// <summary>
/// Details for the selected node: headline numbers, a by-type breakdown and the largest files.
/// Statistics are computed off the UI thread and debounced while the tree is still growing.
/// </summary>
public sealed class DetailsPanel : Element
{
    private readonly ScanViewModel _vm;
    private readonly Action<Action> _post;
    private readonly Body _body;
    private readonly ScrollView _scroll;
    private CancellationTokenSource? _statsCts;
    private int _statsNode = FsTree.None;
    private int _version;

    public DetailsPanel(ScanViewModel vm, Action<Action> post)
    {
        _vm = vm;
        _post = post;
        _scroll = Add(new ScrollView());
        _body = _scroll.Add(new Body(this));

        vm.SelectionChanged += _ => ScheduleStats();
        vm.DataChanged += ScheduleStats;
        ScheduleStats();
    }

    private void ScheduleStats()
    {
        _statsCts?.Cancel();
        var cts = _statsCts = new CancellationTokenSource();
        var node = _vm.Selected;
        var tree = _vm.Tree;
        var version = ++_version;
        _ = Task.Run(async () =>
        {
            try
            {
                // Debounce: selection changes fire rapidly with keyboard navigation.
                await Task.Delay(120, cts.Token);
                var stats = TreeStatisticsBuilder.Compute(tree, node, topFiles: 12, ct: cts.Token);
                _post(() =>
                {
                    if (version != _version) return;
                    _statsNode = node;
                    _body.Stats = stats;
                    _body.InvalidateLayout();
                });
            }
            catch (OperationCanceledException) { }
        }, cts.Token);
    }

    private sealed class Body(DetailsPanel owner) : Element
    {
        private readonly List<(SKRect Rect, int Node)> _fileRows = [];
        private readonly Tween _appear = new(0);
        private TreeStatistics? _stats;
        private float _height = 200;

        public TreeStatistics? Stats
        {
            get => _stats;
            set { _stats = value; _appear.Jump(0); Animate(_appear.To(1, 0.35f)); }
        }

        private ScanViewModel Vm => owner._vm;

        protected override SKSize MeasureContent(SKSize available) => new(available.Width, _height);

        protected override void OnDraw(SKCanvas canvas)
        {
            var t = Theme;
            var tree = Vm.Tree;
            var node = Vm.Selected;
            var stats = _stats;
            var statsFresh = stats is not null && owner._statsNode == node;
            _fileRows.Clear();

            const float pad = 16;
            var x = Bounds.Left + pad;
            var w = Bounds.Width - 2 * pad;
            var y = Bounds.Top + pad;

            // Headline
            var isDir = tree.IsDirectory(node);
            Icons.Draw(canvas, isDir ? Icon.Folder : Icon.File, x + 9, y + 9, 18, isDir ? t.Accent : FileColors.ColorOfExtension(tree.Extension(node)), 1.6f);
            TextRender.DrawEllipsized(canvas, tree.Name(node), x + 28, y + 9, w - 28, t.Title);
            y += 26;
            TextRender.DrawEllipsizedMiddle(canvas, tree.FullPath(node), x, y + 7, w, t.MonoSmall);
            y += 26;

            // Numbers
            var cells = isDir
                ? new[] { ("Size", ByteSize.Format(tree.TotalSize(node))), ("Files", tree.FileCount(node).ToString("N0")), ("Folders", tree.DirCount(node).ToString("N0")), ("Newest", Ago(tree.NewestModified(node))) }
                : new[] { ("Size", ByteSize.Format(tree.Size(node))), ("Type", tree.Extension(node).Length > 0 ? "." + tree.Extension(node) : "—"), ("Modified", Ago(tree.Modified(node))), ("", "") };
            var cellW = w / cells.Length;
            for (var i = 0; i < cells.Length; i++)
            {
                if (cells[i].Item1.Length == 0) continue;
                var cx = x + i * cellW;
                TextRender.Draw(canvas, cells[i].Item1, cx, y + 6, t.Caption);
                TextRender.DrawEllipsized(canvas, cells[i].Item2, cx, y + 26, cellW - 8, t.Mono.With(t.Text).With(14f));
            }
            y += 48;

            using var paint = new SKPaint { IsAntialias = true };
            paint.Color = t.Border;
            canvas.DrawRect(x, y, w, 1, paint);
            y += 14;

            if (!isDir || stats is null)
            {
                SetHeight(y - Bounds.Top + pad);
                return;
            }

            var alpha = statsFresh ? _appear.Value : 0.45f;
            var textColor = t.Text.WithAlpha((byte)(255 * alpha));

            // By type
            TextRender.Draw(canvas, "By type", x, y + 6, t.Caption);
            if (!statsFresh) TextRender.Draw(canvas, "updating…", x + w, y + 6, t.Caption, TextAlign.Right);
            y += 20;
            var byCat = stats.Extensions
                .GroupBy(e => FileColors.Categorize(e.Extension))
                .Select(g => (Cat: g.Key, Size: g.Sum(e => e.TotalSize), Count: g.Sum(e => e.FileCount)))
                .OrderByDescending(g => g.Size)
                .Take(7)
                .ToList();
            var total = Math.Max(1, stats.TotalSize);

            // Stacked bar
            var barRect = new SKRect(x, y, x + w, y + 10);
            paint.Color = t.SurfaceActive;
            canvas.DrawRoundRect(new SKRoundRect(barRect, 3), paint);
            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(barRect, 3), antialias: true);
            var bx = x;
            foreach (var c in byCat)
            {
                var bw = w * (float)c.Size / total * alpha;
                paint.Color = FileColors.ColorOf(c.Cat);
                canvas.DrawRect(bx, y, bw, 10, paint);
                bx += bw;
            }
            canvas.Restore();
            y += 18;

            foreach (var c in byCat)
            {
                paint.Color = FileColors.ColorOf(c.Cat);
                canvas.DrawCircle(x + 5, y + 8, 4, paint);
                TextRender.Draw(canvas, FileColors.Label(c.Cat), x + 16, y + 8, t.Small.With(textColor));
                var pct = 100.0 * c.Size / total;
                TextRender.Draw(canvas, $"{ByteSize.Format(c.Size)}  {pct:0.#}%", x + w, y + 8, t.MonoSmall.With(textColor), TextAlign.Right);
                y += 20;
            }
            y += 8;
            paint.Color = t.Border;
            canvas.DrawRect(x, y, w, 1, paint);
            y += 14;

            // Largest files
            TextRender.Draw(canvas, "Largest files", x, y + 6, t.Caption);
            y += 20;
            foreach (var f in stats.LargestFiles)
            {
                var rowRect = new SKRect(x - 6, y, x + w + 6, y + 24);
                _fileRows.Add((rowRect, f.Node));
                if (f.Node == Vm.Hovered || f.Node == Vm.Selected)
                {
                    paint.Color = f.Node == Vm.Selected ? t.Selection : t.SurfaceHover;
                    canvas.DrawRoundRect(new SKRoundRect(rowRect, t.RadiusSmall), paint);
                }
                var ext = tree.Extension(f.Node);
                paint.Color = FileColors.ColorOfExtension(ext);
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y + 7, x + 4, y + 17), 2), paint);
                var sizeText = ByteSize.Format(f.Size);
                var sizeW = t.MonoSmall.Measure(sizeText);
                TextRender.DrawEllipsized(canvas, tree.Name(f.Node), x + 12, y + 12, w - 12 - sizeW - 10, t.Small.With(textColor));
                TextRender.Draw(canvas, sizeText, x + w, y + 12, t.MonoSmall.With(textColor), TextAlign.Right);
                y += 24;
            }

            SetHeight(y - Bounds.Top + pad);
        }

        private void SetHeight(float h)
        {
            if (Math.Abs(h - _height) < 0.5f) return;
            _height = h;
            InvalidateLayout();
        }

        protected override void OnPointerMove(PointerEvent e)
        {
            Cursor = _fileRows.Any(r => r.Rect.Contains(e.Position)) ? CursorKind.Hand : CursorKind.Arrow;
            base.OnPointerMove(e);
        }

        protected override void OnClick(PointerEvent e)
        {
            foreach (var (rect, node) in _fileRows)
            {
                if (rect.Contains(e.Position))
                {
                    Vm.Selected = node;
                    Vm.Reveal(node);
                    e.Handled = true;
                    return;
                }
            }
            base.OnClick(e);
        }

        private static string Ago(DateTime utc)
        {
            if (utc.Ticks == 0) return "—";
            var span = DateTime.UtcNow - utc;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours} h ago";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays} d ago";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)} mo ago";
            return $"{span.TotalDays / 365:0.#} y ago";
        }
    }
}

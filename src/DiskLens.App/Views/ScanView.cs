using System.Diagnostics;
using DiskLens.App.Controls;
using DiskLens.App.Shell;
using DiskLens.Core;
using DiskLens.Core.Model;
using DiskLens.Core.Scanning;
using DiskLens.UI.Animation;
using DiskLens.UI.Elements;
using DiskLens.UI.Input;
using DiskLens.UI.Layout;
using DiskLens.UI.Rendering;
using DiskLens.UI.Widgets;
using SkiaSharp;

namespace DiskLens.App.Views;

/// <summary>
/// The analysis screen: toolbar with breadcrumb and scan status, tree on the left, treemap and
/// details on the right. Polls the running scan and pushes periodic refreshes into the views.
/// </summary>
public sealed class ScanView : Element
{
    private readonly AppShell _shell;
    private readonly ScanViewModel _vm;
    private readonly Breadcrumb _crumbs;
    private readonly Label _status = new() { StyleSelector = t => t.Small, Align = TextAlign.Right };
    private readonly ProgressBar _progress = new() { FixedWidth = 140, Thickness = 4 };
    private readonly Button _stop = new("Stop", Icon.Stop) { Style = ButtonStyle.Danger };
    private readonly Button _rescan = new("Rescan", Icon.Refresh) { IsVisible = false };
    private readonly Button _open = new("Open", Icon.ExternalLink) { Style = ButtonStyle.Ghost, Tooltip = "Open in file manager" };
    private readonly Stopwatch _sinceRefresh = Stopwatch.StartNew();

    public ScanView(AppShell shell, ScanSession session)
    {
        _shell = shell;
        _vm = new ScanViewModel(session);
        _crumbs = new Breadcrumb(_vm) { Flex = 1 };

        var root = Add(new Column());

        // Toolbar ----------------------------------------------------------------------------------
        var bar = root.Add(new Row { Gap = 10, Padding = new Thickness(14, 8), FixedHeight = 46 });
        bar.Add(_crumbs);
        bar.Add(_status);
        bar.Add(_progress);
        bar.Add(_stop);
        bar.Add(_rescan);
        bar.Add(_open);
        _stop.Activated += session.Cancel;
        _rescan.Activated += () => shell.StartScan(session.Target);
        _open.Activated += OpenSelected;

        // Panes ------------------------------------------------------------------------------------
        var panes = root.Add(new Row { Flex = 1, CrossAlign = CrossAlign.Stretch });
        var left = panes.Add(new Box { Flex = 1, MinWidth = 420 });
        left.Add(new TreeList(_vm));
        panes.Add(new Divider { Axis = Axis.Vertical });

        var right = panes.Add(new Column { Flex = 1.15f });
        right.Add(new TreemapView(_vm) { Flex = 1.6f, Margin = new Thickness(0) });
        right.Add(new Divider());
        var details = right.Add(new Box { Flex = 1, MinHeight = 200 });
        details.Add(new DetailsPanel(_vm, shell.Post));

        _progress.IsIndeterminate = session.IsRunning;
        Animate(new ScanPoller(this));
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var s = _vm.Session;
        var elapsed = s.Elapsed;
        var rate = elapsed.TotalSeconds > 0.5 ? s.NodesAdded / elapsed.TotalSeconds : 0;
        _status.Text = s.State switch
        {
            ScanState.Running => $"{s.NodesAdded:N0} items · {ByteSize.Format(s.BytesSeen)} · {rate:N0}/s",
            ScanState.Completed => $"{s.Tree.FileCount(FsTree.Root):N0} files · {s.Tree.DirCount(FsTree.Root):N0} folders · {ByteSize.Format(s.Tree.TotalSize(FsTree.Root))} · {elapsed.TotalSeconds:0.0}s · {s.Scanner.DisplayName}"
                                   + (s.Errors.Count > 0 ? $" · {s.Errors.Count} skipped" : ""),
            ScanState.Cancelled => $"Stopped after {elapsed.TotalSeconds:0.0}s · {s.NodesAdded:N0} items",
            ScanState.Failed => $"Failed: {s.Error?.Message}",
            _ => "",
        };
    }

    private void OnFinished()
    {
        _progress.IsIndeterminate = false;
        _progress.IsVisible = false;
        _stop.IsVisible = false;
        _rescan.IsVisible = true;
        _vm.NotifyDataChanged();
        UpdateStatus();
        InvalidateLayout();
    }

    private void OpenSelected()
    {
        var tree = _vm.Tree;
        var node = _vm.Selected;
        var path = tree.FullPath(node);
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", tree.IsDirectory(node) ? $"\"{path}\"" : $"/select,\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", tree.IsDirectory(node) ? [path] : ["-R", path]);
            else
                Process.Start("xdg-open", [tree.IsDirectory(node) ? path : Path.GetDirectoryName(path) ?? path]);
        }
        catch (Exception ex)
        {
            _status.Text = "Could not open: " + ex.Message;
        }
    }

    protected override void OnKeyDown(KeyEvent e)
    {
        if (e.Key == Key.F5 && _vm.Session.IsFinished) { _shell.StartScan(_vm.Session.Target); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    /// <summary>Per-frame poll while the scan runs: status text every frame, data refresh every 400 ms.</summary>
    private sealed class ScanPoller(ScanView view) : IAnimation
    {
        private bool _finishedHandled;

        public bool Tick(float dt)
        {
            var s = view._vm.Session;
            if (s.IsFinished)
            {
                if (!_finishedHandled) { _finishedHandled = true; view.OnFinished(); }
                return false;
            }
            view.UpdateStatus();
            if (view._sinceRefresh.ElapsedMilliseconds > 400)
            {
                view._sinceRefresh.Restart();
                view._vm.NotifyDataChanged();
            }
            view.Invalidate();
            return true;
        }
    }

    /// <summary>Clickable path of the treemap zoom root.</summary>
    private sealed class Breadcrumb : Element
    {
        private readonly ScanViewModel _vm;
        private readonly List<(SKRect Rect, int Node)> _hits = [];
        private int _hover = -1;

        public Breadcrumb(ScanViewModel vm)
        {
            _vm = vm;
            vm.ZoomChanged += _ => Invalidate();
            vm.DataChanged += Invalidate;
            Cursor = CursorKind.Hand;
        }

        protected override SKSize MeasureContent(SKSize available) => new(available.Width, 30);

        protected override void OnDraw(SKCanvas canvas)
        {
            var t = Theme;
            var tree = _vm.Tree;
            var path = _vm.ZoomPath();
            _hits.Clear();
            var x = Bounds.Left;
            var cy = Bounds.MidY;
            using var paint = new SKPaint { IsAntialias = true };

            for (var i = 0; i < path.Count; i++)
            {
                var node = path[i];
                var last = i == path.Count - 1;
                var name = tree.Name(node);
                var style = last ? t.BodyMedium : t.BodySecondary;
                var w = Math.Min(style.Measure(name), 220) + 16;
                var rect = new SKRect(x, cy - 13, x + w, cy + 13);
                if (_hover == i && !last)
                {
                    paint.Color = t.SurfaceHover;
                    canvas.DrawRoundRect(new SKRoundRect(rect, t.RadiusSmall), paint);
                }
                TextRender.DrawEllipsized(canvas, name, x + 8, cy, w - 16, style);
                _hits.Add((rect, node));
                x += w;
                if (!last)
                {
                    Icons.Draw(canvas, Icon.ChevronRight, x + 6, cy, 12, t.TextMuted, 1.5f);
                    x += 14;
                }
                if (x > Bounds.Right - 40) break;
            }

            if (path.Count > 1)
            {
                var hint = "Backspace / right-click to zoom out";
                var hx = x + 12;
                if (hx + t.SmallMuted.Measure(hint) < Bounds.Right) TextRender.Draw(canvas, hint, hx, cy, t.SmallMuted);
            }
        }

        protected override void OnPointerMove(PointerEvent e)
        {
            var h = _hits.FindIndex(r => r.Rect.Contains(e.Position));
            if (h != _hover) { _hover = h; Invalidate(); }
            base.OnPointerMove(e);
        }

        protected override void OnPointerExit()
        {
            _hover = -1;
            base.OnPointerExit();
        }

        protected override void OnClick(PointerEvent e)
        {
            foreach (var (rect, node) in _hits)
            {
                if (rect.Contains(e.Position)) { _vm.ZoomRoot = node; _vm.Selected = node; e.Handled = true; return; }
            }
            base.OnClick(e);
        }
    }
}

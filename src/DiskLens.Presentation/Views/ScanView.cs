using System.Diagnostics;
using DiskLens.Presentation.Controls;
using DiskLens.Presentation.Shell;
using DiskLens.Core;
using DiskLens.Core.Model;
using DiskLens.Core.Platform;
using DiskLens.Core.Scanning;
using DiskLens.UI.Animation;
using DiskLens.UI.Elements;
using DiskLens.UI.Input;
using DiskLens.UI.Layout;
using DiskLens.UI.Rendering;
using DiskLens.UI.Widgets;
using SkiaSharp;

namespace DiskLens.Presentation.Views;

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
        _vm.ContextMenuRequested += ShowContextMenu;
        Animate(new ScanPoller(this));
        UpdateStatus();
    }

    /// <summary>
    /// Right-click: on Windows the Explorer menu (with our zoom items on top) is the default; our own
    /// menu is one Shift away. Elsewhere our menu is all there is.
    /// </summary>
    private void ShowContextMenu(int node, SKPoint at, bool alternate)
    {
        var tree = _vm.Tree;
        if (_shell.NativeMenu.IsSupported && !alternate && node != FsTree.Root)
        {
            ShowNativeMenu(node, at);
            return;
        }
        var isDir = tree.IsDirectory(node);
        var isRoot = node == FsTree.Root;
        var finished = _vm.Session.IsFinished;
        var items = new List<MenuItem>
        {
            new(OperatingSystem.IsWindows() ? "Open in Explorer" : "Reveal in file manager", Icon.ExternalLink, () => Reveal(node)),
            new("Copy path", Icon.Copy, () => Root?.Clipboard?.SetText(tree.FullPath(node)), Shortcut: "Ctrl+C"),
        };
        if (isDir)
        {
            items.Add(new("Zoom treemap here", Icon.Search, () => _vm.ZoomRoot = node, IsEnabled: node != _vm.ZoomRoot));
        }
        if (_vm.ZoomRoot != FsTree.Root)
        {
            items.Add(new("Zoom out", Icon.ArrowUp, _vm.ZoomOut, Shortcut: "Backspace"));
        }
        if (_shell.NativeMenu.IsSupported && node != FsTree.Root)
        {
            items.Add(MenuItem.Separator);
            items.Add(new("Explorer menu…", Icon.Settings, () => ShowNativeMenu(node, at), Shortcut: "Right-click"));
        }
        items.Add(MenuItem.Separator);
        var deleteLabel = _shell.Files.SupportsRecycleBin ? "Move to Recycle Bin" : "Delete permanently";
        items.Add(new(deleteLabel, Icon.Trash, () => ConfirmDelete(node), IsDanger: true, IsEnabled: finished && !isRoot, Shortcut: "Del"));

        if (Root is { } root) ContextMenu.Show(root, at, [.. items]);
    }

    private void ShowNativeMenu(int node, SKPoint at)
    {
        var tree = _vm.Tree;
        var path = tree.FullPath(node);
        var isDir = tree.IsDirectory(node);

        // Our items on top of the shell menu; the rest (open, copy path, delete, properties...) is Explorer's.
        var custom = new List<NativeMenuItem>();
        var actions = new List<Action>();
        if (isDir)
        {
            custom.Add(new NativeMenuItem("Zoom treemap here", IsEnabled: node != _vm.ZoomRoot));
            actions.Add(() => _vm.ZoomRoot = node);
        }
        if (_vm.ZoomRoot != FsTree.Root)
        {
            custom.Add(new NativeMenuItem("Zoom out"));
            actions.Add(_vm.ZoomOut);
        }
        custom.Add(new NativeMenuItem("Reveal in tree"));
        actions.Add(() => _vm.Reveal(node));

        // Runs modally on the UI thread.
        var chosen = _shell.ShowNativeMenu(path, at, custom);
        if (chosen >= 0 && chosen < actions.Count)
        {
            actions[chosen]();
            return;
        }

        // Explorer commands run outside our tree. Deletions are the common case and easy to detect:
        // if the entry is gone shortly after the menu closes, drop it from the tree.
        if (_vm.Session.IsFinished) _ = WatchForRemovalAsync(node, path, isDir);
    }

    private async Task WatchForRemovalAsync(int node, string path, bool isDir)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await Task.Delay(250);
            var exists = isDir ? Directory.Exists(path) : File.Exists(path);
            if (exists) continue;
            _shell.Post(() =>
            {
                if (_vm.Tree.HasFlag(node, NodeFlags.Deleted)) return;
                var parent = _vm.Tree.Parent(node);
                _vm.Session.Builder.RemoveSubtree(node);
                if (_vm.ZoomRoot == node || IsUnder(_vm.ZoomRoot, node)) _vm.ZoomRoot = parent;
                if (_vm.Selected == node) _vm.Selected = parent;
                _vm.NotifyDataChanged();
                UpdateStatus();
            });
            return;
        }
        // Renames, edits etc. are not tracked – a rescan picks them up.
        _shell.Post(() => { if (_vm.Session.IsFinished) _status.Text = "Changes made through the Explorer menu show up after a rescan (F5)."; });
    }

    private void Reveal(int node)
    {
        try { _shell.Files.RevealInFileManager(_vm.Tree.FullPath(node), _vm.Tree.IsDirectory(node)); }
        catch (Exception ex) { _status.Text = "Could not open: " + ex.Message; }
    }

    private void ConfirmDelete(int node)
    {
        if (Root is not { } root || node == FsTree.Root || !_vm.Session.IsFinished) return;
        var tree = _vm.Tree;
        var name = tree.Name(node);
        var size = ByteSize.Format(tree.TotalSize(node));
        var what = tree.IsDirectory(node) ? $"the folder \"{name}\" ({size}, {tree.FileCount(node):N0} files)" : $"\"{name}\" ({size})";
        var (title, verb) = _shell.Files.SupportsRecycleBin
            ? ("Move to Recycle Bin?", "Move to Recycle Bin")
            : ("Delete permanently?", "Delete");
        var message = _shell.Files.SupportsRecycleBin
            ? $"{what} will be moved to the Recycle Bin. You can restore it from there."
            : $"{what} will be deleted permanently. This cannot be undone.";
        new ConfirmDialog(title, message, verb, () => _ = DeleteAsync(node), danger: true).Show(root);
    }

    private async Task DeleteAsync(int node)
    {
        var tree = _vm.Tree;
        var path = tree.FullPath(node);
        var isDir = tree.IsDirectory(node);
        _status.Text = $"Deleting {tree.Name(node)}…";
        try
        {
            await _shell.Files.DeleteAsync(path, isDir, CancellationToken.None);
            _shell.Post(() =>
            {
                var parent = tree.Parent(node);
                _vm.Session.Builder.RemoveSubtree(node);
                if (_vm.ZoomRoot == node || IsUnder(_vm.ZoomRoot, node)) _vm.ZoomRoot = parent;
                _vm.Selected = parent;
                _vm.NotifyDataChanged();
                UpdateStatus();
            });
        }
        catch (Exception ex)
        {
            _shell.Post(() => _status.Text = "Delete failed: " + ex.Message);
        }
    }

    private bool IsUnder(int node, int ancestor)
    {
        for (var n = node; n != FsTree.None; n = _vm.Tree.Parent(n)) if (n == ancestor) return true;
        return false;
    }

    private void UpdateStatus()
    {
        var s = _vm.Session;
        var elapsed = s.Elapsed;
        var rate = elapsed.TotalSeconds > 0.5 ? s.NodesAdded / elapsed.TotalSeconds : 0;
        if (s.State == ScanState.Running)
        {
            var fraction = s.PhaseFraction;
            if (fraction is { } f) { _progress.IsIndeterminate = false; _progress.Value = (float)f; }
            else if (!_progress.IsIndeterminate) _progress.IsIndeterminate = true;
        }
        _status.Text = s.State switch
        {
            ScanState.Running when s.Phase is { } phase && s.NodesAdded == 0 => phase,
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

    private void OpenSelected() => Reveal(_vm.Selected);

    protected override void OnKeyDown(KeyEvent e)
    {
        var ctrl = (e.Modifiers & Modifiers.Control) != 0;
        switch (e.Key)
        {
            case Key.F5 when _vm.Session.IsFinished: _shell.StartScan(_vm.Session.Target); break;
            case Key.C when ctrl: Root?.Clipboard?.SetText(_vm.Tree.FullPath(_vm.Selected)); break;
            case Key.Delete: ConfirmDelete(_vm.Selected); break;
            case Key.Backspace: _vm.ZoomOut(); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
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

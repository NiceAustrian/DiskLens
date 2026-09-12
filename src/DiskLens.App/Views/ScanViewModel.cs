using DiskLens.Core.Model;
using DiskLens.Core.Scanning;

namespace DiskLens.App.Views;

/// <summary>
/// State shared by the tree list, the treemap and the details panel: which node is selected,
/// hovered, and which directory the treemap is zoomed into. Plain events; everything happens on the
/// UI thread.
/// </summary>
public sealed class ScanViewModel
{
    private int _selected = FsTree.Root;
    private int _hovered = FsTree.None;
    private int _zoomRoot = FsTree.Root;

    public ScanViewModel(ScanSession session)
    {
        Session = session;
    }

    public ScanSession Session { get; }
    public FsTree Tree => Session.Tree;

    public int Selected
    {
        get => _selected;
        set
        {
            if (_selected == value || value < 0) return;
            _selected = value;
            SelectionChanged?.Invoke(value);
        }
    }

    public int Hovered
    {
        get => _hovered;
        set
        {
            if (_hovered == value) return;
            _hovered = value;
            HoverChanged?.Invoke(value);
        }
    }

    /// <summary>The directory the treemap shows. Always a directory.</summary>
    public int ZoomRoot
    {
        get => _zoomRoot;
        set
        {
            if (_zoomRoot == value || value < 0 || !Tree.IsDirectory(value)) return;
            _zoomRoot = value;
            ZoomChanged?.Invoke(value);
        }
    }

    /// <summary>Fires when the tree has grown or the scan finished – views should refresh derived data.</summary>
    public event Action? DataChanged;
    public event Action<int>? SelectionChanged;
    public event Action<int>? HoverChanged;
    public event Action<int>? ZoomChanged;

    /// <summary>Request that the tree list scrolls to and reveals a node (e.g. after a treemap click).</summary>
    public event Action<int>? RevealRequested;

    public void NotifyDataChanged()
    {
        _sortedChildren.Clear();
        DataChanged?.Invoke();
    }

    private readonly Dictionary<int, int[]> _sortedChildren = [];

    /// <summary>Children of <paramref name="node"/> sorted by total size, largest first. Cached until the data changes.</summary>
    public int[] SortedChildren(int node)
    {
        if (_sortedChildren.TryGetValue(node, out var cached)) return cached;
        var list = new List<int>();
        foreach (var c in Tree.Children(node)) list.Add(c);
        var arr = list.ToArray();
        var tree = Tree;
        Array.Sort(arr, (a, b) =>
        {
            var cmp = tree.TotalSize(b).CompareTo(tree.TotalSize(a));
            return cmp != 0 ? cmp : string.CompareOrdinal(tree.Name(a), tree.Name(b));
        });
        _sortedChildren[node] = arr;
        return arr;
    }
    public void Reveal(int node) => RevealRequested?.Invoke(node);

    public void ZoomOut()
    {
        if (_zoomRoot != FsTree.Root) ZoomRoot = Tree.Parent(_zoomRoot);
    }

    /// <summary>Ancestors of the zoom root from the scan root down, for a breadcrumb.</summary>
    public List<int> ZoomPath()
    {
        var path = new List<int>();
        for (var n = _zoomRoot; n != FsTree.None; n = Tree.Parent(n)) path.Add(n);
        path.Reverse();
        return path;
    }
}

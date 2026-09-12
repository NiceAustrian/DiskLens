using DiskLens.App.Shell;
using DiskLens.Core;
using DiskLens.Core.Scanning;
using DiskLens.UI.Elements;
using DiskLens.UI.Layout;
using DiskLens.UI.Widgets;

namespace DiskLens.App.Views;

/// <summary>Placeholder until the real three-pane view lands: shows live scan counters.</summary>
public sealed class ScanView : Element
{
    private readonly ScanSession _session;
    private readonly Label _counter = new() { StyleSelector = t => t.Heading };
    private readonly Label _state = new() { StyleSelector = t => t.Small };

    public ScanView(AppShell shell, ScanSession session)
    {
        _session = session;
        var col = Add(new Column { Padding = new Thickness(32), Gap = 12 });
        col.Add(_counter);
        col.Add(_state);
        var cancel = col.Add(new Button("Stop", UI.Rendering.Icon.Stop) { Style = ButtonStyle.Danger });
        cancel.Activated += session.Cancel;
        Animate(new Poll(this));
    }

    private sealed class Poll(ScanView v) : UI.Animation.IAnimation
    {
        public bool Tick(float dt)
        {
            v._counter.Text = $"{v._session.NodesAdded:N0} items · {ByteSize.Format(v._session.BytesSeen)}";
            v._state.Text = $"{v._session.State} · {v._session.Scanner.DisplayName} · {v._session.Elapsed.TotalSeconds:0.0}s";
            v.Invalidate();
            return !v._session.IsFinished || dt < 0;
        }
    }
}

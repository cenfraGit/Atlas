using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Atlas;

/// <summary>the stops of a board's tour, down the right hand side. Click one
/// to look at it, double click to play from it, drag to reorder.
///
/// It edits the board's list in place and says so through
/// <see cref="Changed"/>; the view does the saving, as it does for items.</summary>
public sealed class TourPanel : Border
{
    readonly TextBlock _title;
    readonly TextBlock _hint;
    readonly ListBox _list;
    readonly Button _play, _rename, _delete;

    Board? _board;

    // a drag reorders the list live, so the rows themselves are the preview.
    // The order from before it started is kept so Escape can put it back
    int _dragFrom = -1, _dragAt = -1;
    List<Stop>? _before;
    Point _pressAt;

    public event Action<int>? Preview;
    public event Action<int>? Play;
    public event Action? Capture;
    public event Action<int>? RenameRequested;
    public event Action? Changed;

    public TourPanel()
    {
        Reveal.Attach(this, Reveal.Edge.Right);
        Background = Ui.PanelBg;
        BorderBrush = Ui.Edge;
        BorderThickness = new Thickness(1, 0, 0, 0);
        // clear of the mode islands in the top right corner, which sit over
        // this panel and hid its buttons, and of the hint bar along the bottom
        Padding = new Thickness(12, 84, 12, 12);
        Margin = new Thickness(0, 0, 0, 30);
        Width = 300;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Stretch;

        _title = new TextBlock
        {
            FontFamily = Ui.Mono, FontSize = 12, Foreground = Ui.Accent,
            Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap,
        };
        _hint = new TextBlock
        {
            FontFamily = Ui.Mono, FontSize = 11, Foreground = Ui.Dim,
            Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap,
        };
        _list = new ListBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            FontFamily = Ui.Mono, FontSize = 12, Foreground = Ui.Fore,
            // every row, always. The virtualizing default was handed its
            // rows while the panel was hidden and then drew only the first
            // until the selection moved - and a tour is a handful of stops
            ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new StackPanel()),
        };
        _list.DoubleTapped += (_, _) => { if (Selected >= 0) Play?.Invoke(Selected); };
        _list.AddHandler(PointerPressedEvent, OnPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _list.AddHandler(PointerMovedEvent, OnMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _list.AddHandler(PointerReleasedEvent, OnReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var capture = Make("capture  M", () => Capture?.Invoke());
        _play = Make("play  P", () => Play?.Invoke(Math.Max(0, Selected)));
        _rename = Make("rename", () => { if (Selected >= 0) RenameRequested?.Invoke(Selected); });
        _delete = Make("delete", Remove);

        Child = new DockPanel
        {
            Children =
            {
                Docked(_title, Dock.Top),
                Docked(new WrapPanel
                {
                    Margin = new Thickness(0, 0, 0, 10),
                    Children = { capture, _play, _rename, _delete },
                }, Dock.Top),
                Docked(_hint, Dock.Bottom),
                _list,
            },
        };
    }

    static Control Docked(Control c, Dock side)
    {
        DockPanel.SetDock(c, side);
        return c;
    }

    static Button Make(string text, Action run)
    {
        var b = new Button
        {
            Content = text, FontFamily = Ui.Mono, FontSize = 11,
            Padding = new Thickness(9, 3), Margin = new Thickness(0, 0, 4, 4),
            Background = Brushes.Transparent, Foreground = Ui.Fore,
            BorderThickness = new Thickness(1), BorderBrush = Ui.Edge,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        b.Click += (_, _) => run();
        return b;
    }

    public int Selected => _list.SelectedIndex;

    /// <summary>follow a tour that is playing.</summary>
    public void Select(int i)
    {
        if (i < 0 || i >= _list.ItemCount) return;
        _list.SelectedIndex = i;
        _list.ScrollIntoView(i);
    }

    /// <summary>whether a drag is under way, for Escape.</summary>
    public bool Dragging => _dragFrom >= 0 && _dragAt != _dragFrom;

    public void Show(Board board)
    {
        _board = board;
        Rebuild(Math.Max(0, Selected));
        Reveal.Show(this);
    }

    public void Close()
    {
        CancelDrag();
        Reveal.Hide(this);
    }

    /// <summary>redraw the rows, selecting one.</summary>
    public void Rebuild(int select)
    {
        var stops = _board?.Stops ?? [];
        _title.Text = $"TOUR  -  {_board?.Name}";
        _list.ItemsSource = stops.Select((s, i) => $"{i + 1,2}.  {Label(s, i)}").ToList();
        _list.SelectedIndex = stops.Count == 0 ? -1 : Math.Clamp(select, 0, stops.Count - 1);

        _hint.Text = stops.Count == 0
            ? "frame the view you want, then M. move and M again for the next stop."
            : "click: look   double click: play from it   drag: reorder\n" +
              "playing: space or arrows to step, esc to stop";
        _play.IsEnabled = _rename.IsEnabled = _delete.IsEnabled = stops.Count > 0;
        foreach (var b in new[] { _play, _rename, _delete })
            b.Foreground = b.IsEnabled ? Ui.Fore : Ui.Dim;
    }

    public static string Label(Stop s, int i) => string.IsNullOrWhiteSpace(s.Name) ? $"stop {i + 1}" : s.Name;

    public void Remove()
    {
        if (_board is null || Selected < 0) return;
        int at = Selected;
        _board.Stops.RemoveAt(at);
        Rebuild(at);
        Changed?.Invoke();
    }

    /// <summary>move a stop to another place in the order.</summary>
    public void Move(int from, int to)
    {
        if (_board is null || from < 0 || from >= _board.Stops.Count) return;
        to = Math.Clamp(to, 0, _board.Stops.Count - 1);
        if (from == to) return;
        var s = _board.Stops[from];
        _board.Stops.RemoveAt(from);
        _board.Stops.Insert(to, s);
        Rebuild(to);
    }

    // ---- drag to reorder, by hand: the rows are plain strings, so there are
    // no item containers for avalonia's DragDrop to hang off ----

    void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressAt = e.GetPosition(_list);
        _dragFrom = _dragAt = RowAt(_pressAt);
        _before = _board?.Stops.ToList();
    }

    void OnMoved(object? sender, PointerEventArgs e)
    {
        // a press on a row is under way until its release clears it
        if (_dragFrom < 0) return;
        var p = e.GetPosition(_list);
        if (_dragAt == _dragFrom && Math.Abs(p.Y - _pressAt.Y) < 6) return;     // not a drag yet
        int to = RowAt(p);
        if (to < 0 || to == _dragAt) return;
        Move(_dragAt, to);
        _dragAt = to;
    }

    void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        int from = _dragFrom, at = _dragAt;
        _dragFrom = _dragAt = -1;
        _before = null;
        if (from < 0) return;
        if (at != from) { Changed?.Invoke(); return; }
        // a click, not a drag: go and look at it
        Preview?.Invoke(from);
    }

    /// <summary>put the order back as it was before the drag.</summary>
    public void CancelDrag()
    {
        if (_board is not null && _before is not null && Dragging)
        {
            _board.Stops.Clear();
            _board.Stops.AddRange(_before);
            Rebuild(_dragFrom);
        }
        _dragFrom = _dragAt = -1;
        _before = null;
    }

    int RowAt(Point p)
    {
        // asked of the rows themselves: the list stretches down the whole
        // panel, so its height divided by the row count is not a row
        int n = _board?.Stops.Count ?? 0;
        for (int i = 0; i < n; i++)
        {
            if (_list.ContainerFromIndex(i) is not { } row) continue;
            var top = row.TranslatePoint(new Point(0, 0), _list);
            if (top is { } t && p.Y >= t.Y && p.Y < t.Y + row.Bounds.Height) return i;
        }
        return -1;
    }

    /// <summary>keys that reached the canvas while this is open. Not Delete:
    /// the panel stays open while you work on the board, and Delete there
    /// means the items you picked, not the stop the list happens to have
    /// selected.</summary>
    public bool HandleKey(Key key)
    {
        int n = _board?.Stops.Count ?? 0;
        switch (key)
        {
            case Key.Down or Key.Up when n > 0:
                int to = Math.Clamp(Selected + (key == Key.Down ? 1 : -1), 0, n - 1);
                _list.SelectedIndex = to;
                _list.ScrollIntoView(to);
                Preview?.Invoke(to);
                return true;
            case Key.Enter when n > 0: Play?.Invoke(Math.Max(0, Selected)); return true;
            case Key.F2 when Selected >= 0: RenameRequested?.Invoke(Selected); return true;
            default: return false;
        }
    }
}

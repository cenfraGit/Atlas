using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Atlas;

/// <summary>a row in the workspace panel: the map, a group heading or a
/// board.</summary>
public sealed class BoardRow
{
    public string Group { get; init; } = "";
    public Board? Board { get; init; }

    /// <summary>the map - "Home" - pinned at the top. It is somewhere to go,
    /// like a board, but not a board: it cannot be moved, renamed, grouped
    /// or deleted, and nothing can be dropped on it.</summary>
    public bool IsHome { get; init; }

    public bool IsHeader => Board is null && !IsHome;

    public override string ToString() => IsHome ? "Home"
        : IsHeader ? (Group.Length == 0 ? "ungrouped" : Group)
        : "   " + Board!.Name + "   (" + Board.Items.Count + ")";
}

/// <summary>the workspace: the map and every board, grouped, with buttons
/// for what you can do to the ones you have selected. Tab opens it from
/// anywhere. It used to explain its keys in a wall of text; a button that
/// does the thing is easier to read than a sentence about a key.</summary>
public sealed class BoardOverlay : Border
{
    readonly BoardStore _store;
    readonly ListBox _list;
    readonly List<BoardRow> _rows = [];
    readonly Button _open, _rename, _group, _delete;

    // a drag rearranges the list as it goes, so the rows themselves are the
    // preview of where things will land. What everything was before it
    // started is kept so Escape can put it back
    Board? _dragBoard;
    string? _dragGroup;
    bool _moved;
    Point _pressAt;
    List<(Board Board, string Group, int Order)>? _before;
    List<string>? _beforeGroups;

    public event Action<Board>? Open;

    /// <summary>go to the map.</summary>
    public event Action? HomeRequested;
    public event Action? CreateRequested;
    public event Action<Board>? RenameRequested;
    public event Action<Board>? GroupRequested;
    public event Action<List<Board>>? DeleteRequested;

    public BoardOverlay(BoardStore store)
    {
        _store = store;
        Reveal.Attach(this, Reveal.Edge.Left);
        Background = Ui.PanelBg;
        BorderBrush = Ui.Edge;
        BorderThickness = new Thickness(0, 0, 1, 0);
        Padding = new Thickness(12);
        Width = 340;
        MinWidth = MinW;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Stretch;

        _list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontFamily = Ui.Mono,
            FontSize = 12,
            Foreground = Ui.Fore,
            SelectionMode = SelectionMode.Multiple,
            // every row, always: the virtualizing default drew the first
            // couple and left the rest as empty space until something scrolled
            ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new StackPanel()),
        };
        // no sideways scrolling, so a long name wraps to the panel's width
        // instead of running off its edge
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        _list.DoubleTapped += (_, _) => Commit();
        _list.SelectionChanged += (_, _) => { DropHeaders(); Reflect(); };

        // a heading lights up while it is pressed - that says a drag of the
        // group has begun - but not merely hovered, which made headings look
        // like buttons
        _list.ContainerPrepared += (_, e) =>
            e.Container.Classes.Set("header", e.Index < _rows.Count && _rows[e.Index].IsHeader);
        _list.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<ListBoxItem>().Class("header")
            .Class(":pointerover").Not(y => y.Class(":pressed"))
            .Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, Brushes.Transparent) },
        });
        _list.AddHandler(PointerPressedEvent, OnPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _list.AddHandler(PointerMovedEvent, OnMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _list.AddHandler(PointerReleasedEvent, OnReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        _open = Make("open", Commit);
        _rename = Make("rename", () => { if (One is { } b) RenameRequested?.Invoke(b); });
        _group = Make("group", () => { if (One is { } b) GroupRequested?.Invoke(b); });
        _delete = Make("delete", () => { var s = Selected; if (s.Count > 0) DeleteRequested?.Invoke(s); });
        var create = Make("new", () => CreateRequested?.Invoke());

        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10),
            Children = { _open, _rename, _group, _delete, create },
        };

        // a grip down the right edge drags the width: names are long, and a
        // fixed panel either wasted space or cut them off
        var grip = new Border
        {
            Width = 6, Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, -12, -13, -12),
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
        };
        grip.PointerPressed += (_, e) =>
        {
            _resizeFrom = e.GetPosition(this).X - Width;
            e.Pointer.Capture(grip);
            e.Handled = true;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (_resizeFrom is not { } from) return;
            Width = Math.Clamp(e.GetPosition(this).X - from, MinW, MaxW);
        };
        grip.PointerReleased += (_, e) => { _resizeFrom = null; e.Pointer.Capture(null); };

        var content = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = "WORKSPACE", FontFamily = Ui.Mono, FontSize = 12,
                    Foreground = Ui.Accent, Margin = new Thickness(0, 0, 0, 8),
                },
                buttons,
                _list,
                new TextBlock
                {
                    Text = "tab opens and closes this from anywhere. drag a board between groups, or a group heading to reorder groups. esc cancels a drag. drag the right edge to widen.",
                    FontFamily = Ui.Mono, FontSize = 11, Foreground = Ui.Dim,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
                },
            },
        };
        Child = new Grid { Children = { content, grip } };
    }

    const double MinW = 240, MaxW = 900;
    double? _resizeFrom;

    static Button Make(string text, Action run)
    {
        var b = new Button
        {
            Content = text,
            FontFamily = Ui.Mono,
            FontSize = 11,
            Padding = new Thickness(9, 3),
            Margin = new Thickness(0, 0, 4, 4),
            Background = Brushes.Transparent,
            Foreground = Ui.Dim,
            BorderThickness = new Thickness(1),
            BorderBrush = Ui.Edge,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        b.Click += (_, _) => run();
        return b;
    }

    public void Show()
    {
        Rebuild();
        Reveal.Show(this);
    }

    public void Close()
    {
        CancelDrag();
        Reveal.Hide(this);
    }

    /// <summary>the rows as they are shown, top to bottom. For the tests.</summary>
    public IReadOnlyList<BoardRow> Rows => _rows;

    public void Rebuild()
    {
        var keep = Selected;
        bool home = _list.SelectedItems is { Count: 1 } && _list.SelectedIndex == 0 && _rows is [{ IsHome: true }, ..];
        _rows.Clear();
        _rows.Add(new BoardRow { IsHome = true });

        foreach (var group in _store.Groups())
        {
            _rows.Add(new BoardRow { Group = group });
            foreach (var b in _store.Boards.Where(x => x.Group == group)
                         .OrderBy(x => x.Order).ThenBy(x => x.Name, StringComparer.Ordinal))
                _rows.Add(new BoardRow { Group = group, Board = b });
        }

        _list.ItemsSource = _rows.Select((r, i) => r.IsHome ? HomeLine() : r.IsHeader ? Header(r, i == 1) : Line(r)).ToList();
        _list.SelectedItems?.Clear();
        foreach (var b in keep)
        {
            int at = _rows.FindIndex(r => r.Board == b);
            if (at >= 0) _list.SelectedItems?.Add(_list.Items[at]);
        }
        if (home) _list.SelectedIndex = 0;
        if (_list.SelectedItems is { Count: 0 })
        {
            int first = _rows.FindIndex(r => r.Board is not null);
            _list.SelectedIndex = first >= 0 ? first : 0;
        }
        Reflect();
    }

    /// <summary>the map, as the first thing in the list.</summary>
    static Control HomeLine() => new TextBlock
    {
        Text = "Home", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 2, 0, 4),
    };

    /// <summary>a group heading. It used to be a board row shifted left, and
    /// read as one: now it is set apart by case, colour, a count and a rule
    /// above it.</summary>
    Control Header(BoardRow r, bool first)
    {
        int count = _store.Boards.Count(b => b.Group == r.Group);
        return new Border
        {
            BorderBrush = Ui.Edge,
            BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0),
            Margin = new Thickness(0, first ? 0 : 8, 0, 0),
            Padding = new Thickness(0, first ? 2 : 8, 0, 2),
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text = r.ToString().ToUpperInvariant(), Foreground = Ui.Accent,
                        FontSize = 11, FontWeight = FontWeight.Bold,
                    },
                    new TextBlock
                    {
                        Text = $"  {count}", Foreground = Ui.Dim, FontSize = 11,
                    },
                },
            },
        };
    }

    /// <summary>a board: its name, wrapping onto more lines when it is long,
    /// and its item count kept to the right of the first.</summary>
    static Control Line(BoardRow r)
    {
        var count = new TextBlock { Text = $"   {r.Board!.Items.Count}", Foreground = Ui.Dim };
        DockPanel.SetDock(count, Dock.Right);
        return new DockPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            Children = { count, new TextBlock { Text = r.Board.Name, TextWrapping = TextWrapping.Wrap } },
        };
    }

    /// <summary>a heading is never selected, even when pressed to drag it.</summary>
    void DropHeaders()
    {
        if (_list.SelectedItems is null) return;
        foreach (var item in _list.SelectedItems.Cast<object>().ToList())
        {
            int at = _list.Items.IndexOf(item);
            if (at >= 0 && at < _rows.Count && _rows[at].IsHeader) _list.SelectedItems.Remove(item);
        }
    }

    /// <summary>every board currently selected, headings ignored.</summary>
    public List<Board> Selected
    {
        get
        {
            var picked = new List<Board>();
            if (_list.SelectedItems is null) return picked;
            foreach (var item in _list.SelectedItems)
            {
                int at = _list.Items.IndexOf(item);
                if (at >= 0 && at < _rows.Count && _rows[at].Board is { } b) picked.Add(b);
            }
            return picked;
        }
    }

    Board? One => Selected is [var only] ? only : null;

    /// <summary>whether Home - and only Home - is selected.</summary>
    bool HomePicked => _list.SelectedItems is { Count: 1 } && _list.SelectedIndex == 0 && _rows is [{ IsHome: true }, ..];

    void Reflect()
    {
        int n = Selected.Count;
        _open.IsEnabled = n == 1 || HomePicked;
        _rename.IsEnabled = n == 1;
        _group.IsEnabled = n == 1;
        _delete.IsEnabled = n > 0;
        _delete.Content = n > 1 ? $"delete {n}" : "delete";
        foreach (var b in new[] { _open, _rename, _group, _delete })
            b.Foreground = b.IsEnabled ? Ui.Fore : Ui.Dim;
    }

    // ---- drag and drop, by hand ----

    /// <summary>whether a drag has moved anything yet, for Escape.</summary>
    public bool Dragging => _moved;

    void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressAt = e.GetPosition(_list);
        var row = RowAt(_pressAt);
        // Home stays where it is
        _dragBoard = row?.Board;
        _dragGroup = row is { IsHeader: true } ? row.Group : null;
        _moved = false;
        _before = _store.Boards.Select(b => (b, b.Group, b.Order)).ToList();
        _beforeGroups = [.. _store.GroupOrder];
    }

    void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_dragBoard is null && _dragGroup is null) return;
        var p = e.GetPosition(_list);
        if (!_moved && Math.Abs(p.Y - _pressAt.Y) < 6) return;     // not a drag yet
        if (RowAt(p) is not { } target || target.IsHome) return;    // nothing goes above Home

        if (_dragBoard is { } board) MoveBoard(board, target);
        else MoveGroup(_dragGroup!, target);
    }

    /// <summary>put a board where the row under the pointer is: at the top of
    /// a group whose heading it is over, otherwise beside the board it is
    /// over, in that board's group.</summary>
    public void MoveBoard(Board board, BoardRow target)
    {
        if (target.Board == board) return;
        int from = _rows.FindIndex(r => r.Board == board);
        int to = _rows.IndexOf(target);
        bool down = to > from;

        board.Group = target.Group;
        var siblings = _store.Boards.Where(b => b.Group == board.Group && b != board)
            .OrderBy(b => b.Order).ThenBy(b => b.Name, StringComparer.Ordinal).ToList();
        int index = target.IsHeader ? 0 : siblings.IndexOf(target.Board!) + (down ? 1 : 0);
        siblings.Insert(Math.Clamp(index, 0, siblings.Count), board);
        for (int i = 0; i < siblings.Count; i++) siblings[i].Order = i;

        _moved = true;
        Rebuild();
    }

    /// <summary>put a group where the group of the row under the pointer is.
    /// Every group gets a place in the stored order from then on, so the
    /// order no longer depends on names.</summary>
    public void MoveGroup(string group, BoardRow target)
    {
        if (target.Group == group) return;
        var order = _store.Groups();
        bool down = order.IndexOf(target.Group) > order.IndexOf(group);
        order.Remove(group);
        order.Insert(order.IndexOf(target.Group) + (down ? 1 : 0), group);
        _store.GroupOrder.Clear();
        _store.GroupOrder.AddRange(order);

        _moved = true;
        Rebuild();
    }

    void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        bool moved = _moved;
        var before = _before;
        var beforeGroups = _beforeGroups;
        EndDrag();
        if (!moved || before is null) return;

        foreach (var (b, group, order) in before)
            if (b.Group != group || b.Order != order) _store.Save(b);
        if (beforeGroups is null || !beforeGroups.SequenceEqual(_store.GroupOrder)) _store.SaveGroups();
    }

    /// <summary>put everything back as it was before the drag.</summary>
    public void CancelDrag()
    {
        if (_moved && _before is not null)
        {
            foreach (var (b, group, order) in _before) { b.Group = group; b.Order = order; }
            _store.GroupOrder.Clear();
            _store.GroupOrder.AddRange(_beforeGroups ?? []);
            Rebuild();
        }
        EndDrag();
    }

    void EndDrag()
    {
        _dragBoard = null;
        _dragGroup = null;
        _moved = false;
        _before = null;
        _beforeGroups = null;
    }

    /// <summary>the row under a point, asked of the rows themselves: a heading
    /// is taller than a board row, so the list's height divided by the row
    /// count is not a row.</summary>
    BoardRow? RowAt(Point p)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_list.ContainerFromIndex(i) is not { } row) continue;
            var top = row.TranslatePoint(new Point(0, 0), _list);
            if (top is { } t && p.Y >= t.Y && p.Y < t.Y + row.Bounds.Height) return _rows[i];
        }
        return null;
    }

    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Escape: Close(); return true;
            case Key.Enter: Commit(); return true;
            // Home, from wherever: the way back to the map
            case Key.H: Close(); HomeRequested?.Invoke(); return true;
            case Key.C: CreateRequested?.Invoke(); return true;
            case Key.F2: if (One is { } r) RenameRequested?.Invoke(r); return true;
            case Key.F3: if (One is { } g) GroupRequested?.Invoke(g); return true;
            case Key.Delete:
                var picked = Selected;
                if (picked.Count > 0) DeleteRequested?.Invoke(picked);
                return true;
            case Key.Down: Step(1); return true;
            case Key.Up: Step(-1); return true;
            default: return false;
        }
    }

    /// <summary>the next board up or down, over any heading; past the last
    /// one it stays where it is.</summary>
    void Step(int delta)
    {
        for (int i = _list.SelectedIndex + delta; i >= 0 && i < _rows.Count; i += delta)
        {
            if (_rows[i].IsHeader) continue;        // Home is a stop, headings are not
            _list.SelectedItems?.Clear();
            _list.SelectedIndex = i;
            _list.ScrollIntoView(i);
            return;
        }
    }

    void Commit()
    {
        if (HomePicked) { Close(); HomeRequested?.Invoke(); return; }
        if (One is not { } b) return;
        Close();
        Open?.Invoke(b);
    }

    /// <summary>open it, or close it when it is open.</summary>
    public void Toggle()
    {
        if (Reveal.Showing(this)) Close();
        else Show();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Atlas;

/// <summary>a row in the boards panel: either a group heading or a board.</summary>
public sealed class BoardRow
{
    public string Group { get; init; } = "";
    public Board? Board { get; init; }
    public bool IsHeader => Board is null;

    public override string ToString() => IsHeader
        ? (Group.Length == 0 ? "ungrouped" : Group)
        : "   " + Board!.Name + "   (" + Board.Items.Count + ")";
}

/// <summary>boards, grouped, with buttons for what you can do to the ones you
/// have selected. it used to explain its keys in a wall of text; a button that
/// does the thing is easier to read than a sentence about a key.</summary>
public sealed class BoardOverlay : Border
{
    readonly BoardStore _store;
    readonly ListBox _list;
    readonly List<BoardRow> _rows = [];
    readonly Button _open, _rename, _group, _delete;
    Board? _dragging;
    Point _pressAt;

    public event Action<Board>? Open;
    public event Action? CreateRequested;
    public event Action<Board>? RenameRequested;
    public event Action<Board>? GroupRequested;
    public event Action<List<Board>>? DeleteRequested;

    public BoardOverlay(BoardStore store)
    {
        _store = store;
        IsVisible = false;
        Background = Ui.PanelBg;
        BorderBrush = Ui.Edge;
        BorderThickness = new Thickness(0, 0, 1, 0);
        Padding = new Thickness(12);
        Width = 340;
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
        };
        _list.DoubleTapped += (_, _) => Commit();
        _list.SelectionChanged += (_, _) => Reflect();
        _list.AddHandler(PointerPressedEvent, OnPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
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

        Child = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = "BOARDS", FontFamily = Ui.Mono, FontSize = 12,
                    Foreground = Ui.Accent, Margin = new Thickness(0, 0, 0, 8),
                },
                buttons,
                _list,
            },
        };
    }

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
        IsVisible = true;
    }

    public void Close() => IsVisible = false;

    public void Rebuild()
    {
        var keep = Selected;
        _rows.Clear();

        var groups = _store.Boards.Select(b => b.Group).Distinct()
            .OrderBy(g => g.Length == 0 ? "" : "1" + g, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            _rows.Add(new BoardRow { Group = group });
            foreach (var b in _store.Boards.Where(x => x.Group == group)
                         .OrderBy(x => x.Order).ThenBy(x => x.Name, StringComparer.Ordinal))
                _rows.Add(new BoardRow { Group = group, Board = b });
        }

        _list.ItemsSource = _rows.Select(r => r.ToString()).ToList();
        _list.SelectedItems?.Clear();
        foreach (var b in keep)
        {
            int at = _rows.FindIndex(r => r.Board == b);
            if (at >= 0) _list.SelectedItems?.Add(_list.Items[at]);
        }
        if (_list.SelectedItems is { Count: 0 })
        {
            int first = _rows.FindIndex(r => !r.IsHeader);
            if (first >= 0) _list.SelectedIndex = first;
        }
        Reflect();
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

    void Reflect()
    {
        int n = Selected.Count;
        _open.IsEnabled = n == 1;
        _rename.IsEnabled = n == 1;
        _group.IsEnabled = n == 1;
        _delete.IsEnabled = n > 0;
        _delete.Content = n > 1 ? $"delete {n}" : "delete";
        foreach (var b in new[] { _open, _rename, _group, _delete })
            b.Foreground = b.IsEnabled ? Ui.Fore : Ui.Dim;
    }

    // ---- drag and drop, by hand: the rows are plain strings, so there are no
    // item containers for avalonia's DragDrop to hang off ----

    void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressAt = e.GetPosition(_list);
        _dragging = RowAt(_pressAt)?.Board;
    }

    void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var from = _dragging;
        _dragging = null;
        if (from is null) return;

        var at = e.GetPosition(_list);
        if (Math.Abs(at.Y - _pressAt.Y) < 6) return;        // a click, not a drag

        var target = RowAt(at);
        if (target is null || target.Board == from) return;

        from.Group = target.Group;
        var siblings = _store.Boards.Where(b => b.Group == from.Group && b != from)
            .OrderBy(b => b.Order).ToList();
        int index = target.IsHeader ? 0 : siblings.IndexOf(target.Board!) + (at.Y > _pressAt.Y ? 1 : 0);
        siblings.Insert(Math.Clamp(index, 0, siblings.Count), from);
        for (int i = 0; i < siblings.Count; i++) siblings[i].Order = i;

        foreach (var b in siblings) _store.Save(b);
        Rebuild();
    }

    BoardRow? RowAt(Point p)
    {
        if (_rows.Count == 0) return null;
        // rows are a uniform height, so the position maps straight to an index
        double h = _list.Bounds.Height / Math.Max(1, _rows.Count);
        int i = (int)(p.Y / Math.Max(1, h));
        return i >= 0 && i < _rows.Count ? _rows[i] : null;
    }

    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Escape: Close(); return true;
            case Key.Enter: Commit(); return true;
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

    void Step(int delta)
    {
        if (_rows.Count == 0) return;
        int i = Math.Clamp(_list.SelectedIndex + delta, 0, _rows.Count - 1);
        while (i > 0 && i < _rows.Count - 1 && _rows[i].IsHeader) i += delta > 0 ? 1 : -1;
        _list.SelectedItems?.Clear();
        _list.SelectedIndex = Math.Clamp(i, 0, _rows.Count - 1);
        _list.ScrollIntoView(_list.SelectedIndex);
    }

    void Commit()
    {
        if (One is not { } b) return;
        Close();
        Open?.Invoke(b);
    }
}

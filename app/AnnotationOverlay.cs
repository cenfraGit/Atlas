using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Atlas;

/// <summary>every annotation in the repo, with how well each one still anchors.
/// the orphan list the design called for is just this, sorted worst first.</summary>
public sealed class AnnotationOverlay : Border
{
    readonly AnnotationStore _store;
    readonly Scene _scene;
    readonly ListBox _list;
    readonly TextBlock _hint;
    readonly Button _keep;
    List<Annotation> _rows = [];

    public event Action<Annotation>? Chosen;
    public event Action<Annotation>? EditRequested;

    /// <summary>move every selected note between showing everywhere and
    /// showing on the current board only.</summary>
    public event Action<IReadOnlyList<Annotation>, bool>? ScopeRequested;

    /// <summary>the board the list is being read from, for naming the button
    /// and for saying which board a local note belongs to. Null on the map.</summary>
    public (string Id, string Name)? OnBoard;

    public AnnotationOverlay(AnnotationStore store, Scene scene)
    {
        _store = store;
        _scene = scene;
        IsVisible = false;
        Background = Ui.PanelBg;
        BorderBrush = Ui.Edge;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10);
        Width = 760;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 60, 0, 0);

        _hint = new TextBlock
        {
            FontFamily = Ui.Mono, FontSize = 11, Foreground = Ui.Dim,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _list = new ListBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            MaxHeight = 460, FontFamily = Ui.Mono, FontSize = 12,
            Foreground = Ui.Fore,
            // several notes becoming a board's own is one decision, so it
            // takes one gesture rather than one per note
            SelectionMode = SelectionMode.Multiple,
        };
        _list.DoubleTapped += (_, _) => Commit();

        // the same buttons the boards panel grew: a key you have to know about
        // is not a way to delete something
        _keep = Make("keep on this board", () => Scope(local: true));
        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                Make("go", Commit),
                Make("edit", () => { if (Current is { } a) EditRequested?.Invoke(a); }),
                _keep,
                Make("show everywhere", () => Scope(local: false)),
                Make("delete", Remove),
            },
        };
        Child = new StackPanel { Children = { _hint, buttons, _list } };
    }

    static Button Make(string text, Action run)
    {
        var b = new Button
        {
            Content = text,
            FontFamily = Ui.Mono,
            FontSize = 11,
            Padding = new Thickness(9, 3),
            Margin = new Thickness(0, 0, 4, 0),
            Background = Brushes.Transparent,
            Foreground = Ui.Fore,
            BorderThickness = new Thickness(1),
            BorderBrush = Ui.Edge,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        b.Click += (_, _) => run();
        return b;
    }

    Annotation? Current =>
        _list.SelectedIndex >= 0 && _list.SelectedIndex < _rows.Count ? _rows[_list.SelectedIndex] : null;

    /// <summary>everything ticked, or just the one under the cursor.</summary>
    List<Annotation> Selected()
    {
        var picked = _list.SelectedItems?.Count > 0
            ? _list.SelectedItems.Cast<object>().Select(o => _list.Items.IndexOf(o))
                .Where(i => i >= 0 && i < _rows.Count).Select(i => _rows[i]).ToList()
            : [];
        if (picked.Count == 0 && Current is { } one) picked.Add(one);
        return picked;
    }

    void Scope(bool local)
    {
        var picked = Selected();
        if (picked.Count == 0) return;
        ScopeRequested?.Invoke(picked, local);
        Rebuild();
    }

    /// <summary>after an edit elsewhere, so the list is not showing old text.</summary>
    public void Refresh()
    {
        if (!IsVisible) return;
        int at = _list.SelectedIndex;
        Rebuild();
        if (_rows.Count > 0) _list.SelectedIndex = Math.Clamp(at, 0, _rows.Count - 1);
    }

    public void Open()
    {
        _scene.EnsureAllAnchored();
        Rebuild();
        IsVisible = true;
        if (_rows.Count > 0) _list.SelectedIndex = 0;
    }

    public void Close() => IsVisible = false;

    static int Severity(AnchorKind k) => k switch
    {
        AnchorKind.Orphan => 0,
        AnchorKind.Drifted => 1,
        AnchorKind.Context => 2,
        _ => 3,
    };

    void Rebuild()
    {
        // anything that needs a human decision floats to the top
        _rows = _store.Annotations
            .OrderBy(a => Severity(KindOf(a)))
            .ThenBy(a => a.File, StringComparer.Ordinal)
            .ToList();

        _list.ItemsSource = _rows.Select(a =>
        {
            var kind = KindOf(a);
            var tag = kind switch
            {
                AnchorKind.Orphan => "ORPHAN ",
                AnchorKind.Drifted => "DRIFTED",
                AnchorKind.Context => "moved  ",
                _ => "       ",
            };
            var name = a.File[(a.File.LastIndexOf('/') + 1)..];
            var where = a.Symbol is null ? name : $"{name}  {a.Symbol}";
            // a note that only shows on one board has to say so, or the list
            // is a list of notes you cannot find
            var scope = a.Global ? "     " : "board";
            return $"{tag} {scope}  {Trim(a.Text, 46)}   -  {where}";
        }).ToList();

        int bad = _rows.Count(a => Severity(KindOf(a)) <= 1);
        int local = _rows.Count(a => !a.Global);

        _keep.IsEnabled = OnBoard is not null;
        _keep.Content = OnBoard is { } b ? $"keep on {Trim(b.Name, 18).TrimEnd()}" : "keep on this board";

        _hint.Text = _rows.Count == 0
            ? "no annotations yet.  write one on a board: right click a line of code"
            : $"{_rows.Count} annotations, {bad} needing attention, {local} kept to one board";
    }

    /// <summary>unresolved files have not been read yet, so treat them as fine
    /// rather than shouting orphan at something we simply have not looked at.</summary>
    AnchorKind KindOf(Annotation a)
    {
        // every scope, not just what is visible here: you cannot repair a
        // note the list refuses to show you
        foreach (var (candidate, anchor) in _scene.AllAnchorsFor(a.File))
            if (ReferenceEquals(candidate, a)) return anchor.Kind;
        return AnchorKind.Symbol;
    }

    static string Trim(string s, int n) =>
        s.Length <= n ? s.PadRight(n) : s[..(n - 1)] + "…";

    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Escape: Close(); return true;
            case Key.Enter: Commit(); return true;
            case Key.Delete: Remove(); return true;
            case Key.Down: Move(1); return true;
            case Key.Up: Move(-1); return true;
            case Key.G: Scope(local: false); return true;
            case Key.K: Scope(local: true); return true;
            default: return false;
        }
    }

    void Move(int delta)
    {
        if (_rows.Count == 0) return;
        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + delta, 0, _rows.Count - 1);
        _list.ScrollIntoView(_list.SelectedIndex);
    }

    void Commit()
    {
        int i = _list.SelectedIndex;
        if (i < 0 || i >= _rows.Count) return;
        var a = _rows[i];
        Close();
        Chosen?.Invoke(a);
    }

    void Remove()
    {
        int i = _list.SelectedIndex;
        if (i < 0 || i >= _rows.Count) return;
        var a = _rows[i];
        _store.Annotations.Remove(a);
        _store.Save();
        _scene.Reanchor(a.File);
        Rebuild();
        if (_rows.Count > 0) _list.SelectedIndex = Math.Min(i, _rows.Count - 1);
    }
}

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
    List<Annotation> _rows = [];

    public event Action<Annotation>? Chosen;
    public event Action<Annotation>? EditRequested;

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
        };
        _list.DoubleTapped += (_, _) => Commit();

        // the same buttons the boards panel grew: a key you have to know about
        // is not a way to delete something
        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                Make("go", Commit),
                Make("edit", () => { if (Current is { } a) EditRequested?.Invoke(a); }),
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
            return $"{tag}  {Trim(a.Text, 52)}   -  {where}";
        }).ToList();

        int bad = _rows.Count(a => Severity(KindOf(a)) <= 1);
        _hint.Text = _rows.Count == 0
            ? "no annotations yet.  write one on a board: right click a line of code"
            : $"{_rows.Count} annotations, {bad} needing attention";
    }

    /// <summary>unresolved files have not been read yet, so treat them as fine
    /// rather than shouting orphan at something we simply have not looked at.</summary>
    AnchorKind KindOf(Annotation a)
    {
        foreach (var (candidate, anchor) in _scene.AnchorsFor(a.File))
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

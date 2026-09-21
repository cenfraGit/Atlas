using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Atlas;

/// <summary>a one-line text prompt, for naming a bookmark or a tour.</summary>
public sealed class PromptOverlay : Border
{
    readonly TextBlock _label;
    readonly TextBox _box;
    Action<string>? _done;

    public PromptOverlay()
    {
        Reveal.Attach(this);
        Background = Ui.PanelBg;
        BorderBrush = Ui.Edge;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10);
        Width = 520;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 60, 0, 0);

        _label = new TextBlock
        {
            FontFamily = Ui.Mono, FontSize = 11,
            Foreground = Ui.Dim, Margin = new Thickness(0, 0, 0, 6),
        };
        _box = new TextBox
        {
            FontFamily = Ui.Mono, FontSize = 14,
            Foreground = Ui.Fore, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), CaretBrush = Ui.Accent,
        };
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Enter)
            {
                var text = (_box.Text ?? "").Trim();
                var cb = _done;
                Close();
                if (text.Length > 0) cb?.Invoke(text);
                e.Handled = true;
            }
        };
        Child = new StackPanel { Children = { _label, _box } };
    }

    public void Ask(string label, string initial, Action<string> done)
    {
        _label.Text = label;
        _box.Text = initial;
        _done = done;
        Reveal.Show(this);
        // see SearchOverlay.Open: the hotkey's character is still in flight
        Dispatcher.UIThread.Post(() => { _box.SelectAll(); _box.Focus(); },
            DispatcherPriority.Background);
    }

    public void Close()
    {
        Reveal.Hide(this);
        _done = null;
    }
}

/// <summary>list of tours and bookmarks. enter plays a tour or flies to a
/// bookmark, delete removes the selected one.</summary>
public sealed class BookmarkOverlay : Border
{
    readonly BookmarkStore _store;
    readonly Scene _scene;
    readonly ListBox _list;
    readonly TextBlock _hint;

    // parallel to the list rows: a tour, or a bookmark
    readonly List<(Tour? Tour, Bookmark? Mark)> _rows = [];

    public event Action<Bookmark>? FlyTo;
    public event Action<Tour>? Play;

    public BookmarkOverlay(BookmarkStore store, Scene scene)
    {
        _store = store;
        _scene = scene;
        Reveal.Attach(this);
        Background = Ui.PanelBg;
        BorderBrush = Ui.Edge;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10);
        Width = 620;
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

        Child = new StackPanel { Children = { _hint, _list } };
    }

    public void Open()
    {
        Rebuild();
        Reveal.Show(this);
        if (_rows.Count > 0) _list.SelectedIndex = 0;
    }

    public void Close() => Reveal.Hide(this);

    void Rebuild()
    {
        _rows.Clear();
        var labels = new List<string>();

        foreach (var t in _store.Tours)
        {
            _rows.Add((t, null));
            labels.Add($"[tour]  {t.Name}  ({t.Stops.Count} stops)");
        }
        foreach (var b in _store.Bookmarks)
        {
            _rows.Add((null, b));
            var where = b.File is null
                ? "map view"
                : _scene.IndexOfPath(b.File) < 0 ? $"MISSING  {b.File}"
                : b.Line >= 0 ? $"{b.File}:{b.Line + 1}" : b.File;
            labels.Add($"        {b.Name}   -  {where}");
        }

        _list.ItemsSource = labels;
        _hint.Text = _rows.Count == 0
            ? "nothing saved yet.  M: save a bookmark   R: record a tour"
            : "enter: go   delete: remove   esc: close";
    }

    /// <summary>returns true when the key was for this panel.</summary>
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
        var (tour, mark) = _rows[i];
        Close();
        if (tour is not null) Play?.Invoke(tour);
        else if (mark is not null) FlyTo?.Invoke(mark);
    }

    void Remove()
    {
        int i = _list.SelectedIndex;
        if (i < 0 || i >= _rows.Count) return;
        var (tour, mark) = _rows[i];
        if (tour is not null) _store.Tours.Remove(tour);
        else if (mark is not null)
        {
            _store.Bookmarks.Remove(mark);
            // a tour must not keep a stop that no longer exists
            foreach (var t in _store.Tours) t.Stops.Remove(mark.Id);
        }
        _store.Save();
        Rebuild();
        if (_rows.Count > 0) _list.SelectedIndex = Math.Min(i, _rows.Count - 1);
    }
}

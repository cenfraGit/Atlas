using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Atlas;

/// <summary>the results of searching what the files say.
///
/// Wider and taller than the path search, because a row here is a line of
/// source and a line of source is long. Moving the selection flies to the
/// match rather than waiting for Enter: the point of a search across a map
/// is watching where in the repo the answers are, and that only works if the
/// camera keeps up with the list.</summary>
public sealed class GrepOverlay : Border
{
    readonly TextBox _box;
    readonly ListBox _list;
    readonly TextBlock _hint;
    List<Found> _found = [];

    /// <summary>a match to go and look at.</summary>
    public event Action<Found>? Picked;

    /// <summary>run the search. The overlay does not know how to read files
    /// or which thread to do it on; it asks, and is handed the answers.</summary>
    public event Action<string>? Requested;

    public GrepOverlay()
    {
        Reveal.Attach(this);
        Background = Ui.PanelBg;
        BorderBrush = Ui.Edge;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10);
        Width = 900;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 60, 0, 0);

        _box = new TextBox
        {
            FontFamily = Ui.Mono, FontSize = 14, Foreground = Ui.Fore,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Watermark = "find in files",
        };
        // on the box, not on the canvas. The box has focus while you are
        // typing, so a key routed through the canvas never arrives - the
        // arrows moved the caret and the list sat still
        _box.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter: e.Handled = true; Requested?.Invoke(_box.Text ?? ""); break;
                case Key.Down: e.Handled = true; Move(1); break;
                case Key.Up: e.Handled = true; Move(-1); break;
            }
        };

        _hint = new TextBlock
        {
            FontFamily = Ui.Mono, FontSize = 11, Foreground = Ui.Dim,
            Margin = new Thickness(0, 4, 0, 6),
        };
        _list = new ListBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            MaxHeight = 520, FontFamily = Ui.Mono, FontSize = 12, Foreground = Ui.Fore,
        };
        _list.DoubleTapped += (_, _) => Go();

        Child = new StackPanel { Children = { _box, _hint, _list } };
    }

    public void Open(string where)
    {
        _found = [];
        _list.ItemsSource = new List<string>();
        _hint.Text = $"searching {where}   -   enter: search   up/down: walk the matches   esc: close";
        Reveal.Show(this);

        // the ctrl+F is still in flight as text input; take focus after it
        // has been delivered, the same as the path search does
        Dispatcher.UIThread.Post(() => { _box.SelectAll(); _box.Focus(); },
            DispatcherPriority.Background);
    }

    public void Close() => Reveal.Hide(this);

    public void Searching() => _hint.Text = "searching...";

    public void Show(List<Found> found, string query, string where, bool capped)
    {
        _found = found;
        int width = found.Count == 0 ? 0 : Math.Min(52, found.Max(f => Label(f).Length));
        _list.ItemsSource = found.Select(f => $"{Pad(Label(f), width)}  {f.Text}").ToList();

        _hint.Text = found.Count == 0
            ? $"no match for \"{query}\" in {where}"
            : $"{found.Count}{(capped ? "+" : "")} in {Grep.FilesIn(found)} file" +
              $"{(Grep.FilesIn(found) == 1 ? "" : "s")}   -   up/down: walk them   esc: close";

        if (found.Count > 0) _list.SelectedIndex = 0;
    }

    static string Label(Found f)
    {
        var name = f.Path[(f.Path.LastIndexOf('/') + 1)..];
        return $"{name}:{f.Line + 1}";
    }

    static string Pad(string s, int width) =>
        s.Length <= width ? s.PadRight(width) : s[..(width - 1)] + "…";

    /// <summary>for a key that reached the canvas instead - the box holds
    /// focus in practice, so this is the fallback rather than the path.
    /// Escape is not here: the window peels it off the layer stack.</summary>
    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Down: Move(1); return true;
            case Key.Up: Move(-1); return true;
            default: return false;
        }
    }

    void Move(int by)
    {
        if (_found.Count == 0) return;
        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + by, 0, _found.Count - 1);
        _list.ScrollIntoView(_list.SelectedIndex);
        Go();
    }

    void Go()
    {
        int i = _list.SelectedIndex;
        if (i >= 0 && i < _found.Count) Picked?.Invoke(_found[i]);
    }
}

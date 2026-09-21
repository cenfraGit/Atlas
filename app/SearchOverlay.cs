using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Atlas;

/// <summary>the search box floating over the canvas. a real avalonia TextBox,
/// so text editing, selection and IME come for free.</summary>
public sealed class SearchOverlay : Border
{
    static readonly IBrush PanelBg = new SolidColorBrush(Color.FromArgb(0xF2, 0x07, 0x12, 0x1c));
    static readonly IBrush Edge = new SolidColorBrush(Color.FromRgb(0x1b, 0x4c, 0x66));
    static readonly IBrush Fore = new SolidColorBrush(Color.FromRgb(0x9f, 0xd4, 0xea));
    static readonly IBrush Dim = new SolidColorBrush(Color.FromRgb(0x35, 0x70, 0x8f));
    static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0xff, 0xd1, 0x66));

    readonly Scene _scene;
    readonly TextBox _box;
    readonly ListBox _list;
    List<Hit> _hits = [];

    public event Action<int>? Chosen;

    public SearchOverlay(Scene scene)
    {
        _scene = scene;
        Reveal.Attach(this);
        Background = PanelBg;
        BorderBrush = Edge;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(8);
        Width = 620;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 60, 0, 0);

        _box = new TextBox
        {
            FontFamily = Ui.Mono,
            FontSize = 14,
            Foreground = Fore,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Watermark = "find a file",
            CaretBrush = Accent,
        };
        _box.TextChanged += (_, _) => Requery();
        _box.KeyDown += OnBoxKey;

        _list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            MaxHeight = 420,
            FontFamily = Ui.Mono,
            FontSize = 12,
            Foreground = Dim,
        };
        _list.DoubleTapped += (_, _) => Commit();

        Child = new StackPanel { Children = { _box, _list } };
    }

    public void Open()
    {
        Reveal.Show(this);
        Requery();
        // the '/' that opened this panel is still in flight as text input; take
        // focus after it has been delivered, so it does not land in the box
        Dispatcher.UIThread.Post(() => { _box.SelectAll(); _box.Focus(); },
            DispatcherPriority.Background);
    }

    public void Close()
    {
        Reveal.Hide(this);
    }

    void Requery()
    {
        _hits = Search.Run(_scene.Data, _box.Text ?? "");
        _list.ItemsSource = _hits.Select(h => h.Path).ToList();
        if (_hits.Count > 0) _list.SelectedIndex = 0;
    }

    void OnBoxKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Enter:
                Commit();
                e.Handled = true;
                break;
            // arrows move the result selection while the caret stays in the box
            case Key.Down when _hits.Count > 0:
                _list.SelectedIndex = Math.Min(_list.SelectedIndex + 1, _hits.Count - 1);
                _list.ScrollIntoView(_list.SelectedIndex);
                e.Handled = true;
                break;
            case Key.Up when _hits.Count > 0:
                _list.SelectedIndex = Math.Max(_list.SelectedIndex - 1, 0);
                _list.ScrollIntoView(_list.SelectedIndex);
                e.Handled = true;
                break;
        }
    }

    void Commit()
    {
        int i = _list.SelectedIndex;
        if (i < 0 || i >= _hits.Count) return;
        Close();
        Chosen?.Invoke(_hits[i].Index);
    }
}

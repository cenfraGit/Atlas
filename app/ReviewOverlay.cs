using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Atlas;

/// <summary>pull requests - open ones from GitHub, merged ones recovered from
/// their merge commits - or branches, newest first.</summary>
public sealed class ReviewOverlay : Border
{
    readonly ListBox _list;
    readonly TextBlock _hint;
    List<ReviewTarget> _targets = [];

    public event Action<ReviewTarget>? Chosen;

    public ReviewOverlay()
    {
        Reveal.Attach(this);
        Background = Ui.PanelBg;
        BorderBrush = Ui.Edge;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10);
        Width = 780;
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

    public void Show(List<ReviewTarget> targets, string branch, string what)
    {
        _targets = targets;
        int width = targets.Count == 0 ? 0 : Math.Min(46, targets.Max(t => t.Label.Length));
        _list.ItemsSource = targets.Select(t => $"{Pad(t.Label, width)}   {t.Detail}").ToList();
        _hint.Text = targets.Count == 0
            ? $"no {what} found"
            : $"{what}   -   on {branch}   -   enter: open   esc: close" +
              "\nthen  ]  and  [  step through its commits";
        if (targets.Count > 0) _list.SelectedIndex = 0;
        Reveal.Show(this);
    }

    /// <summary>open straight away while the list is read. Walking the
    /// history for merged pull requests takes a moment on a long one, and a
    /// panel that only appeared once it was done read as a key that did
    /// nothing.</summary>
    public void Loading(string what)
    {
        _targets = [];
        _list.ItemsSource = new List<string>();
        _hint.Text = $"reading {what}...";
        Reveal.Show(this);
    }

    static string Pad(string s, int width) =>
        s.Length <= width ? s.PadRight(width) : s[..(width - 1)] + "…";

    public void Close() => Reveal.Hide(this);

    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Escape: Close(); return true;
            case Key.Enter: Commit(); return true;
            case Key.Down: Move(1); return true;
            case Key.Up: Move(-1); return true;
            default: return false;
        }
    }

    void Move(int delta)
    {
        if (_targets.Count == 0) return;
        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + delta, 0, _targets.Count - 1);
        _list.ScrollIntoView(_list.SelectedIndex);
    }

    void Commit()
    {
        int i = _list.SelectedIndex;
        if (i < 0 || i >= _targets.Count) return;
        var target = _targets[i];
        Close();
        Chosen?.Invoke(target);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Atlas;

/// <summary>the commits of whatever is being reviewed, down the right hand
/// side. this is where the review's details live now: a scrolling log line at
/// the bottom of the canvas was impossible to follow.</summary>
public sealed class CommitsPanel : Border
{
    readonly TextBlock _title = Label(13, Ui.Accent);
    readonly TextBlock _summary = Label(11, Ui.Dim);
    readonly TextBlock _note = Label(11, Ui.Dim);
    readonly TextBlock _detail = Label(11, Ui.Fore);
    readonly ListBox _list;

    List<CommitInfo> _commits = [];

    /// <summary>-1 is the whole target, 0..n a single commit.</summary>
    public event Action<int>? Picked;

    static TextBlock Label(double size, IBrush brush) => new()
    {
        FontFamily = Ui.Mono, FontSize = size, Foreground = brush,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
    };

    public CommitsPanel()
    {
        IsVisible = false;
        Background = Ui.PanelBg;
        BorderBrush = Ui.Edge;
        BorderThickness = new Thickness(1, 0, 0, 0);
        Padding = new Thickness(12);
        Width = 380;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Stretch;

        _list = new ListBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            FontFamily = Ui.Mono, FontSize = 12, Foreground = Ui.Fore,
        };
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedIndex >= 0) Picked?.Invoke(_list.SelectedIndex - 1);
        };

        Child = new DockPanel
        {
            Children =
            {
                Dock(_title, Avalonia.Controls.Dock.Top),
                Dock(_summary, Avalonia.Controls.Dock.Top),
                Dock(_note, Avalonia.Controls.Dock.Top),
                Dock(new Legend(), Avalonia.Controls.Dock.Bottom),
                Dock(_detail, Avalonia.Controls.Dock.Bottom),
                _list,
            },
        };
    }

    static Control Dock(Control c, Dock side)
    {
        DockPanel.SetDock(c, side);
        return c;
    }

    public void Show(string label, List<CommitInfo> commits)
    {
        _title.Text = label;
        _commits = commits;

        var rows = new List<string> { $"all {commits.Count} commit{(commits.Count == 1 ? "" : "s")}" };
        rows.AddRange(commits.Select((c, i) => $"{i + 1,2}. {c.Short}  {Trim(c.Subject, 30)}"));
        _list.ItemsSource = rows;
        IsVisible = true;
    }

    public void Close() => IsVisible = false;

    /// <summary>reflect a change made with the bracket keys, without looping
    /// back into Picked.</summary>
    public void Sync(int commitAt, ChangeSet set, int placed, bool onSnapshot)
    {
        if (_list.SelectedIndex != commitAt + 1) _list.SelectedIndex = commitAt + 1;

        _summary.Text = $"{set.Files.Count} files   +{set.TotalAdded}  -{set.TotalRemoved}";
        _note.Text = placed >= set.Files.Count
            ? onSnapshot ? "tree at this commit" : ""
            : placed == 0
                ? "none of these paths are in this tree"
                : $"{placed} of them are on the map" + (onSnapshot ? "   (tree at this commit)" : "");

        _detail.Text = commitAt >= 0 && commitAt < _commits.Count
            ? $"{_commits[commitAt].Short}\n{_commits[commitAt].Author}\n" +
              $"{_commits[commitAt].When:yyyy-MM-dd HH:mm}\n\n{_commits[commitAt].Subject}"
            : "[ and ] step through the commits";
    }

    static string Trim(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}

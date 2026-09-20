using Avalonia.Controls;
using Avalonia.Media;

namespace Atlas;

/// <summary>builds the secondary-click menu. kept apart from the view so the
/// actions read as a list of what you can do to a selection.</summary>
public static class ContextActions
{
    public static MenuItem Item(string header, Action action, bool enabled = true)
    {
        var item = new MenuItem
        {
            Header = header,
            FontFamily = Ui.Mono,
            FontSize = 12,
            IsEnabled = enabled,
        };
        item.Click += (_, _) => action();
        return item;
    }

    public static MenuItem Submenu(string header, IEnumerable<MenuItem> children)
    {
        var kids = children.ToList();
        var item = new MenuItem
        {
            Header = header,
            FontFamily = Ui.Mono,
            FontSize = 12,
            ItemsSource = kids,
            IsEnabled = kids.Count > 0,
        };
        return item;
    }

    public static MenuItem Separator() => new() { Header = "-" };
}

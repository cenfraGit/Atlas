using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Atlas;

/// <summary>the actions along the bottom, as buttons rather than a painted
/// reminder of which keys exist. each one runs exactly what its key runs.</summary>
public sealed class HintBar : Border
{
    readonly StackPanel _row;

    public HintBar()
    {
        Background = Ui.PanelBg;
        BorderBrush = Ui.Edge;
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(8, 3);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Bottom;

        _row = new StackPanel { Orientation = Orientation.Horizontal };
        Child = _row;
    }

    /// <summary>replace the buttons. called whenever the mode changes, which is
    /// rare enough that rebuilding is simpler than diffing.</summary>
    public void Set(IEnumerable<(string Label, string Key, Action Run)> actions)
    {
        _row.Children.Clear();
        foreach (var (label, key, run) in actions)
        {
            var button = new Button
            {
                Content = key.Length > 0 ? $"{label}  {key}" : label,
                FontFamily = Ui.Mono,
                FontSize = 11,
                Padding = new Thickness(9, 3),
                Margin = new Thickness(0, 0, 3, 0),
                Background = Brushes.Transparent,
                Foreground = Ui.Dim,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            button.PointerEntered += (_, _) => button.Foreground = Ui.Accent;
            button.PointerExited += (_, _) => button.Foreground = Ui.Dim;
            button.Click += (_, _) => run();
            _row.Children.Add(button);
        }
    }
}

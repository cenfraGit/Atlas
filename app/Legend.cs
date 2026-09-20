using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Atlas;

/// <summary>what the colours mean while reviewing. lives in the commits panel
/// because that is where you are looking when you need it.</summary>
public sealed class Legend : StackPanel
{
    public Legend()
    {
        Orientation = Orientation.Vertical;
        Margin = new Thickness(0, 10, 0, 0);

        Add(Color.FromRgb(0x3f, 0xb9, 0x6a), "added lines");
        Add(Color.FromRgb(0xd9, 0x5c, 0x5c), "removed lines");
        Add(Color.FromRgb(0xb4, 0x8a, 0x5c), "a file with both");
        Add(Color.FromRgb(0x23, 0x2b, 0x35), "unchanged, dimmed");
    }

    void Add(Color color, string what)
    {
        Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 3),
            Children =
            {
                new Border
                {
                    Width = 14, Height = 9,
                    Background = new SolidColorBrush(color),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                },
                new TextBlock
                {
                    Text = what,
                    FontFamily = Ui.Mono, FontSize = 11,
                    Foreground = Ui.Dim,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        });
    }
}

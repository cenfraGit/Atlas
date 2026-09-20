using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Atlas;

/// <summary>the eraser's own settings.
///
/// It used to borrow the pen's panel, which offered it a colour - a setting
/// an eraser has no use for, sitting where its actual settings should have
/// been. Its mode had no control at all and no indicator, so the only sign
/// of which mode you were in was a message that had already scrolled past.
///
/// A tool with modes has to show which one it is in.</summary>
public sealed class EraserBar : Border
{
    readonly TextBlock _size;
    readonly Border _whole, _split;

    public event Action<int>? SizeStepped;
    public event Action<bool>? ModePicked;

    public EraserBar()
    {
        IsVisible = false;
        Background = Ui.PanelBg;
        BorderBrush = Ui.Edge;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(8, 5);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 52, 0, 0);

        _size = new TextBlock
        {
            FontFamily = Ui.Mono,
            FontSize = 11,
            Foreground = Ui.Dim,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        _size.PointerPressed += (_, e) => { e.Handled = true; SizeStepped?.Invoke(1); };

        _whole = Mode("whole element", () => ModePicked?.Invoke(false));
        _split = Mode("split stroke", () => ModePicked?.Invoke(true));

        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                _size,
                new Border { Width = 1, Background = Ui.Edge, Margin = new Thickness(4, 2) },
                _whole,
                _split,
            },
        };
    }

    static Border Mode(string label, Action click)
    {
        var chip = new Border
        {
            Padding = new Thickness(8, 3),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { FontFamily = Ui.Mono, FontSize = 11, Foreground = Ui.Dim, Text = label },
        };
        chip.PointerPressed += (_, e) => { e.Handled = true; click(); };
        return chip;
    }

    static void Light(Border chip, bool on)
    {
        chip.BorderBrush = on ? Ui.Accent : Brushes.Transparent;
        if (chip.Child is TextBlock t) t.Foreground = on ? Ui.Accent : Ui.Dim;
    }

    public void Reflect(bool visible, float radius, bool split)
    {
        IsVisible = visible;
        _size.Text = $"eraser {radius:0}   [ ]";
        Light(_whole, !split);
        Light(_split, split);
    }
}

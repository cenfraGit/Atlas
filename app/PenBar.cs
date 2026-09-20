using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Atlas;

/// <summary>the pen's colour and thickness, beside the board toolbar.
///
/// Shown only while the brush or the eraser is armed. A drawing tool's
/// settings are worth room on screen exactly while you are drawing and are
/// clutter the rest of the time, and there is no guessing what colour you are
/// about to draw in: the swatch you are using is the lit one.
///
/// The swatches also recolour whatever is picked, so the same control means
/// "draw in this" and "make that this" - which is what a selection makes it
/// mean anyway.</summary>
public sealed class PenBar : Border
{
    readonly StackPanel _row;
    readonly TextBlock _weight;
    readonly List<(string Hex, Border Chip)> _chips = [];

    /// <summary>a colour was chosen; null means the default.</summary>
    public event Action<string>? ColourPicked;
    public event Action<int>? WeightStepped;

    public PenBar((string Name, string Hex)[] colours)
    {
        IsVisible = false;
        Background = Ui.PanelBg;
        BorderBrush = Ui.Edge;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(8, 5);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 52, 0, 0);

        _row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        _weight = new TextBlock
        {
            FontFamily = Ui.Mono,
            FontSize = 11,
            Foreground = Ui.Dim,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        // the wheel over the number is the fastest way to find the weight you
        // want, and [ and ] do the same from the keyboard
        _weight.PointerPressed += (_, e) => { e.Handled = true; WeightStepped?.Invoke(1); };
        _row.Children.Add(_weight);
        _row.Children.Add(new Border { Width = 1, Background = Ui.Edge, Margin = new Thickness(4, 2) });

        foreach (var (name, hex) in colours)
        {
            var chip = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.Parse(hex)),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                Cursor = new Cursor(StandardCursorType.Hand),
                [ToolTip.TipProperty] = name,
            };
            chip.PointerPressed += (_, e) => { e.Handled = true; ColourPicked?.Invoke(hex); };
            _chips.Add((hex, chip));
            _row.Children.Add(chip);
        }

        Child = _row;
    }

    public void Reflect(bool visible, float weight, string? colour)
    {
        IsVisible = visible;
        _weight.Text = $"pen {weight:0.#}   [ ]";
        foreach (var (hex, chip) in _chips)
            chip.BorderBrush = string.Equals(hex, colour, StringComparison.OrdinalIgnoreCase)
                ? Ui.Fore
                : Brushes.Transparent;
    }
}

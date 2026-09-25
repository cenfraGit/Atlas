using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Atlas;

/// <summary>the way back to the map, top left.
///
/// Escape used to do this, which made it the one key that could not also mean
/// "cancel what I am doing" - and cancel is what you want from it far more
/// often on a board. So leaving got a button instead: leaving is a thing you
/// do rarely and deliberately, and a button is the only version of it that
/// announces itself. From the keyboard it is home in the workspace: tab, then
/// H.</summary>
public sealed class BackButton : Border
{
    readonly TextBlock _label;

    public event Action? Clicked;

    public BackButton()
    {
        Reveal.Attach(this, Reveal.Edge.Left);
        Background = Ui.PanelBg;
        BorderBrush = Ui.Edge;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(12, 6);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(10, 10, 0, 0);
        Cursor = new Cursor(StandardCursorType.Hand);

        _label = new TextBlock
        {
            FontFamily = Ui.Mono,
            FontSize = 12,
            Foreground = Ui.Fore,
            Text = "<  map",
        };
        Child = _label;

        PointerPressed += (_, e) => { e.Handled = true; Clicked?.Invoke(); };
        PointerEntered += (_, _) => _label.Foreground = Ui.Accent;
        PointerExited += (_, _) => _label.Foreground = Ui.Fore;
    }

    /// <summary>shown whenever there is somewhere to go back to, and told what
    /// it is going back from.</summary>
    public void Reflect(bool onBoard, string? what = null)
    {
        Reveal.Set(this, onBoard);
        _label.Text = what is null ? "<  map" : $"<  map     {what}";
    }
}

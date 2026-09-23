using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Atlas;

/// <summary>a one-line text prompt, for naming a board, a stop or a colour.</summary>
public sealed class PromptOverlay : Border
{
    readonly TextBlock _label;
    readonly TextBox _box;
    Action<string>? _done;

    public PromptOverlay()
    {
        Reveal.Attach(this);
        Background = Ui.PanelBg;
        BorderBrush = Ui.Edge;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10);
        Width = 520;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 60, 0, 0);

        _label = new TextBlock
        {
            FontFamily = Ui.Mono, FontSize = 11,
            Foreground = Ui.Dim, Margin = new Thickness(0, 0, 0, 6),
        };
        _box = new TextBox
        {
            FontFamily = Ui.Mono, FontSize = 14,
            Foreground = Ui.Fore, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), CaretBrush = Ui.Accent,
        };
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Enter)
            {
                var text = (_box.Text ?? "").Trim();
                var cb = _done;
                Close();
                if (text.Length > 0) cb?.Invoke(text);
                e.Handled = true;
            }
        };
        Child = new StackPanel { Children = { _label, _box } };
    }

    public void Ask(string label, string initial, Action<string> done)
    {
        _label.Text = label;
        _box.Text = initial;
        _done = done;
        Reveal.Show(this);
        // see SearchOverlay.Open: the hotkey's character is still in flight
        Dispatcher.UIThread.Post(() => { _box.SelectAll(); _box.Focus(); },
            DispatcherPriority.Background);
    }

    public void Close()
    {
        Reveal.Hide(this);
        _done = null;
    }
}

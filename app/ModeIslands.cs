using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Atlas;

/// <summary>two independent toggles at the top right. they are not modes of
/// one another: editing and wheel-zoom are unrelated, so they get their own
/// islands rather than sharing a row of radio buttons.</summary>
public sealed class ModeIslands : StackPanel
{
    readonly Island _edit;
    readonly Island _zoom;

    public event Action<bool>? EditChanged;
    public event Action<bool>? ZoomChanged;

    public ModeIslands()
    {
        Orientation = Orientation.Vertical;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 10, 12, 0);

        _edit = new Island("edit", "E", false);
        _zoom = new Island("zoom with wheel", "S", true);
        _edit.Toggled += v => EditChanged?.Invoke(v);
        _zoom.Toggled += v => ZoomChanged?.Invoke(v);

        Children.Add(_edit);
        Children.Add(_zoom);
    }

    public void Reflect(bool edit, bool zoom)
    {
        _edit.Set(edit);
        _zoom.Set(zoom);
    }

    sealed class Island : Border
    {
        readonly TextBlock _box;
        readonly TextBlock _label;
        bool _on;

        public event Action<bool>? Toggled;

        public Island(string label, string key, bool on)
        {
            _on = on;
            Background = Ui.PanelBg;
            BorderBrush = Ui.Edge;
            BorderThickness = new Thickness(1);
            Padding = new Thickness(9, 5);
            Margin = new Thickness(0, 0, 0, 6);
            HorizontalAlignment = HorizontalAlignment.Right;
            Cursor = new Cursor(StandardCursorType.Hand);

            _box = Text("", 12, Ui.Accent);
            _label = Text(label, 11, Ui.Dim);
            var hint = Text(key, 10, Ui.Edge);
            hint.Margin = new Thickness(8, 0, 0, 0);

            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { _box, _label, hint },
            };
            PointerPressed += (_, _) => { _on = !_on; Paint(); Toggled?.Invoke(_on); };
            Paint();
        }

        static TextBlock Text(string t, double size, IBrush brush) => new()
        {
            Text = t, FontFamily = Ui.Mono, FontSize = size,
            Foreground = brush, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };

        public void Set(bool on)
        {
            if (_on == on) return;
            _on = on;
            Paint();
        }

        void Paint()
        {
            _box.Text = _on ? "[x]" : "[ ]";
            _box.Foreground = _on ? Ui.Accent : Ui.Dim;
            _label.Foreground = _on ? Ui.Fore : Ui.Dim;
        }
    }
}

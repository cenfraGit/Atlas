using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Atlas;

/// <summary>independent toggles at the top right. They are not modes of one
/// another: wheel-zoom, editing and snapping are unrelated, so each is its
/// own box rather than one of a row of radio buttons.
///
/// Wheel-zoom first, because it applies everywhere. Edit and snap share the
/// island under it, and only on a board that can be edited: snapping means
/// nothing without editing, and neither means anything on the map.</summary>
public sealed class ModeIslands : StackPanel
{
    readonly Toggle _zoom;
    readonly Toggle _edit;
    readonly Toggle _snap;
    readonly Border _board;

    public event Action<bool>? EditChanged;
    public event Action<bool>? ZoomChanged;
    public event Action<bool>? SnapChanged;

    public ModeIslands()
    {
        Orientation = Orientation.Vertical;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 10, 12, 0);

        _zoom = new Toggle("zoom with wheel", "S", true);
        _edit = new Toggle("edit", "E", false);
        _snap = new Toggle("snap", "G", false);
        _zoom.Toggled += v => ZoomChanged?.Invoke(v);
        _edit.Toggled += v => EditChanged?.Invoke(v);
        _snap.Toggled += v => SnapChanged?.Invoke(v);

        Children.Add(Island(_zoom));
        Children.Add(_board = Island(_edit, _snap));
        _board.IsVisible = false;
    }

    public void Reflect(bool edit, bool zoom, bool snap, bool onBoard)
    {
        _zoom.Set(zoom);
        _edit.Set(edit);
        _snap.Set(snap);
        _board.IsVisible = onBoard;
    }

    /// <summary>whether the edit and snap island is showing. For the tests.</summary>
    public bool BoardTogglesShown => _board.IsVisible;

    /// <summary>a box round one or more toggles, a bar between each.</summary>
    static Border Island(params Toggle[] toggles)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < toggles.Length; i++)
        {
            if (i > 0) row.Children.Add(Toggle.Text("|", 11, Ui.Edge));
            row.Children.Add(toggles[i]);
        }
        return new Border
        {
            Background = Ui.PanelBg,
            BorderBrush = Ui.Edge,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 5),
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = row,
        };
    }

    sealed class Toggle : StackPanel
    {
        readonly TextBlock _box;
        readonly TextBlock _label;
        bool _on;

        public event Action<bool>? Toggled;

        public Toggle(string label, string key, bool on)
        {
            _on = on;
            Orientation = Orientation.Horizontal;
            Background = Brushes.Transparent;       // the gaps take clicks too
            Cursor = new Cursor(StandardCursorType.Hand);

            _box = Text("", 12, Ui.Accent);
            _label = Text(label, 11, Ui.Dim);
            var hint = Text(key, 10, Ui.Edge);
            hint.Margin = new Thickness(2, 0, 6, 0);
            Children.Add(_box);
            Children.Add(_label);
            Children.Add(hint);

            PointerPressed += (_, e) => { _on = !_on; Paint(); Toggled?.Invoke(_on); e.Handled = true; };
            Paint();
        }

        public static TextBlock Text(string t, double size, IBrush brush) => new()
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

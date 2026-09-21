using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Atlas;

/// <summary>what you can drop on a board, along the top while editing.</summary>
public sealed class BoardBar : Border
{
    readonly StackPanel _row;
    readonly Button _snap;
    readonly Dictionary<string, Button> _tools = [];

    public event Action<string>? Add;
    public event Action? ToggleSnap;

    public BoardBar()
    {
        Reveal.Attach(this);
        Background = Ui.PanelBg;
        BorderBrush = Ui.Edge;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(6, 4);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 10, 0, 0);

        _row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, kind) in new[]
                 {
                     ("note  N", "note"), ("rect  1", "shape"),
                     ("ellipse  2", "ellipse"), ("diamond  3", "diamond"),
                     ("label  4", "text"), ("arrow  Y", "arrow"),
                     ("brush  B", "brush"), ("eraser  X", "eraser"),
                     ("file  A", "file"),
                 })
        {
            var b = Make(label);
            b.Click += (_, _) => Add?.Invoke(kind);
            _tools[kind] = b;
            _row.Children.Add(b);
        }

        _snap = Make("snap  G");
        _snap.Click += (_, _) => ToggleSnap?.Invoke();
        _row.Children.Add(_snap);

        Child = _row;
    }

    static Button Make(string text) => new()
    {
        Content = text,
        FontFamily = Ui.Mono,
        FontSize = 11,
        Padding = new Thickness(10, 4),
        Margin = new Thickness(2, 0),
        Background = Brushes.Transparent,
        Foreground = Ui.Dim,
        BorderThickness = new Thickness(0),
        Cursor = new Cursor(StandardCursorType.Hand),
    };

    /// <summary>a tool that stays armed has to look armed, or you draw a
    /// stroke you did not mean to the next time you drag.</summary>
    public void Reflect(bool editing, bool snap, string? armed = null)
    {
        Reveal.Set(this, editing);
        _snap.Foreground = snap ? Ui.Accent : Ui.Dim;
        foreach (var (kind, button) in _tools)
            button.Foreground = kind == armed ? Ui.Accent : Ui.Dim;
    }
}

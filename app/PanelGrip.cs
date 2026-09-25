using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Atlas;

/// <summary>a grip down a side panel's inner edge that drags its width.
/// Board names, commit subjects and stop names are long, and a fixed panel
/// either wasted space or cut them off.</summary>
public static class PanelGrip
{
    public const double MinW = 240, MaxW = 900;

    /// <summary>wrap the panel's content with the grip laid over its inner
    /// edge - the right edge of a panel on the left, the left edge of one on
    /// the right. Call once the panel's Child is set.</summary>
    public static void Attach(Border panel, bool onLeft)
    {
        var pad = panel.Padding;
        var border = panel.BorderThickness;
        var grip = new Border
        {
            Width = 6,
            Background = Brushes.Transparent,
            HorizontalAlignment = onLeft ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
            // out through the padding, onto the panel's own edge
            Margin = onLeft
                ? new Thickness(0, -pad.Top, -(pad.Right + border.Right), -pad.Bottom)
                : new Thickness(-(pad.Left + border.Left), -pad.Top, 0, -pad.Bottom),
        };
        panel.MinWidth = MinW;

        // measured against the window, which does not move while the panel
        // it holds changes width
        double? fromX = null;
        double fromW = 0;
        grip.PointerPressed += (_, e) =>
        {
            fromX = e.GetPosition(TopLevel.GetTopLevel(panel)).X;
            fromW = panel.Width;
            e.Pointer.Capture(grip);
            e.Handled = true;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (fromX is not { } x0) return;
            double dx = e.GetPosition(TopLevel.GetTopLevel(panel)).X - x0;
            panel.Width = Math.Clamp(fromW + (onLeft ? dx : -dx), MinW, MaxW);
        };
        grip.PointerReleased += (_, e) => { fromX = null; e.Pointer.Capture(null); };

        // out of the panel before into the grid: a control has one parent
        var content = panel.Child!;
        panel.Child = null;
        panel.Child = new Grid { Children = { content, grip } };
    }
}

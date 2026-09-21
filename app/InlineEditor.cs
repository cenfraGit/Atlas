using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace Atlas;

/// <summary>typing into an item where it sits, rather than into a dialog.
///
/// A note used to be edited through the prompt at the top of the window: you
/// double-click something in the middle of the canvas, look somewhere else
/// entirely to type, and look back to find out what happened. On a board
/// that is a diagram rather than a document, the thing you are naming is
/// usually one of several similar boxes, and the dialog takes away the only
/// context that tells them apart.
///
/// So this is a real TextBox laid over the item, at the item's own position
/// and type size. The canvas stops drawing that item's words while it is up,
/// or the same text would be drawn twice slightly out of register.
///
/// Enter commits, shift+Enter starts a line, Escape puts back what was there
/// before, and clicking away commits - which is what clicking away means
/// everywhere else on the board.</summary>
public sealed class InlineEditor : Canvas
{
    readonly TextBox _box;
    Action<string>? _commit;
    string _before = "";
    bool _closing;
    (double X, double Y, double W, double H, double Size)? _placed;

    /// <summary>the item being edited, so the canvas knows to leave its own
    /// text off while the box is over it.</summary>
    public string? ItemId { get; private set; }

    public bool Editing => ItemId is not null;

    public InlineEditor()
    {
        // a Canvas with no background does not take clicks itself, so
        // everything outside the box still reaches the scene beneath
        Background = null;

        _box = new TextBox
        {
            IsVisible = false,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = Ui.Mono,
            Foreground = Ui.Fore,
            Background = new SolidColorBrush(Color.FromArgb(0xf2, 0x0d, 0x0b, 0x18)),
            BorderBrush = Ui.Accent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4),
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        // tunnel, so Enter is seen on the way *down* to the TextBox. A
        // TextBox with AcceptsReturn handles Enter itself and marks it
        // handled, so a bubbling handler is never called and Enter puts a
        // newline in the box instead of committing it - which is exactly
        // how it behaved, and the same reason the window watches Escape in
        // the tunnel phase
        _box.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        _box.LostFocus += (_, _) => Commit();
        Children.Add(_box);
    }

    /// <summary>start editing. <paramref name="commit"/> is called with the
    /// final text, once, whether it was Enter or a click somewhere else.
    ///
    /// The placement is an argument rather than a separate call afterwards,
    /// and that is the whole point of the signature. It used to be shown
    /// first and positioned on the next frame, which left one layout pass to
    /// measure a wrapping TextBox with no width against infinite space - and
    /// that killed the process outright, with no exception and no message,
    /// on every double click.</summary>
    public void Begin(string itemId, string text,
        double x, double y, double w, double h, double fontSize,
        Action<string>? commit = null)
    {
        ItemId = itemId;
        _before = text;
        _commit = commit;
        _closing = false;

        _box.Text = text;
        _placed = null;
        Place(x, y, w, h, fontSize);
        _box.IsVisible = true;

        // the double-click that opened this is still in flight; taking focus
        // now can hand it straight back. Same reasoning as the search box
        Dispatcher.UIThread.Post(() =>
        {
            if (!Editing) return;
            _box.Focus();
            _box.SelectAll();
        }, DispatcherPriority.Background);
    }

    /// <summary>put the box where the item is on screen. Called every frame:
    /// the canvas can pan and zoom underneath an open editor, and a box that
    /// stayed put would be editing one thing while pointing at another.</summary>
    public void Place(double x, double y, double w, double h, double fontSize)
    {
        if (!Editing) return;

        // only when something actually moved. This is called from inside the
        // render pass, and writing a layout property there - even the value
        // it already had - is how you get a render that invalidates layout
        // that schedules a render, every frame, for ever
        var now = (x, y, w, h, fontSize);
        if (_placed == now) return;
        _placed = now;

        // a board zoomed right out asks for a two pixel box in half point
        // type, and one zoomed right in asks for twelve thousand pixels.
        // Neither is a thing to hand a layout pass, and an editor you cannot
        // read is not an editor, so both ends are clamped
        _box.FontSize = Finite(fontSize, 14, 6, 200);
        _box.Width = Finite(w, 260, 60, 2000);
        _box.MinHeight = Finite(h, 32, 24, 1200);
        SetLeft(_box, Finite(x, 0, -10000, 10000));
        SetTop(_box, Finite(y, 0, -10000, 10000));
    }

    /// <summary>NaN and infinity are what a camera at a degenerate scale
    /// produces, and either one poisons a layout pass rather than failing
    /// where it was introduced.</summary>
    static double Finite(double v, double fallback, double lo, double hi) =>
        double.IsNaN(v) || double.IsInfinity(v) ? fallback : Math.Clamp(v, lo, hi);

    /// <summary>set the text as if it had been typed. For tests: driving real
    /// keystrokes through a headless window tests Avalonia's TextBox rather
    /// than anything here.</summary>
    internal void SetTextForTest(string text) => _box.Text = text;

    void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            // shift+Enter is a new line: a note is often two sentences, and
            // the alternative is a modifier for the common case instead
            case Key.Enter when !e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                e.Handled = true;
                Commit();
                break;

            case Key.Escape:
                e.Handled = true;
                Cancel();
                break;
        }
    }

    public void Commit() => Finish(_box.Text ?? "");

    /// <summary>put back what was there. Escape means "I did not mean to do
    /// this", and committing an accidental edit is exactly what it must not
    /// do.</summary>
    public void Cancel() => Finish(_before);

    void Finish(string text)
    {
        if (_closing || !Editing) return;
        _closing = true;

        var done = _commit;
        ItemId = null;
        _commit = null;
        _box.IsVisible = false;

        done?.Invoke(text);
        _closing = false;
    }
}

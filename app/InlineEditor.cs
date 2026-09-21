using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        _box.KeyDown += OnKey;
        _box.LostFocus += (_, _) => Commit();
        Children.Add(_box);
    }

    /// <summary>start editing. <paramref name="commit"/> is called with the
    /// final text, once, whether it was Enter or a click somewhere else.</summary>
    public void Begin(string itemId, string text, Action<string> commit)
    {
        ItemId = itemId;
        _before = text;
        _commit = commit;
        _closing = false;

        _box.Text = text;
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

        _box.FontSize = Math.Clamp(fontSize, 6, 200);
        _box.Width = Math.Max(60, w);
        _box.MinHeight = Math.Max(24, h);
        SetLeft(_box, x);
        SetTop(_box, y);
    }

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

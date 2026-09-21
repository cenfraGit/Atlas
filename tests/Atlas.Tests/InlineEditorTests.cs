using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;

namespace Atlas.Tests;

/// <summary>the editor laid over an item.
///
/// Every one of these runs in a real Avalonia window off screen, because the
/// bug they exist for was not in the logic: showing the box before it had
/// been given a width left a wrapping TextBox to be measured against
/// infinite space, and the process died on every double click with no
/// message at all.</summary>
public class InlineEditorTests
{
    /// <summary>the editor in a Grid, which is where the app puts it, not
    /// as a window's only content - a Canvas alone in a window is given a
    /// size, and a Canvas in a Grid over other things is the real case.</summary>
    static Window Hosted(InlineEditor editor)
    {
        var grid = new Grid();
        grid.Children.Add(new Border());
        grid.Children.Add(editor);
        var window = new Window { Width = 800, Height = 600, Content = grid };
        window.Show();
        return window;
    }

    /// <summary>lay out and actually paint. Painting is the step the stub
    /// renderer skips, and a box that measures fine and blows up when Skia
    /// is asked to draw it would pass every other test here.</summary>
    static void Draw(Window window)
    {
        window.Measure(new Avalonia.Size(800, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }

    [AvaloniaFact]
    public void ShowingTheBoxLaysOutWithoutDying()
    {
        var editor = new InlineEditor();
        var window = Hosted(editor);

        editor.Begin("n1", "a note", 100, 80, 300, 120, 14);

        // layout and paint: an unsized wrapping TextBox is a hazard to both
        Draw(window);

        Assert.True(editor.Editing);
    }

    /// <summary>the box must never be shown before it has a real width. This
    /// is the actual defect: Begin made it visible and the placement came
    /// afterwards, so one layout pass ran with no size to work from.</summary>
    [AvaloniaFact]
    public void TheBoxIsSizedBeforeItIsEverShown()
    {
        var editor = new InlineEditor();
        var window = Hosted(editor);

        editor.Begin("n1", "some words", 40, 60, 280, 90, 18);

        var box = Assert.IsType<TextBox>(Assert.Single(editor.Children));
        Assert.True(box.IsVisible);
        Assert.False(double.IsNaN(box.Width), "the box was shown with no width");
        Assert.True(box.Width > 0);
    }

    [AvaloniaFact]
    public void CommittingHandsBackWhatWasTyped()
    {
        var editor = new InlineEditor();
        var window = Hosted(editor);

        string? got = null;
        editor.Begin("n1", "before", 0, 0, 200, 60, 14, text => got = text);
        editor.SetTextForTest("after");
        editor.Commit();

        Assert.Equal("after", got);
        Assert.False(editor.Editing);
    }

    /// <summary>Escape means "I did not mean to do this", so committing the
    /// accidental edit is exactly what it must not do.</summary>
    [AvaloniaFact]
    public void CancellingPutsBackWhatWasThere()
    {
        var editor = new InlineEditor();
        var window = Hosted(editor);

        string? got = null;
        editor.Begin("n1", "before", 0, 0, 200, 60, 14, text => got = text);
        editor.SetTextForTest("typed over it");
        editor.Cancel();

        Assert.Equal("before", got);
        Assert.False(editor.Editing);
    }

    /// <summary>the callback runs once. It used to be reachable from Enter,
    /// from Escape and from losing focus, and the last of those happens as a
    /// consequence of the other two.</summary>
    [AvaloniaFact]
    public void TheCallbackRunsOnceHoweverItEnds()
    {
        var editor = new InlineEditor();
        var window = Hosted(editor);

        int calls = 0;
        editor.Begin("n1", "x", 0, 0, 200, 60, 14, _ => calls++);
        editor.Commit();
        editor.Commit();
        editor.Cancel();

        Assert.Equal(1, calls);
    }

    /// <summary>Enter commits, typed for real.
    ///
    /// A TextBox with AcceptsReturn handles Enter itself and marks it
    /// handled, so a bubbling handler never runs: Enter put a newline in the
    /// box and left it open. Driven as a real keystroke rather than by
    /// calling Commit, because the routing is the whole of what broke.</summary>
    [AvaloniaFact]
    public void EnterCommitsRatherThanAddingALine()
    {
        var editor = new InlineEditor();
        var window = Hosted(editor);

        string? got = null;
        editor.Begin("n1", "", 40, 60, 280, 90, 18, text => got = text);
        Draw(window);

        var box = Assert.IsType<TextBox>(Assert.Single(editor.Children));
        box.Focus();
        box.Text = "a note";
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal("a note", got);
        Assert.False(editor.Editing);
        Assert.DoesNotContain(Environment.NewLine, box.Text ?? "");
    }

    /// <summary>and shift+Enter still starts a line, which is the reason
    /// Enter had to be intercepted rather than the box left alone.</summary>
    [AvaloniaFact]
    public void ShiftEnterKeepsTypingInstead()
    {
        var editor = new InlineEditor();
        var window = Hosted(editor);

        int calls = 0;
        editor.Begin("n1", "", 40, 60, 280, 90, 18, _ => calls++);
        Draw(window);

        var box = Assert.IsType<TextBox>(Assert.Single(editor.Children));
        box.Focus();
        box.Text = "first";
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);

        Assert.Equal(0, calls);
        Assert.True(editor.Editing);
    }

    [AvaloniaFact]
    public void PlacingSomewhereElseDoesMoveIt()
    {
        var editor = new InlineEditor();
        var window = Hosted(editor);

        editor.Begin("n1", "words", 40, 60, 280, 90, 18);
        Draw(window);

        editor.Place(120, 200, 340, 120, 24);

        var box = Assert.IsType<TextBox>(Assert.Single(editor.Children));
        Assert.Equal(120, Canvas.GetLeft(box));
        Assert.Equal(200, Canvas.GetTop(box));
        Assert.Equal(340, box.Width);
    }

    [AvaloniaFact]
    public void PlacingWhileClosedDoesNothing()
    {
        var editor = new InlineEditor();
        var window = Hosted(editor);

        editor.Place(10, 10, 100, 40, 12);

        Assert.False(editor.Editing);
        Assert.False(Assert.IsType<TextBox>(Assert.Single(editor.Children)).IsVisible);
    }

    /// <summary>a zoomed-out board gives the box a type size under a point
    /// and a width of a few pixels, and a zoomed-in one gives it thousands.
    /// Neither may reach the layout as it stands.</summary>
    [AvaloniaTheory]
    [InlineData(0.01, 2, 1)]
    [InlineData(40, 12000, 4000)]
    public void AbsurdZoomsAreClamped(double fontSize, double w, double h)
    {
        var editor = new InlineEditor();
        var window = Hosted(editor);

        editor.Begin("n1", "words", 0, 0, w, h, fontSize);
        Draw(window);

        var box = Assert.IsType<TextBox>(Assert.Single(editor.Children));
        Assert.InRange(box.FontSize, 6, 200);
        Assert.True(box.Width >= 60);
    }
}

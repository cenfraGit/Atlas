using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Atlas.Tests;

/// <summary>the workspace panel: the boards panel with the map - Home -
/// pinned at the top, opened with Tab from anywhere, long names wrapped,
/// and a right edge that drags its width.</summary>
public class WorkspaceTests
{
    sealed record Rig(SceneView View, Scene Scene, BoardStore Store, BoardOverlay Panel, Window Window, TempDir Repo) : IDisposable
    {
        public void Dispose() => Repo.Dispose();
        public ListBox List => Panel.GetVisualDescendants().OfType<ListBox>().Single();
    }

    static Rig Open(params string[] names)
    {
        var repo = SampleRepo.Build();
        var store = BoardStore.Load(repo.Path);
        foreach (var n in names) store.Create(n);
        var scene = new Scene(Scanner.Build(repo.Path));
        var view = new SceneView(scene);
        var panel = new BoardOverlay(store) { Transitions = null };
        view.AttachBoards(store, panel);
        view.BuildLayers();
        var window = new Window { Width = 1000, Height = 800, Content = new Grid { Children = { view, panel } } };
        App.WireKeys(window, view);
        window.Show();
        return new Rig(view, scene, store, panel, window, repo);
    }

    static void Settle(Window w)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    [AvaloniaFact]
    public void HomeIsFirstAndOpeningItLeavesTheBoard()
    {
        using var r = Open("one");
        r.Scene.ActiveBoard = r.Store.Boards.Single();
        r.Panel.Show();
        Settle(r.Window);

        Assert.True(r.Panel.Rows[0].IsHome);
        r.List.SelectedIndex = 0;
        r.Panel.HandleKey(Key.Enter);

        Assert.Null(r.Scene.ActiveBoard);
        Assert.False(Reveal.Showing(r.Panel));
    }

    /// <summary>Home cannot be dragged, and nothing can be dropped above it.</summary>
    [AvaloniaFact]
    public void HomeStaysAtTheTop()
    {
        using var r = Open("one", "two");
        r.Panel.Show();
        Settle(r.Window);
        Point Centre(int row)
        {
            var c = r.List.ContainerFromIndex(row)!;
            return c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), r.Window)!.Value;
        }

        // a board dragged onto Home goes nowhere
        int one = r.Panel.Rows.ToList().FindIndex(x => x.Board?.Name == "one");
        r.Window.MouseDown(Centre(one), MouseButton.Left);
        r.Window.MouseMove(Centre(0), RawInputModifiers.LeftMouseButton);
        r.Window.MouseUp(Centre(0), MouseButton.Left);
        Assert.True(r.Panel.Rows[0].IsHome);
        Assert.False(r.Panel.Dragging);

        // and Home dragged onto a board goes nowhere either
        r.Window.MouseDown(Centre(0), MouseButton.Left);
        r.Window.MouseMove(Centre(one), RawInputModifiers.LeftMouseButton);
        r.Window.MouseUp(Centre(one), MouseButton.Left);
        Assert.True(r.Panel.Rows[0].IsHome);
    }

    /// <summary>tab from anywhere - including with the panel's own list
    /// holding focus, where tab would otherwise move to the next control.</summary>
    [AvaloniaFact]
    public void TabTogglesItEvenWithFocusInsideIt()
    {
        using var r = Open("one");
        r.View.Focus();

        r.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Assert.True(Reveal.Showing(r.Panel));

        Settle(r.Window);
        r.List.Focus();
        r.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Assert.False(Reveal.Showing(r.Panel));
    }

    /// <summary>a long name wraps inside the panel rather than running off it.</summary>
    [AvaloniaFact]
    public void ALongNameWraps()
    {
        using var r = Open("short", "a board with a name far too long to fit on one line of the panel at its usual width");
        r.Panel.Show();
        Settle(r.Window);

        int shortRow = r.Panel.Rows.ToList().FindIndex(x => x.Board?.Name == "short");
        int longRow = r.Panel.Rows.ToList().FindIndex(x => x.Board?.Name.StartsWith("a board") == true);
        var shortItem = r.List.ContainerFromIndex(shortRow)!;
        var longItem = r.List.ContainerFromIndex(longRow)!;

        Assert.True(longItem.Bounds.Height > shortItem.Bounds.Height * 1.5, "the long name stayed on one line");
        Assert.True(longItem.Bounds.Width <= r.Panel.Bounds.Width, "the long row is wider than the panel");
    }

    /// <summary>the right edge drags the width, within limits.</summary>
    [AvaloniaFact]
    public void TheRightEdgeResizesIt()
    {
        using var r = Open("one");
        r.Panel.Show();
        Settle(r.Window);
        double before = r.Panel.Bounds.Width;
        var edge = new Point(r.Panel.Bounds.Right - 2, 300);

        r.Window.MouseMove(edge);
        r.Window.MouseDown(edge, MouseButton.Left);
        r.Window.MouseMove(new Point(edge.X + 150, 300), RawInputModifiers.LeftMouseButton);
        r.Window.MouseUp(new Point(edge.X + 150, 300), MouseButton.Left);
        Settle(r.Window);
        Assert.Equal(before + 150, r.Panel.Bounds.Width, 1);

        r.Window.MouseDown(new Point(r.Panel.Bounds.Right - 2, 300), MouseButton.Left);
        r.Window.MouseMove(new Point(20, 300), RawInputModifiers.LeftMouseButton);
        r.Window.MouseUp(new Point(20, 300), MouseButton.Left);
        Settle(r.Window);
        Assert.Equal(240, r.Panel.Bounds.Width, 1);
    }
}

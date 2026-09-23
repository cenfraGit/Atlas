using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Atlas.Tests;

/// <summary>the boards panel: dragging boards between groups and groups
/// among themselves, with the list rearranging as the drag goes.
///
/// A drag used to show nothing until the drop, could not be called off, and
/// groups were always alphabetical.</summary>
public class BoardPanelTests
{
    const int W = 900, H = 900;

    sealed record Rig(SceneView View, BoardStore Store, BoardOverlay Panel, Window Window, TempDir Repo) : IDisposable
    {
        public void Dispose() => Repo.Dispose();

        public BoardStore Reloaded() => BoardStore.Load(Repo.Path);

        public Board this[string name] => Store.Boards.Single(b => b.Name == name);
    }

    /// <summary>two groups and an ungrouped board: ungrouped [loose],
    /// alpha [a1, a2], beta [b1].</summary>
    static Rig Open(Action<BoardStore>? before = null)
    {
        var repo = SampleRepo.Build();
        var store = BoardStore.Load(repo.Path);
        foreach (var (name, group, order) in new[] { ("loose", "", 0), ("a1", "alpha", 0), ("a2", "alpha", 1), ("b1", "beta", 0) })
        {
            var b = store.Create(name);
            b.Group = group;
            b.Order = order;
            store.Save(b);
        }
        before?.Invoke(store);

        var scene = new Scene(Scanner.Build(repo.Path));
        var view = new SceneView(scene);
        var panel = new BoardOverlay(store) { Transitions = null };
        view.AttachBoards(store, panel);
        view.BuildLayers();

        var root = new Grid();
        root.Children.Add(view);
        root.Children.Add(panel);
        var window = new Window { Width = W, Height = H, Content = root };
        window.Show();
        panel.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return new Rig(view, store, panel, window, repo);
    }

    static List<string> Layout(BoardOverlay panel) =>
        panel.Rows.Select(r => r.IsHeader ? $"[{r.Group}]" : r.Board!.Name).ToList();

    static Point Centre(Rig r, int row)
    {
        var list = r.Panel.GetVisualDescendants().OfType<ListBox>().Single();
        var c = list.ContainerFromIndex(row)!;
        return c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), r.Window)!.Value;
    }

    static int RowOf(Rig r, string label) => Layout(r.Panel).IndexOf(label);

    [AvaloniaFact]
    public void WithNoStoredOrderGroupsAreUngroupedFirstThenByName()
    {
        using var r = Open();
        Assert.Equal(["[]", "loose", "[alpha]", "a1", "a2", "[beta]", "b1"], Layout(r.Panel));
    }

    /// <summary>a heading can be pressed - that is how a group is dragged -
    /// but it is never left selected, and the buttons act only on boards.</summary>
    [AvaloniaFact]
    public void AHeadingIsNeverSelected()
    {
        using var r = Open();
        var at = Centre(r, RowOf(r, "[alpha]"));

        r.Window.MouseDown(at, MouseButton.Left);
        r.Window.MouseUp(at, MouseButton.Left);

        Assert.Empty(r.Panel.Selected);
        var list = r.Panel.GetVisualDescendants().OfType<ListBox>().Single();
        Assert.DoesNotContain(list.SelectedItems!.Cast<object>(), i => list.Items.IndexOf(i) == RowOf(r, "[alpha]"));
    }

    /// <summary>dropping a board on a heading puts it at the top of that
    /// group - and the list shows that while the button is still down.</summary>
    [AvaloniaFact]
    public void DraggingABoardOntoAHeadingMovesItThereAsYouGo()
    {
        using var r = Open();
        var from = Centre(r, RowOf(r, "b1"));
        var to = Centre(r, RowOf(r, "[alpha]"));

        r.Window.MouseDown(from, MouseButton.Left);
        r.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);

        Assert.Equal(["[]", "loose", "[alpha]", "b1", "a1", "a2"], Layout(r.Panel));
        r.Window.MouseUp(to, MouseButton.Left);

        var b1 = r.Reloaded().Boards.Single(b => b.Name == "b1");
        Assert.Equal(("alpha", 0), (b1.Group, b1.Order));
    }

    [AvaloniaFact]
    public void EscapeMidDragPutsEverythingBack()
    {
        using var r = Open();
        var from = Centre(r, RowOf(r, "b1"));
        var to = Centre(r, RowOf(r, "[alpha]"));

        r.Window.MouseDown(from, MouseButton.Left);
        r.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        Assert.True(r.Panel.Dragging);

        Assert.True(r.View.Escape());
        r.Window.MouseUp(to, MouseButton.Left);

        Assert.Equal(["[]", "loose", "[alpha]", "a1", "a2", "[beta]", "b1"], Layout(r.Panel));
        Assert.Equal("beta", r.Reloaded().Boards.Single(b => b.Name == "b1").Group);
        Assert.True(Reveal.Showing(r.Panel));           // the drag, not the panel
    }

    /// <summary>a group is dragged by its heading, and keeps the place it
    /// was dropped in - across a restart, since groups.json holds it.</summary>
    [AvaloniaFact]
    public void DraggingAHeadingReordersTheGroups()
    {
        using var r = Open();
        var from = Centre(r, RowOf(r, "[beta]"));
        var to = Centre(r, RowOf(r, "loose"));

        r.Window.MouseDown(from, MouseButton.Left);
        r.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        r.Window.MouseUp(to, MouseButton.Left);

        Assert.Equal(["[beta]", "b1", "[]", "loose", "[alpha]", "a1", "a2"], Layout(r.Panel));
        Assert.Equal(["beta", "", "alpha"], r.Reloaded().Groups());
        Assert.True(File.Exists(Path.Combine(r.Repo.Path, ".atlas", "groups.json")));
    }

    /// <summary>a group made after the order was stored has no place in it,
    /// and goes after the groups that do.</summary>
    [AvaloniaFact]
    public void ANewGroupGoesAfterTheOrderedOnes()
    {
        using var r = Open(store =>
        {
            store.GroupOrder.AddRange(["beta", "alpha", ""]);
            var fresh = store.Create("g1");
            fresh.Group = "gamma";
            store.Save(fresh);
        });

        Assert.Equal(["beta", "alpha", "", "gamma"], r.Store.Groups());
    }

    [AvaloniaFact]
    public void AClickWithoutADragMovesNothing()
    {
        using var r = Open();
        var at = Centre(r, RowOf(r, "b1"));

        r.Window.MouseDown(at, MouseButton.Left);
        r.Window.MouseMove(new Point(at.X, at.Y + 2), RawInputModifiers.LeftMouseButton);
        r.Window.MouseUp(at, MouseButton.Left);

        Assert.Equal(["[]", "loose", "[alpha]", "a1", "a2", "[beta]", "b1"], Layout(r.Panel));
        Assert.False(File.Exists(Path.Combine(r.Repo.Path, ".atlas", "groups.json")));
    }

    /// <summary>up from the first board stays on it, rather than landing on
    /// the heading above and leaving nothing selected.</summary>
    [AvaloniaFact]
    public void UpFromTheFirstBoardStaysOnIt()
    {
        using var r = Open();
        r.Panel.HandleKey(Key.Up);
        r.Panel.HandleKey(Key.Up);

        Assert.Equal("loose", Assert.Single(r.Panel.Selected).Name);

        r.Panel.HandleKey(Key.Down);                    // over the alpha heading
        Assert.Equal("a1", Assert.Single(r.Panel.Selected).Name);
    }

    static Avalonia.Media.IBrush? Behind(Rig r, int row)
    {
        var list = r.Panel.GetVisualDescendants().OfType<ListBox>().Single();
        var item = (ListBoxItem)list.ContainerFromIndex(row)!;
        return item.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>()
            .First(p => p.Name == "PART_ContentPresenter").Background;
    }

    static bool Clear(Avalonia.Media.IBrush? b) =>
        b is null || b is Avalonia.Media.ISolidColorBrush { Color.A: 0 };

    /// <summary>a heading does not light up on hover - that made headings
    /// look like buttons - but a board row still does.</summary>
    [AvaloniaFact]
    public void HoveringAHeadingDoesNotLightItUp()
    {
        using var r = Open();
        int heading = RowOf(r, "[alpha]"), board = RowOf(r, "a2");

        r.Window.MouseMove(Centre(r, heading));
        Assert.True(Clear(Behind(r, heading)), "a hovered heading was lit");

        r.Window.MouseMove(Centre(r, board));
        Assert.False(Clear(Behind(r, board)), "a hovered board row was not");
    }

    /// <summary>pressing one does light it up: that is the sign a drag of
    /// the group has begun.</summary>
    [AvaloniaFact]
    public void PressingAHeadingLightsItUp()
    {
        using var r = Open();
        int heading = RowOf(r, "[alpha]");

        r.Window.MouseMove(Centre(r, heading));
        r.Window.MouseDown(Centre(r, heading), MouseButton.Left);

        Assert.False(Clear(Behind(r, heading)), "a pressed heading was not lit");
        r.Window.MouseUp(Centre(r, heading), MouseButton.Left);
    }
}

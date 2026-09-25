using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Atlas.Tests;

/// <summary>a board's tour: views captured on the board, played back as
/// slides.
///
/// Tours used to be lists of map bookmarks. They live on boards now, stored
/// in the board's own file, and the map has neither.</summary>
public class TourTests
{
    const int W = 800, H = 600;

    sealed record Rig(SceneView View, Scene Scene, Board Board, BoardStore Store, TourPanel Panel, Window Window, TempDir Repo) : IDisposable
    {
        public void Dispose() => Repo.Dispose();
    }

    static Rig Open()
    {
        var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        var board = store.Create("tour", "b");
        board.Items.Add(new BoardItem { Id = "n", Kind = "note", X = 0, Y = 0, W = 200, H = 100, Text = "a" });
        board.Items.Add(new BoardItem { Id = "m", Kind = "note", X = 3000, Y = 2000, W = 200, H = 100, Text = "b" });
        scene.ActiveBoard = board;
        scene.CamX = 0;
        scene.CamY = 0;
        scene.CamS = 1f;

        var view = new SceneView(scene);
        view.AttachBoards(store, new BoardOverlay(store));
        var panel = new TourPanel();
        view.AttachTour(panel);
        view.AttachPrompt(new PromptOverlay());
        view.BuildLayers();

        var root = new Grid();
        root.Children.Add(view);
        root.Children.Add(panel);
        var window = new Window { Width = W, Height = H, Content = root };
        window.Show();
        window.Measure(new Avalonia.Size(W, H));
        window.Arrange(new Avalonia.Rect(0, 0, W, H));
        return new Rig(view, scene, board, store, panel, window, repo);
    }

    static void Look(Scene scene, float x, float y, float s)
    {
        scene.CamX = x;
        scene.CamY = y;
        scene.CamS = s;
    }

    [AvaloniaFact]
    public void MCapturesTheViewAsAStopAndSavesIt()
    {
        using var r = Open();
        Look(r.Scene, 100, 50, 2f);

        r.View.HandleKey(Key.M);

        var stop = Assert.Single(r.Board.Stops);
        Assert.Equal((100f, 50f), (stop.X, stop.Y));
        Assert.Equal((W / 2f, H / 2f), (stop.W, stop.H));      // the region, in board units

        // in the board's own file, so the tour travels with the board
        var reread = BoardStore.Load(r.Repo.Path).Boards.Single(b => b.Id == "b");
        Assert.Single(reread.Stops);
    }

    /// <summary>a stop is the region that was on screen, not a zoom level,
    /// so a smaller window frames the same things rather than cropping them.</summary>
    [Fact]
    public void AStopFramesTheSameRegionInAnyWindow()
    {
        var stop = Stop.Of(0, 0, 2f, 1600, 900);          // 800 x 450 of board

        Assert.Equal(2f, stop.ScaleFor(1600, 900), 3);
        Assert.Equal(1f, stop.ScaleFor(800, 450), 3);
        // a window of a different shape fits the whole region inside it
        float s = stop.ScaleFor(800, 800);
        Assert.True(800 / s >= 800 - 0.01f && 800 / s >= 450);
    }

    [AvaloniaFact]
    public void PlayingFliesToEachStopInTurnAndClampsAtTheEnds()
    {
        using var r = Open();
        r.Board.Stops.Add(Stop.Of(0, 0, 1f, W, H));
        r.Board.Stops.Add(Stop.Of(3000, 2000, 0.5f, W, H));

        r.View.HandleKey(Key.P);
        Assert.Equal((0f, 0f), (r.View.Destination().X, r.View.Destination().Y));

        r.View.HandleKey(Key.Right);
        var d = r.View.Destination();
        Assert.Equal(3000f, d.X, 1);
        Assert.Equal(2000f, d.Y, 1);
        Assert.Equal(0.5f, d.S, 3);

        r.View.HandleKey(Key.Space);                     // already on the last one
        Assert.Equal(3000f, r.View.Destination().X, 1);

        r.View.HandleKey(Key.Left);
        Assert.Equal(0f, r.View.Destination().X, 1);
    }

    [AvaloniaFact]
    public void EscapeStopsTheTour()
    {
        using var r = Open();
        r.Board.Stops.Add(Stop.Of(0, 0, 1f, W, H));
        r.Board.Stops.Add(Stop.Of(3000, 2000, 1f, W, H));
        r.View.HandleKey(Key.P);

        Assert.True(r.View.Escape());
        r.View.HandleKey(Key.Right);                     // no longer a tour key

        Assert.Equal(0f, r.View.Destination().X, 1);
    }

    [AvaloniaFact]
    public void PlayingWithNoStopsGoesNowhere()
    {
        using var r = Open();
        Look(r.Scene, 42, 7, 1f);

        r.View.HandleKey(Key.P);

        Assert.Equal((42f, 7f), (r.View.Destination().X, r.View.Destination().Y));
    }

    /// <summary>with the panel open, the right of the window is under it.
    /// A stop is what you could see, so it is the uncovered part - and it
    /// plays back into the uncovered part too.</summary>
    [AvaloniaFact]
    public void TheStopIsWhatThePanelLeftVisible()
    {
        using var r = Open();
        Look(r.Scene, 0, 0, 1f);
        r.View.HandleKey(Key.M, KeyModifiers.Shift);      // the panel
        Assert.True(Reveal.Showing(r.Panel));

        r.View.HandleKey(Key.M);

        var stop = Assert.Single(r.Board.Stops);
        float covered = (float)r.Panel.Width;
        Assert.Equal(W - covered, stop.W, 1);
        Assert.Equal(-covered / 2, stop.X, 1);

        // and going back to it with the panel still open puts the camera
        // where it was
        Look(r.Scene, 5000, 5000, 3f);
        r.View.HandleKey(Key.Down);                        // the panel previews
        r.View.HandleKey(Key.Up);
        var d = r.View.Destination();
        Assert.Equal(0f, d.X, 1);
        Assert.Equal(1f, d.S, 3);
    }

    [AvaloniaFact]
    public void ThePanelDeletesAndReordersStops()
    {
        using var r = Open();
        r.Board.Stops.Add(new Stop { Name = "one", W = 10, H = 10 });
        r.Board.Stops.Add(new Stop { Name = "two", W = 10, H = 10 });
        r.Board.Stops.Add(new Stop { Name = "three", W = 10, H = 10 });
        r.View.HandleKey(Key.M, KeyModifiers.Shift);

        r.Panel.Move(0, 2);
        Assert.Equal(["two", "three", "one"], r.Board.Stops.Select(s => s.Name));

        r.Panel.Select(0);
        r.Panel.Remove();
        Assert.Equal(["three", "one"], r.Board.Stops.Select(s => s.Name));
        // saved, like everything else on a board
        Assert.Equal(2, BoardStore.Load(r.Repo.Path).Boards.Single(b => b.Id == "b").Stops.Count);
    }

    /// <summary>the middle of a row in the panel's list, in window
    /// coordinates.</summary>
    static Avalonia.Point RowCentre(Rig r, int i)
    {
        var list = r.Panel.GetVisualDescendants().OfType<ListBox>().Single();
        var row = list.ContainerFromIndex(i)!;
        return row.TranslatePoint(new Avalonia.Point(row.Bounds.Width / 2, row.Bounds.Height / 2), r.Window)!.Value;
    }

    static Rig WithThreeStops()
    {
        var r = Open();
        foreach (var name in new[] { "one", "two", "three" })
            r.Board.Stops.Add(new Stop { Name = name, W = 10, H = 10 });
        // no slide: a click aimed at a row that is still moving in misses it
        r.Panel.Transitions = null;
        r.View.HandleKey(Key.M, KeyModifiers.Shift);
        // the panel is put in place on the dispatcher; let it arrive
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        r.Window.UpdateLayout();
        return r;
    }

    /// <summary>a drag moves the row as it goes, so what you see while
    /// dragging is the order you will get.</summary>
    [AvaloniaFact]
    public void DraggingAStopReordersTheTour()
    {
        using var r = WithThreeStops();
        var from = RowCentre(r, 0);
        var to = RowCentre(r, 2);

        r.Window.MouseDown(from, MouseButton.Left);
        r.Window.MouseMove(new Avalonia.Point(from.X, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
        r.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        // already moved, before the drop
        Assert.Equal(["two", "three", "one"], r.Board.Stops.Select(s => s.Name));
        r.Window.MouseUp(to, MouseButton.Left);

        Assert.Equal(["two", "three", "one"],
            BoardStore.Load(r.Repo.Path).Boards.Single(b => b.Id == "b").Stops.Select(s => s.Name));
    }

    [AvaloniaFact]
    public void EscapeMidDragPutsTheOrderBack()
    {
        using var r = WithThreeStops();
        var from = RowCentre(r, 0);
        var to = RowCentre(r, 2);

        r.Window.MouseDown(from, MouseButton.Left);
        r.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        Assert.True(r.Panel.Dragging);

        Assert.True(r.View.Escape());
        r.Window.MouseUp(to, MouseButton.Left);

        Assert.Equal(["one", "two", "three"], r.Board.Stops.Select(s => s.Name));
        Assert.True(Reveal.Showing(r.Panel));            // only the drag was cancelled
    }

    [AvaloniaFact]
    public void LeavingTheBoardEndsTheTourAndClosesThePanel()
    {
        using var r = Open();
        r.Board.Stops.Add(Stop.Of(0, 0, 1f, W, H));
        r.View.HandleKey(Key.M, KeyModifiers.Shift);
        r.View.HandleKey(Key.P);

        r.View.GoHome();

        Assert.Null(r.Scene.ActiveBoard);
        Assert.False(Reveal.Showing(r.Panel));
        Assert.False(r.View.Escape());                    // nothing left open
    }

    /// <summary>Delete with the panel open still deletes what is picked on
    /// the board. The panel stays open while you work, and taking the key
    /// would delete a stop you were not looking at.</summary>
    [AvaloniaFact]
    public void DeleteStillMeansThePickedItems()
    {
        using var r = Open();
        r.Board.Stops.Add(new Stop { Name = "one", W = 10, H = 10 });
        r.View.HandleKey(Key.M, KeyModifiers.Shift);
        r.View.SetEditing(true);
        r.Scene.Picked.Add("n");

        r.View.HandleKey(Key.Delete);

        Assert.Single(r.Board.Stops);
        Assert.DoesNotContain(r.Board.Items, i => i.Id == "n");
    }

    /// <summary>a generated board is not yours to add a tour to.</summary>
    [AvaloniaFact]
    public void AReadOnlyBoardTakesNoStops()
    {
        using var r = Open();
        r.Scene.BoardReadOnly = true;

        r.View.HandleKey(Key.M);

        Assert.Empty(r.Board.Stops);
    }

    /// <summary>and the map has no bookmarks any more: M there does nothing,
    /// and nothing is written.</summary>
    [AvaloniaFact]
    public void TheMapHasNoBookmarks()
    {
        using var r = Open();
        r.View.GoHome();

        r.View.HandleKey(Key.M);

        Assert.False(File.Exists(Path.Combine(r.Repo.Path, ".atlas", "bookmarks.json")));
    }
}

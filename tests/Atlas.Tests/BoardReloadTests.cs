using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Atlas.Tests;

/// <summary>boards changed on disk are taken in while the app is open.
///
/// A board built by `atlas board` - or brought in by a pull - used to need a
/// restart to show up. The hard part is not reading the file but telling a
/// change made elsewhere from the echo of the app's own save, which arrives
/// the same way and must not put back a board that has moved on since.</summary>
public class BoardReloadTests
{
    static BoardItem Note(string id, float x = 0) => new() { Id = id, Kind = "note", Text = id, X = x, W = 200 };

    /// <summary>a second store on the same folder stands in for whatever
    /// else writes there - the command line, git, a teammate's editor.</summary>
    static BoardStore Elsewhere(TempDir repo) => BoardStore.Load(repo.Path);

    [Fact]
    public void AnEditMadeElsewhereIsTakenInToTheSameBoard()
    {
        using var repo = SampleRepo.Build();
        var store = BoardStore.Load(repo.Path);
        var mine = store.Create("b");

        var theirs = Elsewhere(repo).Boards.Single();
        theirs.Items.Add(Note("n"));
        theirs.Stops.Add(new Stop { Name = "s" });
        Elsewhere(repo).Save(theirs);

        var changed = store.Refresh();
        Assert.Equal([mine], changed);
        Assert.Same(mine, store.Boards.Single());       // updated in place
        Assert.Equal("n", mine.Items.Single().Id);
        Assert.Equal("s", mine.Stops.Single().Name);
    }

    /// <summary>the app saves as it goes, and every save is a change on
    /// disk. Taking that back in would undo whatever was done since it.</summary>
    [Fact]
    public void TheAppsOwnSaveIsNotAChange()
    {
        using var repo = SampleRepo.Build();
        var store = BoardStore.Load(repo.Path);
        var b = store.Create("b");
        b.Items.Add(Note("saved"));
        store.Save(b);
        b.Items.Add(Note("not saved yet"));

        Assert.Null(store.Refresh());
        Assert.Equal(2, b.Items.Count);
    }

    [Fact]
    public void ABoardMadeOrDeletedElsewhereComesAndGoes()
    {
        using var repo = SampleRepo.Build();
        var store = BoardStore.Load(repo.Path);
        var kept = store.Create("kept");

        var other = Elsewhere(repo);
        var made = other.Create("made");
        Assert.Equal("made", Assert.Single(store.Refresh()!).Name);
        Assert.Equal(2, store.Boards.Count);

        other.Delete(other.Boards.Single(b => b.Name == "made"));
        var gone = Assert.Single(store.Refresh()!);
        Assert.Equal("made", gone.Name);
        Assert.Same(kept, store.Boards.Single());
    }

    /// <summary>a file caught half written does not parse; it is skipped,
    /// and the write finishing is a change of its own.</summary>
    [Fact]
    public void AHalfWrittenFileIsLeftForLater()
    {
        using var repo = SampleRepo.Build();
        var store = BoardStore.Load(repo.Path);
        var b = store.Create("b");

        File.WriteAllText(b.Path, "{ \"id\": \"x\", \"items\": [");
        Assert.Null(store.Refresh());
        Assert.Empty(b.Items);

        // it will not load from there, so finish the write by hand
        var theirs = new Board { Id = b.Id, Name = b.Name, Path = b.Path, Items = { Note("n") } };
        Elsewhere(repo).Save(theirs);
        Assert.NotNull(store.Refresh());
        Assert.Single(b.Items);
    }

    [Fact]
    public void TheGroupOrderIsTakenInToo()
    {
        using var repo = SampleRepo.Build();
        var store = BoardStore.Load(repo.Path);
        store.Create("b");

        var other = Elsewhere(repo);
        other.GroupOrder.AddRange(["docs", ""]);
        other.SaveGroups();

        Assert.Empty(store.Refresh()!);
        Assert.Equal(["docs", ""], store.GroupOrder);
    }

    const int W = 800, H = 600;

    static (SceneView View, Window Window, Scene Scene, BoardStore Store, Board Board) Open(TempDir repo)
    {
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        var board = store.Create("open");
        board.Items.Add(new BoardItem { Id = "s", Kind = "shape", X = -100, Y = -50, W = 200, H = 100 });
        store.Save(board);
        scene.ActiveBoard = board;
        (scene.CamX, scene.CamY, scene.CamS) = (0, 0, 1);

        var view = new SceneView(scene);
        view.AttachBoards(store, new BoardOverlay(store));
        view.BuildLayers();
        var window = new Window { Width = W, Height = H, Content = view };
        window.Show();
        window.Measure(new Avalonia.Size(W, H));
        window.Arrange(new Avalonia.Rect(0, 0, W, H));
        return (view, window, scene, store, board);
    }

    [AvaloniaFact]
    public void TheOpenBoardShowsWhatChangedOnDisk()
    {
        using var repo = SampleRepo.Build();
        var (view, _, scene, _, board) = Open(repo);
        scene.Picked.Add("s");

        var theirs = Elsewhere(repo).Boards.Single();
        theirs.Items.RemoveAll(i => i.Id == "s");
        theirs.Items.Add(Note("n"));
        Elsewhere(repo).Save(theirs);
        view.ReloadBoards();

        Assert.Same(board, scene.ActiveBoard);
        Assert.Equal("n", board.Items.Single().Id);
        Assert.Empty(scene.Picked);                     // "s" is gone
        Assert.False(view.CanUndo);                     // its snapshots were of the old board
    }

    /// <summary>not while something is being dragged: the board would
    /// change under the hand holding it. It lands when the drag ends.</summary>
    [AvaloniaFact]
    public void AReloadWaitsForTheDragToEnd()
    {
        using var repo = SampleRepo.Build();
        var (view, window, scene, _, board) = Open(repo);
        view.SetEditing(true);
        scene.Picked.Add("s");

        var at = new Avalonia.Point(W / 2, H / 2);
        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(new Avalonia.Point(W / 2 + 40, H / 2), RawInputModifiers.LeftMouseButton);

        var theirs = Elsewhere(repo).Boards.Single();
        theirs.Items.Add(Note("n", 500));
        Elsewhere(repo).Save(theirs);
        view.ReloadBoards();
        Assert.DoesNotContain(board.Items, i => i.Id == "n");

        // letting go saves the move - and must not save over their board
        window.MouseUp(new Avalonia.Point(W / 2 + 40, H / 2), MouseButton.Left);
        view.ReloadBoards();
        Assert.Contains(board.Items, i => i.Id == "n");
        Assert.Contains("\"n\"", File.ReadAllText(board.Path));
    }

    /// <summary>disk wins: a save over a file changed since it was read is
    /// refused, and the refresh brings the change in.</summary>
    [Fact]
    public void ASaveDoesNotOverwriteAChangeMadeElsewhere()
    {
        using var repo = SampleRepo.Build();
        var store = BoardStore.Load(repo.Path);
        var b = store.Create("b");

        var theirs = Elsewhere(repo).Boards.Single();
        theirs.Items.Add(Note("theirs"));
        Elsewhere(repo).Save(theirs);

        b.Items.Add(Note("mine"));
        store.Save(b);

        Assert.Contains("theirs", File.ReadAllText(b.Path));
        store.Refresh();
        Assert.Equal("theirs", b.Items.Single().Id);
    }

    [AvaloniaFact]
    public void DeletingTheOpenBoardOnDiskLeavesIt()
    {
        using var repo = SampleRepo.Build();
        var (view, _, scene, _, _) = Open(repo);

        var other = Elsewhere(repo);
        other.Delete(other.Boards.Single());
        view.ReloadBoards();

        Assert.Null(scene.ActiveBoard);
    }

    /// <summary>and the watcher itself: a write from outside reaches the
    /// open board with nobody calling anything.</summary>
    [AvaloniaFact]
    public void TheWatcherNoticesAWrite()
    {
        using var repo = SampleRepo.Build();
        var (view, _, _, _, board) = Open(repo);
        view.WatchBoards();

        var theirs = Elsewhere(repo).Boards.Single();
        theirs.Items.Add(Note("n"));
        Elsewhere(repo).Save(theirs);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (board.Items.All(i => i.Id != "n") && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
        Assert.Contains(board.Items, i => i.Id == "n");
    }
}

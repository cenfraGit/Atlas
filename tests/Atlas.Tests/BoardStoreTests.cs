namespace Atlas.Tests;

/// <summary>boards are written into the scanned repo the moment they change,
/// so round tripping, renaming and deleting are the storage contract.</summary>
public class BoardStoreTests
{
    static BoardItem FileWindow(string id, string path, int from = 5, int to = 24) =>
        new() { Id = id, Kind = "file", File = path, Line = from, EndLine = to, W = 620 };

    [Fact]
    public void ARepoWithNoBoardsLoadsEmpty()
    {
        using var dir = new TempDir();
        Assert.Empty(BoardStore.Load(dir.Path).Boards);
    }

    [Fact]
    public void BoardsLiveUnderDotAtlasInTheScannedRepo()
    {
        using var dir = new TempDir();
        Assert.Equal(Path.Combine(dir.Path, ".atlas", "boards"), BoardStore.DirFor(dir.Path));
    }

    [Fact]
    public void CreatingABoardWritesAReadableFileName()
    {
        using var dir = new TempDir();
        var board = BoardStore.Load(dir.Path).Create("Startup sequence");

        Assert.True(File.Exists(board.Path));
        Assert.StartsWith("startup-sequence-", Path.GetFileName(board.Path));
    }

    [Fact]
    public void ABoardRoundTripsThroughDisk()
    {
        using var dir = new TempDir();
        var store = BoardStore.Load(dir.Path);
        var board = store.Create("Startup sequence");
        board.Items.Add(FileWindow("i1", "app/Program.cs"));
        board.Items.Add(new BoardItem
        {
            Id = "i2", Kind = "note", Text = "the host is already built here",
            X = 700, Y = 0, W = 380, Color = "#ffcc00",
        });
        store.Save(board);

        var reread = BoardStore.Load(dir.Path);
        var only = Assert.Single(reread.Boards);

        Assert.Equal("Startup sequence", only.Name);
        Assert.Equal(2, only.Items.Count);
        Assert.Equal(5, only.Items[0].Line);
        Assert.Equal(24, only.Items[0].EndLine);
        Assert.Equal("the host is already built here", only.Items[1].Text);
        Assert.Equal("#ffcc00", only.Items[1].Color);
    }

    [Fact]
    public void ContainingFindsTheBoardsHoldingAFile()
    {
        using var dir = new TempDir();
        var store = BoardStore.Load(dir.Path);

        var a = store.Create("A");
        a.Items.Add(FileWindow("i1", "app/Program.cs"));
        store.Save(a);

        var b = store.Create("B");
        b.Items.Add(FileWindow("i2", "app/Program.cs"));
        b.Items.Add(FileWindow("i3", "app/Scene.cs"));
        store.Save(b);

        var reread = BoardStore.Load(dir.Path);
        Assert.Equal(2, reread.Containing("app/Program.cs").Count);
        Assert.Single(reread.Containing("app/Scene.cs"));
        Assert.Empty(reread.Containing("nope/missing.cs"));
    }

    [Fact]
    public void RenamingMovesTheFileRatherThanLeavingTwo()
    {
        using var dir = new TempDir();
        var store = BoardStore.Load(dir.Path);
        var board = store.Create("Startup sequence");
        var oldPath = board.Path;

        store.Rename(board, "How drawing works");

        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(board.Path));
        Assert.Equal("How drawing works", board.Name);
        Assert.Single(BoardStore.Load(dir.Path).Boards);
    }

    [Fact]
    public void RenamingKeepsTheIdSoReferencesSurvive()
    {
        using var dir = new TempDir();
        var store = BoardStore.Load(dir.Path);
        var board = store.Create("Startup sequence");
        var id = board.Id;

        store.Rename(board, "Something else");

        Assert.Equal(id, board.Id);
        Assert.Equal(id, BoardStore.Load(dir.Path).Boards[0].Id);
    }

    [Fact]
    public void DeletingRemovesTheBoardFromDiskAndFromTheStore()
    {
        using var dir = new TempDir();
        var store = BoardStore.Load(dir.Path);
        var board = store.Create("Gone");
        var path = board.Path;

        store.Delete(board);

        Assert.False(File.Exists(path));
        Assert.Empty(store.Boards);
        Assert.Empty(BoardStore.Load(dir.Path).Boards);
    }

    [Fact]
    public void GroupsAndOrderSurviveARoundTrip()
    {
        using var dir = new TempDir();
        var store = BoardStore.Load(dir.Path);
        var board = store.Create("Rendering");
        board.Group = "Drawing";
        board.Order = 3;
        store.Save(board);

        var reread = BoardStore.Load(dir.Path).Boards[0];
        Assert.Equal("Drawing", reread.Group);
        Assert.Equal(3, reread.Order);
    }

    [Fact]
    public void EveryBoardGetsItsOwnId()
    {
        using var dir = new TempDir();
        var store = BoardStore.Load(dir.Path);
        var ids = Enumerable.Range(0, 5).Select(i => store.Create($"Board {i}").Id).ToList();

        Assert.Equal(5, ids.Distinct().Count());
        Assert.All(ids, id => Assert.NotEqual("", id));
    }

    [Fact]
    public void AnUnreadableBoardFileDoesNotTakeTheRestDown()
    {
        using var dir = new TempDir();
        var store = BoardStore.Load(dir.Path);
        store.Save(store.Create("Good"));
        File.WriteAllText(Path.Combine(BoardStore.DirFor(dir.Path), "broken.json"), "{ not json");

        // one corrupt file must not cost you every other board
        Assert.Single(BoardStore.Load(dir.Path).Boards);
    }

    /// <summary>what the app writes is what git stores. A board written with
    /// this machine's newline shows up as modified the moment Atlas saves it,
    /// however little changed - and .atlas/ is a folder people commit.</summary>
    [Fact]
    public void ABoardIsWrittenWithLfEndings()
    {
        using var dir = new TempDir("atlas_boards");
        var store = BoardStore.Load(dir.Path);
        var board = store.Create("endings", "e1");
        board.Items.Add(new BoardItem { Id = "n", Kind = "note", Text = "a note" });
        store.Save(board);

        var text = File.ReadAllText(board.Path);

        Assert.Contains("\n", text);
        Assert.DoesNotContain("CRLF", text.Replace("\r\n", "CRLF"));
    }
}

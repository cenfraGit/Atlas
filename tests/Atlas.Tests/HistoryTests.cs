namespace Atlas.Tests;

/// <summary>undo is by snapshot, and the board object is refilled in place
/// because the rest of the app holds a reference to it. Nothing covered this
/// before; the UI test noticed a broken undo only through its side effect on
/// disk.</summary>
public class HistoryTests
{
    static Board BoardWith(params string[] itemIds)
    {
        var b = new Board { Id = "b1", Name = "Startup" };
        foreach (var id in itemIds)
            b.Items.Add(new BoardItem { Id = id, Kind = "note", Text = id, X = 10, Y = 20 });
        return b;
    }

    [Fact]
    public void NothingToUndoOnAFreshHistory()
    {
        var h = new History();
        Assert.False(h.CanUndo);
        Assert.False(h.CanRedo);
        Assert.False(h.Undo(BoardWith()));
        Assert.False(h.Redo(BoardWith()));
    }

    [Fact]
    public void UndoRestoresWhatWasRecordedBeforeTheChange()
    {
        var board = BoardWith("a");
        var h = new History();

        h.Record(board);
        board.Items.Add(new BoardItem { Id = "b", Kind = "note" });
        Assert.Equal(2, board.Items.Count);

        Assert.True(h.Undo(board));
        Assert.Equal(["a"], board.Items.Select(i => i.Id));
    }

    [Fact]
    public void RedoPutsTheChangeBack()
    {
        var board = BoardWith("a");
        var h = new History();

        h.Record(board);
        board.Items.Add(new BoardItem { Id = "b", Kind = "note" });
        h.Undo(board);

        Assert.True(h.Redo(board));
        Assert.Equal(["a", "b"], board.Items.Select(i => i.Id));
    }

    [Fact]
    public void UndoRefillsTheSameBoardObject()
    {
        var board = BoardWith("a");
        var items = board.Items;
        var h = new History();

        h.Record(board);
        board.Items.Add(new BoardItem { Id = "b", Kind = "note" });
        h.Undo(board);

        // the app holds this reference; swapping the list out would strand it
        Assert.Same(items, board.Items);
    }

    [Fact]
    public void ANewChangeDiscardsTheRedoStack()
    {
        var board = BoardWith("a");
        var h = new History();

        h.Record(board);
        board.Items.Add(new BoardItem { Id = "b", Kind = "note" });
        h.Undo(board);
        Assert.True(h.CanRedo);

        h.Record(board);
        board.Items.Add(new BoardItem { Id = "c", Kind = "note" });
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void RenamesAreUndoneToo()
    {
        var board = BoardWith("a");
        var h = new History();

        h.Record(board);
        board.Name = "How drawing works";
        h.Undo(board);

        Assert.Equal("Startup", board.Name);
    }

    [Fact]
    public void ItemStateIsRestoredNotJustTheCount()
    {
        var board = BoardWith("a");
        var h = new History();

        h.Record(board);
        board.Items[0].X = 999;
        board.Items[0].Text = "moved";
        h.Undo(board);

        Assert.Equal(10, board.Items[0].X);
        Assert.Equal("a", board.Items[0].Text);
    }

    [Fact]
    public void TheStackIsBoundedAndKeepsTheMostRecent()
    {
        var board = BoardWith();
        var h = new History();

        // 70 changes against a depth of 60: the oldest are dropped, and undoing
        // as far as it goes must not throw or resurrect a discarded state
        for (int i = 0; i < 70; i++)
        {
            h.Record(board);
            board.Items.Add(new BoardItem { Id = $"i{i}", Kind = "note" });
        }
        int undone = 0;
        while (h.Undo(board)) undone++;

        Assert.Equal(60, undone);
        Assert.Equal(10, board.Items.Count);
    }

    [Fact]
    public void ClearDropsBothStacks()
    {
        var board = BoardWith("a");
        var h = new History();
        h.Record(board);
        board.Items.Clear();
        h.Undo(board);

        h.Clear();
        Assert.False(h.CanUndo);
        Assert.False(h.CanRedo);
    }
}

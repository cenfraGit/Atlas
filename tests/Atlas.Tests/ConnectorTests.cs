using SkiaSharp;

namespace Atlas.Tests;

/// <summary>arrows tied to the things they point at.
///
/// The whole trick is that a tied end has no stored position: it is worked
/// out from the item's box every frame, so nothing has to update a connector
/// when a box moves, because there is nothing to update. These check that -
/// and that an end left loose still behaves like the free arrows that were
/// there before.</summary>
public class ConnectorTests
{
    static BoardItem Box(string id, float x, float y, float w = 100, float h = 60) =>
        new() { Id = id, Kind = "shape", X = x, Y = y, W = w, H = h };

    static BoardItem Arrow(string? from = null, string? to = null,
        float x = 0, float y = 0, float x2 = 500, float y2 = 0) =>
        new() { Id = "a1", Kind = "arrow", X = x, Y = y, X2 = x2, Y2 = y2, From = from, To = to };

    sealed class Fixture : IDisposable
    {
        public readonly TempDir Repo = SampleRepo.Build();
        public readonly Board Board = new() { Id = "b", Name = "flow" };
        public readonly Scene Scene;

        public Fixture()
        {
            Scene = new Scene(Scanner.Build(Repo.Path)) { ActiveBoard = Board, CamS = 1f };
        }

        public void Dispose()
        {
            Scene.ActiveBoard = null;
            Scene.Dispose();
            Repo.Dispose();
        }
    }

    // --- loose ends -------------------------------------------------------

    [Fact]
    public void AnUntiedArrowIsWhereItWasPut()
    {
        using var f = new Fixture();
        var arrow = Arrow(x: 10, y: 20, x2: 300, y2: 400);
        f.Board.Items.Add(arrow);

        var (a, b) = f.Scene.ArrowEnds(arrow);

        Assert.Equal(new SKPoint(10, 20), a);
        Assert.Equal(new SKPoint(300, 400), b);
    }

    // --- tied ends --------------------------------------------------------

    [Fact]
    public void ATiedEndSitsOnTheEdgeOfItsBox()
    {
        using var f = new Fixture();
        var box = Box("b1", 0, 0);            // 0,0 to 100,60
        f.Board.Items.Add(box);
        var arrow = Arrow(from: "b1", x2: 500, y2: 30);
        f.Board.Items.Add(arrow);

        var (a, _) = f.Scene.ArrowEnds(arrow);

        // straight out to the right, so it leaves through the right edge
        Assert.Equal(100, a.X, 1);
        Assert.Equal(30, a.Y, 1);
    }

    [Fact]
    public void ATiedEndFollowsWhenTheBoxMoves()
    {
        using var f = new Fixture();
        var box = Box("b1", 0, 0);
        f.Board.Items.Add(box);
        var arrow = Arrow(from: "b1", x2: 500, y2: 30);
        f.Board.Items.Add(arrow);

        var before = f.Scene.ArrowEnds(arrow).A;

        box.X += 200;                          // drag the box
        var after = f.Scene.ArrowEnds(arrow).A;

        // nothing told the arrow, and the arrow moved
        Assert.Equal(before.X + 200, after.X, 1);
    }

    [Fact]
    public void ATiedEndFollowsWhenTheBoxIsResized()
    {
        using var f = new Fixture();
        var box = Box("b1", 0, 0);
        f.Board.Items.Add(box);
        var arrow = Arrow(from: "b1", x2: 500, y2: 30);
        f.Board.Items.Add(arrow);

        float before = f.Scene.ArrowEnds(arrow).A.X;
        box.W = 300;

        Assert.True(f.Scene.ArrowEnds(arrow).A.X > before, "the end should move out with the edge");
    }

    [Fact]
    public void BothEndsCanBeTied()
    {
        using var f = new Fixture();
        f.Board.Items.Add(Box("b1", 0, 0));
        f.Board.Items.Add(Box("b2", 400, 0));
        var arrow = Arrow(from: "b1", to: "b2");
        f.Board.Items.Add(arrow);

        var (a, b) = f.Scene.ArrowEnds(arrow);

        // they meet each other's facing edges, and do not cross
        Assert.Equal(100, a.X, 1);
        Assert.Equal(400, b.X, 1);
        Assert.True(a.X < b.X);
    }

    [Fact]
    public void TheEndsDoNotChaseEachOtherRoundTheBoxes()
    {
        using var f = new Fixture();
        f.Board.Items.Add(Box("b1", 0, 0));
        f.Board.Items.Add(Box("b2", 0, 400));   // directly below
        var arrow = Arrow(from: "b1", to: "b2");
        f.Board.Items.Add(arrow);

        var (a, b) = f.Scene.ArrowEnds(arrow);

        // each aims at the other's centre, not at the edge the other just
        // moved to, so a vertical pair meets top and bottom
        Assert.Equal(60, a.Y, 1);
        Assert.Equal(400, b.Y, 1);
    }

    [Fact]
    public void OneEndTiedAndOneLooseWorks()
    {
        using var f = new Fixture();
        f.Board.Items.Add(Box("b1", 0, 0));
        var arrow = Arrow(from: "b1", x2: 900, y2: 30);
        f.Board.Items.Add(arrow);

        var (a, b) = f.Scene.ArrowEnds(arrow);

        Assert.Equal(100, a.X, 1);
        Assert.Equal(new SKPoint(900, 30), b);
    }

    // --- things going missing --------------------------------------------

    [Fact]
    public void CuttingTheBoxLeavesTheArrowWhereItWas()
    {
        using var f = new Fixture();
        var box = Box("b1", 0, 0);
        f.Board.Items.Add(box);
        var arrow = Arrow(from: "b1", x: 42, y: 43, x2: 500, y2: 30);
        f.Board.Items.Add(arrow);

        f.Board.Items.Remove(box);
        var (a, _) = f.Scene.ArrowEnds(arrow);

        // the stored position is the fallback, so it does not collapse to 0,0
        Assert.Equal(new SKPoint(42, 43), a);
    }

    [Fact]
    public void ATieToNothingIsHarmless()
    {
        using var f = new Fixture();
        var arrow = Arrow(from: "never-existed", x: 5, y: 6);
        f.Board.Items.Add(arrow);

        Assert.Equal(new SKPoint(5, 6), f.Scene.ArrowEnds(arrow).A);
    }

    // --- picking ----------------------------------------------------------

    [Fact]
    public void AConnectorIsPickedAlongItsDrawnLineNotItsStoredOne()
    {
        using var f = new Fixture();
        f.Board.Items.Add(Box("b1", 0, 0));
        // stored coordinates say one thing; the tie says another
        var arrow = Arrow(from: "b1", x: -9000, y: -9000, x2: 400, y2: 30);
        f.Board.Items.Add(arrow);

        // halfway along where it is actually drawn, 100,30 to 400,30
        Assert.Equal("a1", f.Scene.ArrowAt(250, 30)?.Id);
        Assert.Null(f.Scene.ArrowAt(250, 300));
    }

    [Fact]
    public void AnEndKnobIsWhereTheEndIsDrawn()
    {
        using var f = new Fixture();
        f.Board.Items.Add(Box("b1", 0, 0));
        var arrow = Arrow(from: "b1", x: -9000, y: -9000, x2: 400, y2: 30);
        f.Board.Items.Add(arrow);
        f.Scene.Picked.Add("a1");

        var end = f.Scene.ArrowEndAt(100, 30);

        Assert.NotNull(end);
        Assert.Equal(1, end!.Value.End);
    }

    [Fact]
    public void AnArrowStillDoesNotSwallowAClickAsABox()
    {
        using var f = new Fixture();
        f.Board.Items.Add(Box("b1", 0, 0));
        f.Board.Items.Add(Arrow(from: "b1", x2: 400, y2: 300));

        // a click well off the line, inside the arrow's bounding rectangle
        Assert.Null(f.Scene.ArrowAt(380, 40));
    }

    // --- storage ----------------------------------------------------------

    [Fact]
    public void AConnectorRoundTrips()
    {
        using var dir = new TempDir();
        var store = BoardStore.Load(dir.Path);
        var board = store.Create("flow");
        board.Items.Add(Box("b1", 0, 0));
        board.Items.Add(Box("b2", 400, 0));
        board.Items.Add(Arrow(from: "b1", to: "b2"));
        store.Save(board);

        var reread = Assert.Single(BoardStore.Load(dir.Path).Boards);
        var arrow = reread.Items.Single(i => i.Kind == "arrow");

        Assert.Equal("b1", arrow.From);
        Assert.Equal("b2", arrow.To);
    }

    [Fact]
    public void UndoRestoresATie()
    {
        var board = new Board { Id = "b", Name = "flow" };
        board.Items.Add(Box("b1", 0, 0));
        var arrow = Arrow(from: "b1");
        board.Items.Add(arrow);

        var history = new History();
        history.Record(board);
        arrow.From = null;
        history.Undo(board);

        Assert.Equal("b1", board.Items.Single(i => i.Kind == "arrow").From);
    }
}

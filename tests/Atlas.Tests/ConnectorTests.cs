using SkiaSharp;

namespace Atlas.Tests;

/// <summary>arrows tied to the things they point at.
///
/// Two things to hold. A tied end has no stored position: it is worked out
/// from the item's box every frame, so nothing has to update a connector when
/// a box moves, because there is nothing to update. And an end lands on one
/// of four anchors - top, right, bottom, left - never anywhere else. An
/// endpoint free to sit anywhere on an edge slides about as either box moves,
/// which is what made the first attempt unreadable.</summary>
public class ConnectorTests
{
    static BoardItem Box(string id, float x, float y, float w = 100, float h = 60) =>
        new() { Id = id, Kind = "shape", X = x, Y = y, W = w, H = h };

    static BoardItem Arrow(string? from = null, string? to = null,
        float x = 0, float y = 0, float x2 = 500, float y2 = 0,
        int fromSide = -1, int toSide = -1) =>
        new()
        {
            Id = "a1", Kind = "arrow", X = x, Y = y, X2 = x2, Y2 = y2,
            From = from, To = to, FromSide = fromSide, ToSide = toSide,
        };

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

    // --- four anchors, and only four --------------------------------------

    [Fact]
    public void TheAnchorsAreTheMiddlesOfTheFourSides()
    {
        using var f = new Fixture();
        var box = Box("b1", 100, 200, w: 100, h: 60);   // 100,200 to 200,260
        f.Board.Items.Add(box);

        Assert.Equal(new SKPoint(150, 200), f.Scene.AnchorOf(box, Scene.Top));
        Assert.Equal(new SKPoint(200, 230), f.Scene.AnchorOf(box, Scene.Right));
        Assert.Equal(new SKPoint(150, 260), f.Scene.AnchorOf(box, Scene.Bottom));
        Assert.Equal(new SKPoint(100, 230), f.Scene.AnchorOf(box, Scene.Left));
    }

    [Fact]
    public void ATiedEndIsAlwaysOnAnAnchorWhereverTheOtherEndIs()
    {
        using var f = new Fixture();
        var box = Box("b1", 0, 0);
        f.Board.Items.Add(box);
        var arrow = Arrow(from: "b1");
        f.Board.Items.Add(arrow);

        var anchors = new[]
        {
            f.Scene.AnchorOf(box, Scene.Top), f.Scene.AnchorOf(box, Scene.Right),
            f.Scene.AnchorOf(box, Scene.Bottom), f.Scene.AnchorOf(box, Scene.Left),
        };

        // sweep the far end all the way round; the tied end may only ever be
        // one of four points, never somewhere along an edge
        for (int deg = 0; deg < 360; deg += 7)
        {
            double rad = deg * Math.PI / 180;
            arrow.X2 = 50 + (float)(600 * Math.Cos(rad));
            arrow.Y2 = 30 + (float)(600 * Math.Sin(rad));

            var a = f.Scene.ArrowEnds(arrow).A;
            Assert.Contains(anchors, p => Math.Abs(p.X - a.X) < 0.01f && Math.Abs(p.Y - a.Y) < 0.01f);
        }
    }

    [Theory]
    [InlineData(Scene.Top)]
    [InlineData(Scene.Right)]
    [InlineData(Scene.Bottom)]
    [InlineData(Scene.Left)]
    public void AChosenSideStaysChosen(int side)
    {
        using var f = new Fixture();
        var box = Box("b1", 0, 0);
        f.Board.Items.Add(box);
        // the far end is off to the right, which is not the side asked for
        var arrow = Arrow(from: "b1", x2: 900, y2: 30, fromSide: side);
        f.Board.Items.Add(arrow);

        Assert.Equal(f.Scene.AnchorOf(box, side), f.Scene.ArrowEnds(arrow).A);
    }

    [Theory]
    [InlineData(50, -40, Scene.Top)]
    [InlineData(140, 30, Scene.Right)]
    [InlineData(50, 100, Scene.Bottom)]
    [InlineData(-40, 30, Scene.Left)]
    public void AnEndAttachesToTheAnchorItWasDroppedNearest(float x, float y, int side)
    {
        using var f = new Fixture();
        var box = Box("b1", 0, 0);
        f.Board.Items.Add(box);

        Assert.Equal(side, f.Scene.NearestSide(box, x, y));
    }

    [Fact]
    public void AnUnchosenSideFacesWhateverItPointsAt()
    {
        using var f = new Fixture();
        var box = Box("b1", 0, 0);
        f.Board.Items.Add(box);
        var arrow = Arrow(from: "b1");
        f.Board.Items.Add(arrow);

        arrow.X2 = 50; arrow.Y2 = -900;
        Assert.Equal(f.Scene.AnchorOf(box, Scene.Top), f.Scene.ArrowEnds(arrow).A);

        arrow.X2 = 50; arrow.Y2 = 900;
        Assert.Equal(f.Scene.AnchorOf(box, Scene.Bottom), f.Scene.ArrowEnds(arrow).A);
    }

    [Fact]
    public void AnAnchorFollowsAResizeRatherThanStayingWhereItWas()
    {
        using var f = new Fixture();
        var box = Box("b1", 0, 0);
        f.Board.Items.Add(box);
        var arrow = Arrow(from: "b1", fromSide: Scene.Bottom);
        f.Board.Items.Add(arrow);

        box.H = 400;

        Assert.Equal(new SKPoint(50, 400), f.Scene.ArrowEnds(arrow).A);
    }

    [Fact]
    public void AnEllipseHasTheSameFourAnchors()
    {
        using var f = new Fixture();
        var oval = new BoardItem { Id = "e1", Kind = "ellipse", X = 0, Y = 0, W = 100, H = 60 };
        f.Board.Items.Add(oval);

        // every element gets the same four, whatever shape it is drawn as
        Assert.Equal(new SKPoint(50, 0), f.Scene.AnchorOf(oval, Scene.Top));
        Assert.Equal(new SKPoint(100, 30), f.Scene.AnchorOf(oval, Scene.Right));
    }

    // --- only a node connects ---------------------------------------------

    [Fact]
    public void LandingOnANodeFindsIt()
    {
        using var f = new Fixture();
        var box = Box("b1", 0, 0);            // anchors at 50,0 100,30 50,60 0,30
        f.Board.Items.Add(box);

        var hit = f.Scene.AnchorAt(100, 30);

        Assert.NotNull(hit);
        Assert.Equal("b1", hit!.Value.Item.Id);
        Assert.Equal(Scene.Right, hit.Value.Side);
    }

    [Fact]
    public void LandingInsideTheElementIsNotLandingOnANode()
    {
        using var f = new Fixture();
        f.Board.Items.Add(Box("b1", 0, 0, w: 400, h: 300));

        // dead centre of a big box: well inside it, nowhere near a node.
        // A line crossing a box is often just a line crossing a box
        Assert.Null(f.Scene.AnchorAt(200, 150));
    }

    [Fact]
    public void LandingOnEmptyCanvasFindsNothing()
    {
        using var f = new Fixture();
        f.Board.Items.Add(Box("b1", 0, 0));

        Assert.Null(f.Scene.AnchorAt(900, 900));
    }

    [Fact]
    public void ANodeHasSomeReachSoItCanBeHit()
    {
        using var f = new Fixture();
        f.Board.Items.Add(Box("b1", 0, 0));

        // a few pixels off the right node still counts; aiming has to be
        // possible with a mouse
        Assert.NotNull(f.Scene.AnchorAt(104, 33));
    }

    [Fact]
    public void ArrowsAndStrokesHaveNoNodesToLandOn()
    {
        using var f = new Fixture();
        f.Board.Items.Add(Arrow(x: 0, y: 0, x2: 200, y2: 0));

        var ink = new BoardItem { Id = "s1", Kind = "stroke", Weight = 2 };
        Strokes.Add(ink, 300, 0, minStep: 0);
        Strokes.Add(ink, 400, 0, minStep: 0);
        f.Board.Items.Add(ink);

        // a connector ties to elements, not to other lines
        Assert.Null(f.Scene.AnchorAt(100, 0));
        Assert.Null(f.Scene.AnchorAt(350, 0));
    }

    [Fact]
    public void TheNearestNodeWinsWhenTwoAreClose()
    {
        using var f = new Fixture();
        f.Board.Items.Add(Box("b1", 0, 0, w: 40, h: 40));    // right node at 40,20
        f.Board.Items.Add(Box("b2", 44, 0, w: 40, h: 40));   // left node at 44,20

        Assert.Equal("b2", f.Scene.AnchorAt(43, 20)!.Value.Item.Id);
        Assert.Equal("b1", f.Scene.AnchorAt(41, 20)!.Value.Item.Id);
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

    // --- what happens to a tie when the thing it points at goes -----------

    /// <summary>a tied end is not where the arrow says it is, so anything
    /// that hunts for an arrow has to ask where it is drawn. The eraser did
    /// not, and rubbed out the place an arrow used to be.</summary>
    [Fact]
    public void TheEraserFindsATiedArrowWhereItIsDrawn()
    {
        using var f = new Fixture();
        var box = Box("b1", 0, 0);
        var arrow = Arrow(from: "b1", x: 0, y: 0, x2: 400, y2: 0, fromSide: Scene.Right);
        f.Board.Items.Add(box);
        f.Board.Items.Add(arrow);

        // the box moves, and with it the end tied to it
        box.Y = 600;

        var (a, b) = f.Scene.ArrowEnds(arrow);
        float midX = (a.X + b.X) / 2, midY = (a.Y + b.Y) / 2;

        Assert.Contains(arrow, f.Scene.ItemsNear(midX, midY, 8));
        Assert.DoesNotContain(arrow, f.Scene.ItemsNear(200, 0, 8));   // where it was
    }

    /// <summary>deleting what an arrow points at leaves the arrow where it
    /// was drawn. The tie falls back to the stored coordinates, and those are
    /// from when the tie was made - so a connector used to snap back across
    /// the board the moment you deleted the box it pointed at.</summary>
    [Fact]
    public void DeletingWhatAnArrowPointsAtLeavesItWhereItWas()
    {
        using var f = new Fixture();
        var box = Box("b1", 0, 0);
        var arrow = Arrow(from: "b1", x: 0, y: 0, x2: 400, y2: 0, fromSide: Scene.Right);
        f.Board.Items.Add(box);
        f.Board.Items.Add(arrow);

        box.Y = 600;
        var (before, _) = f.Scene.ArrowEnds(arrow);

        f.Scene.Remove(f.Board, [box]);

        Assert.Null(arrow.From);
        Assert.Equal(-1, arrow.FromSide);
        var (after, _) = f.Scene.ArrowEnds(arrow);
        Assert.Equal(before.X, after.X, 2);
        Assert.Equal(before.Y, after.Y, 2);
    }

    /// <summary>the other end is none of its business.</summary>
    [Fact]
    public void DeletingOneEndLeavesTheOtherTieAlone()
    {
        using var f = new Fixture();
        var from = Box("b1", 0, 0);
        var to = Box("b2", 400, 0);
        var arrow = Arrow(from: "b1", to: "b2");
        f.Board.Items.Add(from);
        f.Board.Items.Add(to);
        f.Board.Items.Add(arrow);

        f.Scene.Remove(f.Board, [from]);

        Assert.Null(arrow.From);
        Assert.Equal("b2", arrow.To);
    }

    /// <summary>and an arrow going at the same time is not resurrected by
    /// being written to on its way out.</summary>
    [Fact]
    public void RemovingABoxAndItsArrowTogetherTakesBoth()
    {
        using var f = new Fixture();
        var box = Box("b1", 0, 0);
        var arrow = Arrow(from: "b1");
        f.Board.Items.Add(box);
        f.Board.Items.Add(arrow);

        Assert.Equal(2, f.Scene.Remove(f.Board, [box, arrow]));
        Assert.Empty(f.Board.Items);
    }

    // --- how much room an arrow takes up ----------------------------------

    /// <summary>an arrow's W is whatever the default was when it was made and
    /// its H is nothing, so anything reading its stored box reads six hundred
    /// units of empty canvas beside the tail.</summary>
    [Fact]
    public void AnArrowIsAsBigAsTheLineItDraws()
    {
        using var f = new Fixture();
        var arrow = Arrow(x: 0, y: 0, x2: -200, y2: -300);
        f.Board.Items.Add(arrow);

        var box = f.Scene.BoundsOf(arrow);

        Assert.Equal(new SKRect(-200, -300, 0, 0), box);
    }

    [Fact]
    public void ABandOverAnArrowTakesIt()
    {
        using var f = new Fixture();
        var arrow = Arrow(x: 0, y: 0, x2: 0, y2: -400);      // straight up
        f.Board.Items.Add(arrow);

        Assert.Contains(arrow, f.Scene.ItemsIn(new SKRect(-50, -450, 50, -350)));
    }

    /// <summary>and a band well clear of it does not, however far the
    /// phantom box used to reach.</summary>
    [Fact]
    public void ABandBesideAnArrowLeavesIt()
    {
        using var f = new Fixture();
        var arrow = Arrow(x: 0, y: 0, x2: 0, y2: -400);
        f.Board.Items.Add(arrow);

        Assert.DoesNotContain(arrow, f.Scene.ItemsIn(new SKRect(300, -50, 500, 50)));
    }

    /// <summary>fitting the board frames the line, not the phantom. An arrow
    /// that goes up and to the left used to be fitted entirely off screen.</summary>
    [Fact]
    public void FittingTheBoardFramesBothEndsOfAnArrow()
    {
        using var f = new Fixture();
        f.Board.Items.Add(Arrow(x: 0, y: 0, x2: -400, y2: -200));

        f.Scene.FitBoard(800, 600);

        Assert.Equal(-200, f.Scene.CamX, 1);
        Assert.Equal(-100, f.Scene.CamY, 1);
    }

    /// <summary>a tied end counts where it is drawn, which is the only way
    /// fitting a flowchart frames the flowchart.</summary>
    [Fact]
    public void TheBoundsOfATiedArrowFollowTheBox()
    {
        using var f = new Fixture();
        var box = Box("b1", 0, 0);
        var arrow = Arrow(from: "b1", x: 0, y: 0, x2: 400, y2: 0, fromSide: Scene.Right);
        f.Board.Items.Add(box);
        f.Board.Items.Add(arrow);

        box.Y = 600;

        // the tied end is now the lowest point on the line, and the box has
        // to reach it. It used to stop at the stale Y the arrow was made with
        var (a, b) = f.Scene.ArrowEnds(arrow);
        var bounds = f.Scene.BoundsOf(arrow);
        Assert.Equal(Math.Max(a.Y, b.Y), bounds.Bottom, 1);
        Assert.Equal(Math.Min(a.Y, b.Y), bounds.Top, 1);
    }
}

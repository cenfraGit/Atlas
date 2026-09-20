using SkiaSharp;

namespace Atlas.Tests;

/// <summary>freehand strokes. A stroke is the first board item that is not a
/// box, so the thing worth pinning down is that it still behaves like one
/// everywhere else: it has bounds, it can be picked, moved, swept up by the
/// rubberband, saved and undone.</summary>
public class StrokeTests
{
    static BoardItem Stroke(params (float X, float Y)[] points)
    {
        var it = new BoardItem { Id = "s1", Kind = "stroke", Weight = Strokes.DefaultWeight };
        foreach (var (x, y) in points) Strokes.Add(it, x, y, minStep: 0);
        return it;
    }

    // --- points ----------------------------------------------------------

    [Fact]
    public void AStrokeCollectsThePointsItIsGiven()
    {
        var it = Stroke((0, 0), (10, 0), (10, 10));

        Assert.Equal(3, Strokes.CountOf(it));
        Assert.Equal(new SKPoint(10, 10), Strokes.PointAt(it, 2));
    }

    [Fact]
    public void SamplesTooCloseTogetherAreDropped()
    {
        var it = new BoardItem { Kind = "stroke" };

        Assert.True(Strokes.Add(it, 0, 0, minStep: 5));
        Assert.False(Strokes.Add(it, 1, 1, minStep: 5));    // a mouse reports far
        Assert.False(Strokes.Add(it, 2, 2, minStep: 5));    // more than a curve needs
        Assert.True(Strokes.Add(it, 10, 0, minStep: 5));

        Assert.Equal(2, Strokes.CountOf(it));
    }

    [Fact]
    public void AnEmptyStrokeIsHarmless()
    {
        var it = new BoardItem { Kind = "stroke" };

        Assert.Equal(0, Strokes.CountOf(it));
        Assert.Equal(float.MaxValue, Strokes.DistanceTo(it, 5, 5));
        Strokes.Reframe(it);
        Assert.Equal(0, it.W);
        using var path = Strokes.PathOf(it);
        Assert.True(path.IsEmpty);
    }

    // --- bounds ----------------------------------------------------------

    [Fact]
    public void TheBoundsFollowThePoints()
    {
        var it = Stroke((10, 20), (110, 20), (110, 70));

        // grown by half the pen width, or the ends fall outside the box
        float pad = Strokes.DefaultWeight / 2 + 1;
        Assert.Equal(10 - pad, it.X, 2);
        Assert.Equal(20 - pad, it.Y, 2);
        Assert.Equal(100 + pad * 2, it.W, 2);
        Assert.Equal(50 + pad * 2, it.H, 2);
    }

    [Fact]
    public void EveryPointIsInsideTheBounds()
    {
        var it = Stroke((5, 5), (-30, 80), (200, -40), (60, 60));

        for (int i = 0; i < Strokes.CountOf(it); i++)
        {
            var p = Strokes.PointAt(it, i);
            Assert.InRange(p.X, it.X, it.X + it.W);
            Assert.InRange(p.Y, it.Y, it.Y + it.H);
        }
    }

    [Fact]
    public void ASingleDotStillHasABox()
    {
        var it = Stroke((50, 50));

        Assert.True(it.W > 0 && it.H > 0, "a dot with no area cannot be picked");
    }

    [Fact]
    public void MovingAStrokeMovesItsInkAndItsBox()
    {
        var it = Stroke((0, 0), (10, 10));
        float x = it.X, y = it.Y;

        Strokes.Move(it, 100, -50);

        Assert.Equal(x + 100, it.X, 2);
        Assert.Equal(y - 50, it.Y, 2);
        Assert.Equal(new SKPoint(100, -50), Strokes.PointAt(it, 0));
        Assert.Equal(new SKPoint(110, -40), Strokes.PointAt(it, 1));
    }

    [Fact]
    public void MovingByNothingChangesNothing()
    {
        var it = Stroke((3, 4), (9, 12));
        var before = it.Points!.ToList();

        Strokes.Move(it, 0, 0);

        Assert.Equal(before, it.Points);
    }

    // --- hit testing -----------------------------------------------------

    [Fact]
    public void APointOnTheLineIsAtNoDistance()
    {
        var it = Stroke((0, 0), (100, 0));
        Assert.Equal(0, Strokes.DistanceTo(it, 50, 0), 3);
    }

    [Fact]
    public void DistanceIsMeasuredToTheNearestSegment()
    {
        var it = Stroke((0, 0), (100, 0), (100, 100));

        Assert.Equal(10, Strokes.DistanceTo(it, 50, 10), 2);      // off the first leg
        Assert.Equal(10, Strokes.DistanceTo(it, 90, 50), 2);      // off the second
    }

    [Fact]
    public void DistancePastAnEndIsToTheEndItself()
    {
        var it = Stroke((0, 0), (100, 0));

        // not to the infinite line the segment lies on
        Assert.Equal(50, Strokes.DistanceTo(it, 150, 0), 2);
    }

    [Fact]
    public void TouchingNeedsTheInkNotJustTheBox()
    {
        // an L: the inside of its corner is well within the bounding box but
        // nowhere near the stroke
        var it = Stroke((0, 0), (0, 100), (100, 100));

        Assert.True(Strokes.Touches(it, 0, 50, 3), "a point on the ink should touch");
        Assert.False(Strokes.Touches(it, 80, 20, 3), "empty space inside the box should not");
    }

    [Fact]
    public void AFatterPenIsEasierToHit()
    {
        var thin = Stroke((0, 0), (100, 0));
        var fat = Stroke((0, 0), (100, 0));
        fat.Weight = 20;
        Strokes.Reframe(fat);

        Assert.False(Strokes.Touches(thin, 50, 8, 1));
        Assert.True(Strokes.Touches(fat, 50, 8, 1), "a fat stroke should hit as wide as it looks");
    }

    // --- on a board ------------------------------------------------------

    static (Scene Scene, Board Board, TempDir Repo) Boarded()
    {
        var repo = SampleRepo.Build();
        var board = new Board { Id = "b", Name = "drawing" };
        var scene = new Scene(Scanner.Build(repo.Path)) { ActiveBoard = board, CamS = 1f };
        return (scene, board, repo);
    }

    [Fact]
    public void AStrokeIsPickedByItsInk()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            board.Items.Add(Stroke((0, 0), (0, 100), (100, 100)));

            Assert.NotNull(scene.StrokeAt(0, 50));
            Assert.Null(scene.StrokeAt(80, 20));       // inside the box, off the ink
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void AStrokeDoesNotSwallowAClickAsABox()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            board.Items.Add(Stroke((0, 0), (0, 100), (100, 100)));

            // the same rule arrows follow: a click in the empty part of the
            // box belongs to the canvas
            Assert.Null(scene.ItemAt(80, 20));
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void TheRubberbandSweepsStrokesUpLikeAnythingElse()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            board.Items.Add(Stroke((10, 10), (60, 60)));
            board.Items.Add(new BoardItem { Id = "n", Kind = "note", X = 400, Y = 0, W = 200, H = 80 });

            var swept = scene.ItemsIn(new SKRect(0, 0, 200, 200)).Select(i => i.Id).ToList();

            Assert.Equal(["s1"], swept);
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void TheEraserTakesWholeStrokesItTouches()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var a = Stroke((0, 0), (100, 0));
            var b = Stroke((0, 500), (100, 500));
            b.Id = "s2";
            board.Items.Add(a);
            board.Items.Add(b);

            var hit = scene.StrokesNear(50, 2, radius: 6);

            Assert.Same(a, Assert.Single(hit));
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void TheEraserMissesWhatItIsNotOver()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            board.Items.Add(Stroke((0, 0), (100, 0)));

            Assert.Empty(scene.StrokesNear(50, 400, radius: 6));
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void AStrokeCountsTowardsTheBoardsBounds()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            board.Items.Add(Stroke((-200, -100), (800, 600)));

            var b = scene.ContentBounds();

            Assert.True(b.Left <= -200 && b.Top <= -100);
            Assert.True(b.Right >= 800 && b.Bottom >= 600);
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void AStrokeIsNotResizable() =>
        // resizing would have to scale every point; the grip would lie
        Assert.False(Scene.Resizable(new BoardItem { Kind = "stroke" }));

    // --- the splitting eraser --------------------------------------------

    /// <summary>a horizontal line of points one unit apart.</summary>
    static BoardItem Line(int points = 21)
    {
        var it = new BoardItem { Id = "s1", Kind = "stroke", Weight = 1 };
        for (int i = 0; i < points; i++) Strokes.Add(it, i * 10, 0, minStep: 0);
        return it;
    }

    [Fact]
    public void ABiteOutOfTheMiddleLeavesTwoStrokes()
    {
        var pieces = Strokes.Erase(Line(), x: 100, y: 0, radius: 15);

        Assert.Equal(2, pieces.Count);
        Assert.All(pieces, p => Assert.Equal("stroke", p.Kind));
    }

    [Fact]
    public void TheTwoHalvesAreOnEitherSideOfTheHole()
    {
        var pieces = Strokes.Erase(Line(), x: 100, y: 0, radius: 15);

        var left = pieces[0];
        var right = pieces[1];

        Assert.True(left.X + left.W < 100, "the left piece should end before the hole");
        Assert.True(right.X > 100, "the right piece should start after the hole");
    }

    [Fact]
    public void RubbingAnEndOffLeavesOneStroke()
    {
        var pieces = Strokes.Erase(Line(), x: 0, y: 0, radius: 25);

        var only = Assert.Single(pieces);
        Assert.True(only.X > 0, "the rubbed end should be gone");
    }

    [Fact]
    public void RubbingTheWholeThingLeavesNothing()
    {
        Assert.Empty(Strokes.Erase(Line(points: 3), x: 10, y: 0, radius: 500));
    }

    [Fact]
    public void RubbingNowhereNearLeavesItWhole()
    {
        var only = Assert.Single(Strokes.Erase(Line(), x: 0, y: 900, radius: 10));

        Assert.Equal(21, Strokes.CountOf(only));
    }

    [Fact]
    public void ALoneSurvivingPointIsDroppedRatherThanLeftAsLitter()
    {
        // a dot left behind where a line used to be is not a stroke
        var pieces = Strokes.Erase(Line(points: 3), x: 10, y: 0, radius: 5);

        Assert.All(pieces, p => Assert.True(Strokes.CountOf(p) >= 2));
    }

    [Fact]
    public void ThePiecesKeepTheStrokesLook()
    {
        var it = Line();
        it.Color = "#d95c5c";
        it.Weight = 7;
        Strokes.Reframe(it);

        foreach (var piece in Strokes.Erase(it, 100, 0, 15))
        {
            Assert.Equal("#d95c5c", piece.Color);
            Assert.Equal(7, piece.Weight);
        }
    }

    [Fact]
    public void EachPieceGetsItsOwnId()
    {
        var pieces = Strokes.Erase(Line(), 100, 0, 15);

        Assert.Equal(2, pieces.Select(p => p.Id).Distinct().Count());
        Assert.DoesNotContain(pieces, p => p.Id == "s1");
    }

    [Fact]
    public void EachPieceIsFramedAroundItsOwnInk()
    {
        foreach (var piece in Strokes.Erase(Line(), 100, 0, 15))
            for (int i = 0; i < Strokes.CountOf(piece); i++)
            {
                var p = Strokes.PointAt(piece, i);
                Assert.InRange(p.X, piece.X, piece.X + piece.W);
                Assert.InRange(p.Y, piece.Y, piece.Y + piece.H);
            }
    }

    [Fact]
    public void AFatterStrokeIsRubbedWiderJustAsItIsHitWider()
    {
        var thin = Line();
        var fat = Line();
        fat.Weight = 30;
        Strokes.Reframe(fat);

        // same rub, same place: the fat one loses more of itself
        int thinLeft = Strokes.Erase(thin, 100, 0, 5).Sum(Strokes.CountOf);
        int fatLeft = Strokes.Erase(fat, 100, 0, 5).Sum(Strokes.CountOf);

        Assert.True(fatLeft < thinLeft, $"fat kept {fatLeft}, thin kept {thinLeft}");
    }

    [Fact]
    public void ErasingAnEmptyStrokeIsHarmless() =>
        Assert.Empty(Strokes.Erase(new BoardItem { Kind = "stroke" }, 0, 0, 10));

    [Fact]
    public void RepeatedRubsFragmentButNeverResurrect()
    {
        var it = Line(41);
        var pieces = new List<BoardItem> { it };

        for (int at = 50; at < 350; at += 100)
            pieces = pieces.SelectMany(p => Strokes.Erase(p, at, 0, 12)).ToList();

        Assert.True(pieces.Count > 1, "rubbing in several places should fragment it");
        Assert.All(pieces, p => Assert.True(Strokes.CountOf(p) >= 2));
        // nothing survives inside a hole
        foreach (var p in pieces)
            for (int i = 0; i < Strokes.CountOf(p); i++)
                Assert.DoesNotContain(new[] { 50, 150, 250 },
                    hole => Math.Abs(Strokes.PointAt(p, i).X - hole) < 6);
    }

    // --- persistence and undo --------------------------------------------

    [Fact]
    public void AStrokeSurvivesBeingSavedAndReloaded()
    {
        using var dir = new TempDir();
        var store = BoardStore.Load(dir.Path);
        var board = store.Create("drawing");

        var it = Stroke((1.5f, 2.5f), (30, 40), (77, 12));
        it.Color = "#ff8800";
        it.Weight = 7;
        board.Items.Add(it);
        store.Save(board);

        var reread = Assert.Single(BoardStore.Load(dir.Path).Boards).Items[0];

        Assert.Equal("stroke", reread.Kind);
        Assert.Equal(3, Strokes.CountOf(reread));
        Assert.Equal(it.Points, reread.Points);
        Assert.Equal("#ff8800", reread.Color);
        Assert.Equal(7, reread.Weight);
    }

    [Fact]
    public void ABoardWithNoStrokesWritesNoPointsField()
    {
        using var dir = new TempDir();
        var store = BoardStore.Load(dir.Path);
        var board = store.Create("plain");
        board.Items.Add(new BoardItem { Id = "n", Kind = "note", Text = "hello" });
        store.Save(board);

        // a null list must not become "points": null in every note ever saved
        Assert.DoesNotContain("points", File.ReadAllText(board.Path));
    }

    [Fact]
    public void UndoBringsAnErasedStrokeBack()
    {
        var board = new Board { Id = "b", Name = "drawing" };
        board.Items.Add(Stroke((0, 0), (50, 50)));
        var history = new History();

        history.Record(board);
        board.Items.Clear();
        Assert.True(history.Undo(board));

        // snapshot undo means a new item kind is undoable without writing
        // anything for it
        var back = Assert.Single(board.Items);
        Assert.Equal("stroke", back.Kind);
        Assert.Equal(2, Strokes.CountOf(back));
        Assert.Equal(new SKPoint(50, 50), Strokes.PointAt(back, 1));
    }

    // --- the path --------------------------------------------------------

    [Fact]
    public void ThePathCoversTheStroke()
    {
        var it = Stroke((0, 0), (50, 20), (100, 0), (150, 40));
        using var path = Strokes.PathOf(it);

        Assert.False(path.IsEmpty);
        // the smoothing runs through the midpoints, so the drawn curve stays
        // inside the stroke's own box
        var b = path.Bounds;
        Assert.InRange(b.Left, it.X, it.X + it.W);
        Assert.InRange(b.Right, it.X, it.X + it.W);
        Assert.InRange(b.Top, it.Y, it.Y + it.H);
        Assert.InRange(b.Bottom, it.Y, it.Y + it.H);
    }

    [Fact]
    public void TwoPointsAreJustALine()
    {
        var it = Stroke((0, 0), (100, 100));
        using var path = Strokes.PathOf(it);

        Assert.Equal(0, path.Bounds.Left, 2);
        Assert.Equal(100, path.Bounds.Right, 2);
    }
}

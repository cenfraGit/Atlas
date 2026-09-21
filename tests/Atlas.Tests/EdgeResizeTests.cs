using SkiaSharp;

namespace Atlas.Tests;

/// <summary>dragging a wall rather than a corner.
///
/// A corner moves two edges at once, which is what you want when the shape
/// matters and a nuisance when only one dimension does. A wall moves one and
/// leaves the other three.
///
/// A file window is the reason this exists: a window onto a three thousand
/// line file is unreadable, and the fix is to show fewer lines rather than
/// to squash them. Its walls clip the range instead of resizing anything.</summary>
public class EdgeResizeTests
{
    static (Scene Scene, Board Board, TempDir Repo) Boarded()
    {
        var repo = SampleRepo.Build();
        var board = new Board { Id = "b", Name = "walls" };
        var scene = new Scene(Scanner.Build(repo.Path)) { ActiveBoard = board, CamS = 1f };
        return (scene, board, repo);
    }

    static BoardItem Boxed(Board board, string kind = "shape")
    {
        var it = new BoardItem { Id = "s", Kind = kind, X = 0, Y = 0, W = 200, H = 120 };
        board.Items.Add(it);
        return it;
    }

    static BoardItem Window(Board board, int from = 0, int to = 99)
    {
        var it = new BoardItem
        {
            Id = "f", Kind = "file", File = SampleRepo.LongFile,
            X = 0, Y = 0, W = 620, Line = from, EndLine = to,
        };
        board.Items.Add(it);
        return it;
    }

    // --- which walls exist ---------------------------------------------------

    [Theory]
    [InlineData("shape")]
    [InlineData("ellipse")]
    [InlineData("diamond")]
    [InlineData("note")]
    [InlineData("image")]
    public void ABoxHasAllFour(string kind) =>
        Assert.Equal(4, Scene.EdgesOf(new BoardItem { Kind = kind }).Count());

    /// <summary>a label's height is its words, so there is nothing for a top
    /// or bottom handle to do. Its width is what the text wraps to.</summary>
    [Fact]
    public void ALabelHasOnlyItsSides()
    {
        var walls = Scene.EdgesOf(new BoardItem { Kind = "text" }).ToList();
        Assert.Equal(2, walls.Count);
        Assert.Contains(Scene.Left, walls);
        Assert.Contains(Scene.Right, walls);
    }

    /// <summary>a file window's width scales the whole card, so a side
    /// handle would be a zoom rather than a resize.</summary>
    [Fact]
    public void AFileWindowHasOnlyTopAndBottom()
    {
        var walls = Scene.EdgesOf(new BoardItem { Kind = "file" }).ToList();
        Assert.Equal(2, walls.Count);
        Assert.Contains(Scene.Top, walls);
        Assert.Contains(Scene.Bottom, walls);
    }

    [Fact]
    public void AnArrowHasNone() =>
        Assert.Empty(Scene.EdgesOf(new BoardItem { Kind = "arrow" }));

    // --- dragging one -------------------------------------------------------

    [Fact]
    public void TheRightWallChangesTheWidthAndNothingElse()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Boxed(board);
            scene.ResizeEdge(it, Scene.Right, 300, 999);

            Assert.Equal(300, it.W);
            Assert.Equal(0, it.X);
            Assert.Equal(0, it.Y);
            Assert.Equal(120, it.H);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>a left or top wall moves the origin, because the opposite
    /// edge is the one staying put.</summary>
    [Fact]
    public void TheLeftWallMovesTheOriginAndKeepsTheRightEdge()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Boxed(board);
            scene.ResizeEdge(it, Scene.Left, 50, 999);

            Assert.Equal(50, it.X);
            Assert.Equal(150, it.W);
            Assert.Equal(200, it.X + it.W);
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void TheTopWallMovesTheOriginAndKeepsTheBottom()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Boxed(board);
            scene.ResizeEdge(it, Scene.Top, 999, 40);

            Assert.Equal(40, it.Y);
            Assert.Equal(80, it.H);
            Assert.Equal(0, it.X);
            Assert.Equal(200, it.W);
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void AWallCannotBePushedThroughTheOppositeOne()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Boxed(board);
            scene.ResizeEdge(it, Scene.Right, -500, 0);

            Assert.True(it.W >= 8, $"width collapsed to {it.W}");
            scene.ActiveBoard = null;
        }
    }

    // --- clipping a file window ---------------------------------------------

    [Fact]
    public void DraggingTheBottomOfAWindowShowsFewerLines()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Window(board, 0, 99);
            float step = scene.Data.LineH * (it.W / scene.Data.Files[scene.IndexOfPath(SampleRepo.LongFile)].W);
            float bottom = it.Y + Scene.WinHeadH + 100 * step;

            scene.ClipTo(it, Scene.Bottom, bottom - 20 * step);

            Assert.Equal(0, it.Line);
            Assert.Equal(79, it.EndLine);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>and the code that is left does not move while the wall goes
    /// through it, which means the item's Y follows the top handle.</summary>
    [Fact]
    public void DraggingTheTopOfAWindowClipsAndMovesItDown()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Window(board, 0, 99);
            float step = scene.Data.LineH * (it.W / scene.Data.Files[scene.IndexOfPath(SampleRepo.LongFile)].W);

            scene.ClipTo(it, Scene.Top, it.Y + 10 * step);

            Assert.Equal(10, it.Line);
            Assert.Equal(99, it.EndLine);
            Assert.Equal(10 * step, it.Y, 0.01);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>nothing is scaled: the lines that remain are the same height
    /// they were, which is what makes this clipping rather than squashing -
    /// and what makes the window cheaper to draw.</summary>
    [Fact]
    public void ClippingDoesNotScaleTheCode()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Window(board, 0, 99);
            float before = it.W;

            scene.ClipTo(it, Scene.Bottom, it.Y);

            Assert.Equal(before, it.W);
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void ClippingStopsAtOneLineRatherThanInvertingTheRange()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Window(board, 0, 99);

            scene.ClipTo(it, Scene.Bottom, -100000);
            Assert.True(it.EndLine >= it.Line, $"{it.Line}..{it.EndLine} is backwards");

            scene.ClipTo(it, Scene.Top, 100000);
            Assert.True(it.EndLine >= it.Line, $"{it.Line}..{it.EndLine} is backwards");
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void ClippingCannotRunPastTheEndOfTheFile()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Window(board, 0, 99);
            scene.ClipTo(it, Scene.Bottom, 1_000_000);

            Assert.Equal(SampleRepo.LongFileLines - 1, it.EndLine);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>an open-ended range has to be closed before the top of it
    /// can be clipped, or the window would jump to the whole file.</summary>
    [Fact]
    public void ClippingTheTopOfAnOpenRangeClosesIt()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Window(board, 0, -1);
            float step = scene.Data.LineH * (it.W / scene.Data.Files[scene.IndexOfPath(SampleRepo.LongFile)].W);

            scene.ClipTo(it, Scene.Top, it.Y + 5 * step);

            Assert.Equal(5, it.Line);
            Assert.Equal(SampleRepo.LongFileLines - 1, it.EndLine);
            scene.ActiveBoard = null;
        }
    }

    // --- finding a wall to drag ---------------------------------------------

    [Fact]
    public void AWallIsOnlyGrabbableOnAPickedItem()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Boxed(board);
            Assert.Null(scene.EdgeAt(200, 60));

            scene.Picked.Add(it.Id);
            var wall = scene.EdgeAt(200, 60);

            Assert.NotNull(wall);
            Assert.Equal(Scene.Right, wall!.Value.Edge);
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void TheMiddleOfAnItemIsNotAWall()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Boxed(board);
            scene.Picked.Add(it.Id);

            Assert.Null(scene.EdgeAt(100, 60));
            scene.ActiveBoard = null;
        }
    }

    /// <summary>the sides of a file window are not handles, so a click there
    /// is a click on the window.</summary>
    [Fact]
    public void AFileWindowHasNoSideHandles()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Window(board);
            scene.Picked.Add(it.Id);

            Assert.Null(scene.EdgeAt(it.X, 100));
            Assert.NotNull(scene.EdgeAt(it.X + it.W / 2, it.Y));
            scene.ActiveBoard = null;
        }
    }
}

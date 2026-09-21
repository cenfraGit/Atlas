namespace Atlas.Tests;

/// <summary>what a click lands on when things overlap.
///
/// Arrows and strokes are drawn after every box, so they are always on top
/// of one. Picking asked for boxes first and only fell back to the lines,
/// which is the opposite order - and a file window is a large box, so an
/// arrow drawn across one could not be selected at all. The only way to get
/// at it was to drag the boxes it joined out of the window first.</summary>
public class PickOrderTests
{
    static (Scene Scene, Board Board, TempDir Repo) Boarded()
    {
        var repo = SampleRepo.Build();
        var board = new Board { Id = "b", Name = "overlap" };
        var scene = new Scene(Scanner.Build(repo.Path)) { ActiveBoard = board, CamS = 1f };
        return (scene, board, repo);
    }

    /// <summary>a file window with two boxes on it and an arrow between
    /// them, all inside the window's rectangle - which is the case that
    /// could not be clicked.</summary>
    static (BoardItem Window, BoardItem A, BoardItem B, BoardItem Arrow) Layered(Board board)
    {
        var window = new BoardItem
        {
            Id = "w", Kind = "file", File = SampleRepo.LongFile,
            X = 0, Y = 0, W = 620, Line = 0, EndLine = 120,
        };
        var a = new BoardItem { Id = "a", Kind = "shape", X = 60, Y = 60, W = 120, H = 80 };
        var b = new BoardItem { Id = "b2", Kind = "shape", X = 400, Y = 60, W = 120, H = 80 };
        var arrow = new BoardItem
        {
            Id = "ar", Kind = "arrow", From = a.Id, To = b.Id,
            FromSide = Scene.Right, ToSide = Scene.Left,
            X = 180, Y = 100, X2 = 400, Y2 = 100,
        };
        board.Items.Add(window);
        board.Items.Add(a);
        board.Items.Add(b);
        board.Items.Add(arrow);
        return (window, a, b, arrow);
    }

    [Fact]
    public void AnArrowOverAFileWindowIsPicked()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var (_, _, _, arrow) = Layered(board);

            // halfway along the shaft, well inside the window
            var hit = scene.PickAt(290, 100);

            Assert.NotNull(hit);
            Assert.Equal(arrow.Id, hit!.Id);
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void AStrokeOverAFileWindowIsPickedToo()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            Layered(board);
            var ink = new BoardItem { Id = "s", Kind = "stroke", Weight = 3 };
            for (int i = 0; i <= 20; i++) Strokes.Add(ink, 200 + i * 5, 300, minStep: 0);
            board.Items.Add(ink);

            var hit = scene.PickAt(250, 300);

            Assert.NotNull(hit);
            Assert.Equal("s", hit!.Id);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>and the window is still pickable where nothing is over it,
    /// or this trades one unreachable thing for another.</summary>
    [Fact]
    public void TheWindowItselfIsStillPicked()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var (window, _, _, _) = Layered(board);

            var hit = scene.PickAt(300, 400);

            Assert.NotNull(hit);
            Assert.Equal(window.Id, hit!.Id);
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void ABoxOnTheWindowIsStillPicked()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var (_, a, _, _) = Layered(board);

            var hit = scene.PickAt(100, 80);

            Assert.NotNull(hit);
            Assert.Equal(a.Id, hit!.Id);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>an arrow crossing a shape wins, because that is what is on
    /// top. The shape is a click away on any part of it the line misses.</summary>
    [Fact]
    public void AnArrowCrossingAShapeBeatsIt()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var box = new BoardItem { Id = "s1", Kind = "shape", X = 0, Y = 0, W = 300, H = 200 };
            var arrow = new BoardItem
            {
                Id = "ar", Kind = "arrow", X = -50, Y = 100, X2 = 350, Y2 = 100,
            };
            board.Items.Add(box);
            board.Items.Add(arrow);

            Assert.Equal("ar", scene.PickAt(150, 100)!.Id);
            Assert.Equal("s1", scene.PickAt(150, 40)!.Id);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>a line of code is only reachable when nothing is drawn over
    /// it. An arrow across a window is over the code as much as a rectangle
    /// is, so clicking the arrow must not also annotate a line.</summary>
    [Fact]
    public void AnArrowOverCodeHidesTheLineUnderIt()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            Layered(board);

            Assert.Null(scene.LineInWindowAt(290, 100));
            Assert.NotNull(scene.LineInWindowAt(300, 400));
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void NothingUnderThePointIsStillNothing()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            Layered(board);
            Assert.Null(scene.PickAt(-400, -400));
            scene.ActiveBoard = null;
        }
    }
}

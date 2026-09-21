namespace Atlas.Tests;

/// <summary>a board window stays on the code it was opened on.
///
/// The window stores line numbers, and a line number stops meaning the same
/// thing the moment somebody inserts above it. Left alone, a window onto
/// lines 100-140 shows what used to be at 80-120 after twenty lines go in at
/// the top: different code, in the same place on the board, with every
/// rectangle and stroke drawn over it now pointing at the wrong thing.
///
/// Moving the *range* rather than the drawings is what fixes it. The window
/// keeps its position and its size, so anything drawn on top of it is still
/// over the same code without needing an anchor of its own.</summary>
[Collection("render")]
public class WindowDriftTests
{
    const string Path = "app/Drift.cs";

    /// <summary>a file with a method far enough down that inserting above it
    /// is a real move.</summary>
    static string Source(int leadingBlankLines)
    {
        var lf = ((char)10).ToString();
        var text = new System.Text.StringBuilder();
        text.Append("namespace Demo;").Append(lf).Append(lf);
        text.Append("public class Drift").Append(lf).Append('{').Append(lf);
        for (int i = 0; i < leadingBlankLines; i++) text.Append("    // filler").Append(lf);
        text.Append("    public int Target()").Append(lf);
        text.Append("    {").Append(lf);
        text.Append("        return 42;").Append(lf);
        text.Append("    }").Append(lf);
        text.Append('}').Append(lf);
        return text.ToString();
    }

    static int LineOf(Scene scene, string needle)
    {
        var lines = scene.ReadLines(Path);
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Contains(needle, StringComparison.Ordinal)) return i;
        return -1;
    }

    static (Scene Scene, Board Board, BoardItem Window, TempDir Repo) Opened(int filler)
    {
        var repo = SampleRepo.Build();
        repo.File(Path, Source(filler));

        var scene = new Scene(Scanner.Build(repo.Path));
        var board = new Board { Id = "b", Name = "drift" };
        scene.ActiveBoard = board;

        int at = LineOf(scene, "public int Target()");
        var window = new BoardItem
        {
            Id = "w", Kind = "file", File = Path, Key = scene.KeyFor(Path),
            X = 0, Y = 0, W = 620, Line = at, EndLine = at + 4,
        };
        board.Items.Add(window);
        scene.Reanchor(window);

        return (scene, board, window, repo);
    }

    /// <summary>the case that prompted this: lines go in above, and the
    /// window follows the method down instead of staying on the numbers.</summary>
    [Fact]
    public void InsertingAboveMovesTheRangeNotTheCode()
    {
        var (scene, board, window, repo) = Opened(filler: 3);
        using (repo)
        using (scene)
        {
            int before = window.Line;
            int span = window.EndLine - window.Line;

            repo.File(Path, Source(23));          // twenty more lines above
            Assert.True(scene.AnchorWindows(board), "the window did not move at all");

            Assert.Equal(before + 20, window.Line);
            Assert.Equal(span, window.EndLine - window.Line);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>and it really is on the method afterwards, not merely twenty
    /// lines lower - which is the same thing here only because the edit was
    /// a clean insertion.</summary>
    [Fact]
    public void TheWindowStillStartsOnTheDeclaration()
    {
        var (scene, board, window, repo) = Opened(filler: 3);
        using (repo)
        using (scene)
        {
            repo.File(Path, Source(23));
            scene.AnchorWindows(board);

            Assert.Equal(LineOf(scene, "public int Target()"), window.Line);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>the board position and size do not change. That is the whole
    /// point: a rectangle drawn over the window is still over the same code
    /// without having an anchor of its own.</summary>
    [Fact]
    public void TheWindowDoesNotMoveOnTheBoard()
    {
        var (scene, board, window, repo) = Opened(filler: 3);
        using (repo)
        using (scene)
        {
            float x = window.X, y = window.Y, w = window.W;

            repo.File(Path, Source(23));
            scene.AnchorWindows(board);

            Assert.Equal(x, window.X);
            Assert.Equal(y, window.Y);
            Assert.Equal(w, window.W);
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void AnUntouchedFileMovesNothing()
    {
        var (scene, board, window, repo) = Opened(filler: 3);
        using (repo)
        using (scene)
        {
            int before = window.Line;
            Assert.False(scene.AnchorWindows(board), "nothing changed, so nothing should have been saved");
            Assert.Equal(before, window.Line);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>a window made before windows had anchors gets one from
    /// wherever it currently points, the way EnsureKeys fills in a missing
    /// fingerprint.</summary>
    [Fact]
    public void AWindowWithNoAnchorGetsOne()
    {
        var (scene, board, window, repo) = Opened(filler: 3);
        using (repo)
        using (scene)
        {
            window.Symbol = null;
            window.Context = null;

            Assert.True(scene.AnchorWindows(board));
            Assert.NotNull(window.Context);
            Assert.Equal("Demo.Drift.Target(0)", window.Symbol);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>clipping is a deliberate move, so the anchor goes with it -
    /// otherwise the next open would drag the window back.</summary>
    [Fact]
    public void ClippingRecordsWhereTheWindowNowPoints()
    {
        var (scene, board, window, repo) = Opened(filler: 3);
        using (repo)
        using (scene)
        {
            var wasContext = window.Context;

            float step = scene.Data.LineH * (window.W / scene.Data.Files[scene.IndexOfPath(Path)].W);
            scene.ClipTo(window, Scene.Top, window.Y + 2 * step);
            scene.Reanchor(window);

            Assert.NotEqual(wasContext, window.Context);

            int moved = window.Line;
            Assert.False(scene.AnchorWindows(board), "re-anchoring undid a deliberate clip");
            Assert.Equal(moved, window.Line);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>a window whose code is gone is left where it is. Moving it
    /// to a guess is worse than leaving it somewhere you can see is wrong.</summary>
    [Fact]
    public void AnOrphanedWindowIsLeftAlone()
    {
        var (scene, board, window, repo) = Opened(filler: 3);
        using (repo)
        using (scene)
        {
            int before = window.Line;
            var lf = ((char)10).ToString();
            repo.File(Path, "namespace Demo;" + lf + lf + "public class Drift { }" + lf);

            scene.AnchorWindows(board);

            Assert.Equal(before, window.Line);
            scene.ActiveBoard = null;
        }
    }
}

/// <summary>a drawing pinned over a window follows the code inside it.
///
/// The window's own anchor handles lines inserted *above* it - the range
/// moves and everything on top stays right. This is the other half: lines
/// inserted *inside* the range, where the window keeps its first line and
/// the code below the insertion slides down out from under whatever was
/// drawn on it.</summary>
[Collection("render")]
public class PinnedItemTests
{
    const string Path = "app/Pinned.cs";

    static string Source(int fillerInsideFirst)
    {
        var lf = ((char)10).ToString();
        var t = new System.Text.StringBuilder();
        t.Append("namespace Demo;").Append(lf).Append(lf);
        t.Append("public class Pinned").Append(lf).Append('{').Append(lf);
        t.Append("    public int First()").Append(lf).Append("    {").Append(lf);
        for (int i = 0; i < fillerInsideFirst; i++) t.Append("        // filler").Append(lf);
        t.Append("        return 1;").Append(lf).Append("    }").Append(lf).Append(lf);
        t.Append("    public int Second()").Append(lf).Append("    {").Append(lf);
        t.Append("        return 2;").Append(lf).Append("    }").Append(lf);
        t.Append('}').Append(lf);
        return t.ToString();
    }

    static int LineOf(Scene scene, string needle)
    {
        var lines = scene.ReadLines(Path);
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Contains(needle, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>a window onto the whole class, with a rectangle laid over
    /// the second method - which is below the place the filler goes.</summary>
    static (Scene Scene, Board Board, BoardItem Window, BoardItem Mark, TempDir Repo) Drawn(int filler)
    {
        var repo = SampleRepo.Build();
        repo.File(Path, Source(filler));

        var scene = new Scene(Scanner.Build(repo.Path));
        var board = new Board { Id = "b", Name = "pinned" };
        scene.ActiveBoard = board;

        var f = scene.Data.Files[scene.IndexOfPath(Path)];
        var window = new BoardItem
        {
            Id = "w", Kind = "file", File = Path, Key = scene.KeyFor(Path),
            X = 0, Y = 0, W = 620, Line = 0, EndLine = -1,
        };
        board.Items.Add(window);
        scene.Reanchor(window);

        float step = scene.LineStepIn(window, f);
        var mark = new BoardItem
        {
            Id = "m", Kind = "shape", W = 200, H = 20,
            X = 10, Y = window.Y + Scene.WinHeadH + LineOf(scene, "public int Second()") * step,
        };
        board.Items.Add(mark);
        scene.PinOver(mark);

        return (scene, board, window, mark, repo);
    }

    [Fact]
    public void ADrawingOverAWindowIsPinnedToIt()
    {
        var (scene, _, window, mark, repo) = Drawn(1);
        using (repo)
        using (scene)
        {
            Assert.Equal(window.Id, mark.Host);
            Assert.NotNull(mark.Context);
            Assert.Equal("Demo.Pinned.Second(0)", mark.Symbol);
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void ADrawingOnBareCanvasIsNotPinned()
    {
        var (scene, board, _, _, repo) = Drawn(1);
        using (repo)
        using (scene)
        {
            var loose = new BoardItem { Id = "l", Kind = "shape", X = 5000, Y = 5000, W = 80, H = 40 };
            board.Items.Add(loose);
            scene.PinOver(loose);

            Assert.Null(loose.Host);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>the case this exists for: twenty lines go into the *first*
    /// method, the second slides down, and the rectangle over it follows.</summary>
    [Fact]
    public void InsertingInsideTheWindowMovesWhatIsDrawnBelowIt()
    {
        var (scene, board, window, mark, repo) = Drawn(1);
        using (repo)
        using (scene)
        {
            var f = scene.Data.Files[scene.IndexOfPath(Path)];
            float step = scene.LineStepIn(window, f);
            float before = mark.Y;

            repo.File(Path, Source(21));                 // twenty more, inside First()
            Assert.True(scene.AnchorBoard(board), "nothing followed the code");

            Assert.Equal(before + 20 * step, mark.Y, 0.5);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>and it is still over the same declaration afterwards, which
    /// is the thing that actually matters.</summary>
    [Fact]
    public void TheDrawingIsStillOverTheSameCode()
    {
        var (scene, board, window, mark, repo) = Drawn(1);
        using (repo)
        using (scene)
        {
            repo.File(Path, Source(21));
            scene.AnchorBoard(board);

            var f = scene.Data.Files[scene.IndexOfPath(Path)];
            float step = scene.LineStepIn(window, f);
            var (from, _) = scene.RangeOf(window, f);
            int under = from + (int)MathF.Round((mark.Y - (window.Y + Scene.WinHeadH)) / step);

            Assert.Equal(LineOf(scene, "public int Second()"), under);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>a drawing from a board made before any of this is pinned
    /// where it is, not moved to where it should have been.
    ///
    /// The board may already have drifted, and there is no record of where
    /// the drawing was meant to be - so taking its current position as the
    /// truth is the best available answer, and it stops the drift there.
    /// The same catching-up EnsureKeys does for fingerprints.</summary>
    [Fact]
    public void AnUnpinnedDrawingIsPinnedWhereItIsRatherThanMoved()
    {
        var (scene, board, _, mark, repo) = Drawn(1);
        using (repo)
        using (scene)
        {
            mark.Host = null;
            mark.Context = null;
            float before = mark.Y;

            repo.File(Path, Source(21));
            scene.AnchorBoard(board);

            Assert.Equal(before, mark.Y);
            Assert.NotNull(mark.Host);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>and once pinned it follows the next edit.</summary>
    [Fact]
    public void APinnedLooseDrawingFollowsTheNextEdit()
    {
        var (scene, board, window, mark, repo) = Drawn(1);
        using (repo)
        using (scene)
        {
            mark.Host = null;
            mark.Context = null;
            scene.AnchorBoard(board);           // picks it up where it is

            var f = scene.Data.Files[scene.IndexOfPath(Path)];
            float step = scene.LineStepIn(window, f);
            float before = mark.Y;

            repo.File(Path, Source(21));
            scene.AnchorBoard(board);

            Assert.Equal(before + 20 * step, mark.Y, 0.5);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>ink is points rather than a box, so it has to be translated
    /// rather than have its Y set.</summary>
    [Fact]
    public void InkFollowsToo()
    {
        var (scene, board, window, _, repo) = Drawn(1);
        using (repo)
        using (scene)
        {
            var f = scene.Data.Files[scene.IndexOfPath(Path)];
            float step = scene.LineStepIn(window, f);
            float at = window.Y + Scene.WinHeadH + LineOf(scene, "public int Second()") * step;

            var ink = new BoardItem { Id = "ink", Kind = "stroke", Weight = 3 };
            for (int i = 0; i <= 10; i++) Strokes.Add(ink, 20 + i * 8, at, minStep: 0);
            board.Items.Add(ink);
            scene.PinOver(ink);
            Assert.Equal(window.Id, ink.Host);

            float before = ink.Points![1];
            repo.File(Path, Source(21));
            scene.AnchorBoard(board);

            Assert.Equal(before + 20 * step, ink.Points[1], 0.5);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>a drawing whose window has been removed is let go rather
    /// than moved somewhere arbitrary.</summary>
    [Fact]
    public void LosingTheWindowUnpinsRatherThanMoves()
    {
        var (scene, board, window, mark, repo) = Drawn(1);
        using (repo)
        using (scene)
        {
            float before = mark.Y;
            board.Items.Remove(window);

            scene.AnchorBoard(board);

            Assert.Null(mark.Host);
            Assert.Equal(before, mark.Y);
            scene.ActiveBoard = null;
        }
    }
}

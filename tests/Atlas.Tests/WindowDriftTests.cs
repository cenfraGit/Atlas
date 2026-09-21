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

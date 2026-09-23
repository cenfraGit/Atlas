using SkiaSharp;

namespace Atlas.Tests;

/// <summary>the change view shows what a change removed, where it was.
///
/// Each file is cut at its removals into a stack of cropped windows, with the
/// old lines in a red block between them, so every window stays an ordinary
/// window and the rest of the app needs to know nothing about it.</summary>
[Collection("render")]
public class RemovedInChangeViewTests
{
    const string Path = "src/Thirty.cs";
    const int Lines = 30;

    static (Scene Scene, TempDir Repo) Repo()
    {
        var repo = SampleRepo.Build();
        repo.File(Path, string.Join("\n", Enumerable.Range(0, Lines).Select(n => $"// line {n}")));
        return (new Scene(Scanner.Build(repo.Path)), repo);
    }

    static ChangeSet Set(params FileChange[] files)
    {
        var set = new ChangeSet { Label = "c", Files = [.. files] };
        set.Index();
        return set;
    }

    static FileChange Removing(string path, params (int At, int OldLine, string[] Lines)[] blocks)
    {
        var c = new FileChange(path, 0, blocks.Sum(b => b.Lines.Length));
        foreach (var (at, old, lines) in blocks)
        {
            c.RemovedText.Add(new RemovedBlock(at, old, [.. lines]));
            c.RemovedAt.AddRange(lines.Select(_ => at));
        }
        return c;
    }

    static string Shape(Board b) => string.Join(" ", b.Items.Select(i =>
        i.Kind == "file" ? $"[{i.Line}-{i.EndLine}]" : i.Kind == "removed" ? $"-{i.Text!.Split('\n').Length}" : i.Kind));

    [Fact]
    public void AFileWithNoRemovalsIsStillOneWholeWindow()
    {
        var (scene, repo) = Repo();
        using (repo) using (scene)
        {
            var added = new FileChange(Path, 1, 0);
            added.AddedLines.Add(5);
            Assert.Equal("[0-29]", Shape(ChangeBoard.Build(Set(added), scene, "c")));
        }
    }

    /// <summary>every line of the file once, in order, with each block
    /// exactly where its lines came out.</summary>
    [Fact]
    public void TheFileIsCutWhereLinesWereRemoved()
    {
        var (scene, repo) = Repo();
        using (repo) using (scene)
        {
            var c = Removing(Path, (10, 10, ["a", "b"]), (20, 22, ["c"]));
            Assert.Equal("[0-9] -2 [10-19] -1 [20-29]", Shape(ChangeBoard.Build(Set(c), scene, "c")));
        }
    }

    [Fact]
    public void ARemovalAtEitherEndHasNoEmptyWindowBesideIt()
    {
        var (scene, repo) = Repo();
        using (repo) using (scene)
        {
            var atStart = Removing(Path, (0, 0, ["head"]));
            Assert.Equal("-1 [0-29]", Shape(ChangeBoard.Build(Set(atStart), scene, "c")));

            var atEnd = Removing(Path, (Lines, Lines, ["tail"]));
            Assert.Equal("[0-29] -1", Shape(ChangeBoard.Build(Set(atEnd), scene, "c")));
        }
    }

    /// <summary>the pieces touch, one under the next in one column, so the
    /// stack reads as one file.</summary>
    [Fact]
    public void ThePiecesOfAFileTouch()
    {
        var (scene, repo) = Repo();
        using (repo) using (scene)
        {
            var board = ChangeBoard.Build(Set(Removing(Path, (10, 10, ["a", "b"]))), scene, "c");
            for (int n = 1; n < board.Items.Count; n++)
            {
                var above = board.Items[n - 1];
                Assert.Equal(above.X, board.Items[n].X);
                Assert.Equal(above.Y + scene.ItemHeight(above), board.Items[n].Y, 2);
            }
        }
    }

    /// <summary>a removed line is as tall as a line in the windows beside it,
    /// or the old text would be a different size from the new.</summary>
    [Fact]
    public void ARemovedLineIsAsTallAsAWindowLine()
    {
        var (scene, repo) = Repo();
        using (repo) using (scene)
        {
            var board = ChangeBoard.Build(Set(Removing(Path, (10, 10, ["a", "b", "c"]))), scene, "c");
            var window = board.Items[0];
            var block = board.Items[1];
            var f = scene.Data.Files[scene.IndexOfPath(Path)];

            Assert.Equal(3 * scene.LineStepIn(window, f), block.H, 2);
            Assert.Equal((10, "a\nb\nc"), (block.Line, block.Text));
        }
    }

    [Fact]
    public void HiddenEachFileIsOneWindowAgain()
    {
        var (scene, repo) = Repo();
        using (repo) using (scene)
        {
            var c = Removing(Path, (10, 10, ["a"]), (20, 21, ["b"]));
            Assert.Equal("[0-29]", Shape(ChangeBoard.Build(Set(c), scene, "c", removed: false)));
        }
    }

    /// <summary>a file the change deleted is not in the scan, so it used to
    /// have nowhere to appear. It is its old text, as one block.</summary>
    [Fact]
    public void ADeletedFileIsShownAsItsOldText()
    {
        var (scene, repo) = Repo();
        using (repo) using (scene)
        {
            var gone = Removing("src/Gone.cs", (0, 0, ["class Gone", "{", "}"]));

            var board = ChangeBoard.Build(Set(gone), scene, "c");
            var block = Assert.Single(board.Items);
            Assert.Equal(("removed", "src/Gone.cs"), (block.Kind, block.File));
            Assert.True(block.H > 3 * scene.Data.LineH, "a deleted file has a header as well as its lines");

            Assert.Empty(ChangeBoard.Build(Set(gone), scene, "c", removed: false).Items);
        }
    }

    [Fact]
    public void TheViewOpensOnTheWindowHoldingTheFirstChange()
    {
        var (scene, repo) = Repo();
        using (repo) using (scene)
        {
            var c = Removing(Path, (20, 20, ["x"]));
            var set = Set(c);
            var board = ChangeBoard.Build(set, scene, "c");
            var spot = ChangeBoard.FirstChange(board, set, scene)!.Value;

            // the change is at line 20, with context from 17: in the second window
            var second = board.Items[2];
            Assert.Equal(20, second.Line);
            Assert.InRange(spot.Y, board.Items[0].Y, second.Y + scene.ItemHeight(second));
        }
    }

    /// <summary>only the first piece of a file has a header. The pieces after
    /// a cut carry straight on under the red block, like a diff, instead of
    /// each costing a header's height - about three lines close in.</summary>
    [Fact]
    public void OnlyTheFirstPieceHasAHeader()
    {
        var (scene, repo) = Repo();
        using (repo) using (scene)
        {
            var board = ChangeBoard.Build(Set(Removing(Path, (10, 10, ["a"]), (20, 20, ["b"]))), scene, "c");
            var windows = board.Items.Where(i => i.Kind == "file").ToList();

            Assert.Equal([false, true, true], windows.Select(w => w.Continued));
            var f = scene.Data.Files[scene.IndexOfPath(Path)];
            var second = windows[1];
            Assert.Equal((second.EndLine - second.Line + 1) * scene.LineStepIn(second, f), scene.ItemHeight(second), 2);
            Assert.Equal(Scene.WinHeadH + 10 * scene.LineStepIn(windows[0], f), scene.ItemHeight(windows[0]), 2);
        }
    }

    /// <summary>and one copied off the change view onto a board of your own
    /// is a window in its own right again, header and all.</summary>
    [Fact]
    public void ACopiedPieceGetsItsHeaderBack()
    {
        var piece = new BoardItem { Id = "w", Kind = "file", File = Path, Continued = true };
        var copy = System.Text.Json.JsonSerializer.Deserialize<BoardItem>(System.Text.Json.JsonSerializer.Serialize(piece))!;
        Assert.False(copy.Continued);
    }

    /// <summary>with the removed lines on the board as text, the thin red
    /// marker where they were would say it twice. With R hiding them, it is
    /// the only sign left, so it comes back.</summary>
    [Fact]
    public void TheThinMarkerIsLeftOutWhereTheTextIsShown()
    {
        var (scene, repo) = Repo();
        using (repo) using (scene)
        {
            var c = Removing(Path, (10, 10, ["a", "b"]));
            var set = Set(c);
            scene.Review = set;
            var board = ChangeBoard.Build(set, scene, "c");
            scene.ActiveBoard = board;
            scene.BoardReadOnly = true;

            // the block moved far off, so only the windows are on screen -
            // but it is still on the board, so its file counts as shown
            var block = board.Items.Single(i => i.Kind == "removed");
            block.X += 100_000;
            var second = board.Items.First(i => i.Kind == "file" && i.Continued);
            scene.CamX = second.X + 310;
            scene.CamY = second.Y;
            scene.CamS = 3f;

            int shown = Reddish(Render(scene, 600, 300));
            board.Items.Remove(block);
            int hidden = Reddish(Render(scene, 600, 300));

            Assert.True(hidden > 0, "no marker at all with the text hidden");
            Assert.True(shown < hidden / 4, $"{shown} red pixels with the text shown, {hidden} with it hidden");
            scene.ActiveBoard = null;
        }
    }

    /// <summary>a window draws the lines that can be seen, not its whole
    /// range. It drew every line every frame, which for the change view's
    /// whole-file windows was 25ms a frame - past the budget for 60 on its
    /// own - and is about a millisecond now.</summary>
    [Fact]
    public void AWindowDrawsOnlyTheLinesOnScreen()
    {
        using var repo = SampleRepo.Build();
        const string big = "src/Big.cs";
        repo.File(big, string.Join("\n", Enumerable.Range(0, 3000).Select(n => $"var value{n} = {n};")));
        using var scene = new Scene(Scanner.Build(repo.Path));
        var window = new BoardItem { Id = "w", Kind = "file", File = big, Line = 0, EndLine = -1, W = 620 };
        scene.ActiveBoard = new Board { Id = "b", Items = { window } };
        scene.CamX = 310;
        scene.CamY = scene.ItemHeight(window) / 2;
        scene.CamS = 2f;

        using var bmp = new SKBitmap(800, 600);
        using var canvas = new SKCanvas(bmp);
        for (int n = 0; n < 300 && scene.LinesOf(big) is null; n++) { scene.Draw(canvas, 800, 600); Thread.Sleep(10); }
        Assert.NotNull(scene.LinesOf(big));

        int before = scene.LinesDrawn;
        scene.Draw(canvas, 800, 600);
        int drawn = scene.LinesDrawn - before;

        // 600 pixels at this zoom is under forty lines
        Assert.InRange(drawn, 1, 60);
        scene.ActiveBoard = null;
    }

    static SKColor[] Render(Scene scene, int w, int h)
    {
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp)) { scene.Draw(canvas, w, h); canvas.Flush(); }
        var px = new SKColor[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = bmp.GetPixel(x, y);
        return px;
    }

    static int Reddish(SKColor[] px) => px.Count(c => c.Red > 150 && c.Red > c.Green + 40 && c.Red > c.Blue + 40);

    /// <summary>close in the old lines are text on a dark red panel; far out
    /// they are a solid red band, the way a removal looks on the map.</summary>
    [Fact]
    public void TheBlockIsTextCloseInAndABandFarOut()
    {
        var (scene, repo) = Repo();
        using (repo) using (scene)
        {
            var block = new BoardItem
            {
                Id = "r", Kind = "removed", File = Path, Line = 4, W = 620,
                Text = "removed = TheFirstLine();\nremoved = TheSecondLine();", H = 2 * 3 * (620f / 240),
            };
            scene.ActiveBoard = new Board { Id = "b", Items = { block } };
            scene.BoardReadOnly = true;
            scene.CamY = block.H / 2;

            // close in, on the left edge where the text starts
            scene.CamX = 60;
            scene.CamS = 3f;
            var close = Render(scene, 600, 200);
            scene.CamX = 310;
            scene.CamS = 0.2f;
            var far = Render(scene, 600, 200);

            int closeRed = Reddish(close), farRed = Reddish(far);
            Assert.True(closeRed > 0, "no red text close in");
            // far out the band fills its whole box: every pixel in it red
            float bandPx = 620 * 0.2f * block.H * 0.2f;
            Assert.True(farRed > bandPx * 0.8f, $"far out {farRed} red pixels of a {bandPx:F0} pixel band");
            scene.ActiveBoard = null;
        }
    }

    /// <summary>its height is stored, so nothing off the draw loop measures
    /// text to find it - the rule every board item keeps.</summary>
    [Fact]
    public void ItsHeightMeasuresNothing()
    {
        var (scene, repo) = Repo();
        using (repo) using (scene)
        {
            var block = new BoardItem { Id = "r", Kind = "removed", Text = "a\nb", H = 42, W = 620 };
            int before = scene.TextMeasures;

            Assert.Equal(42, scene.LastHeight(block));
            Assert.Equal(42, scene.ItemHeight(block));
            Assert.Equal(before, scene.TextMeasures);
        }
    }
}

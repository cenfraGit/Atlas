using SkiaSharp;

namespace Atlas.Tests;

/// <summary>the change view: whole windows, and a deleted file as a red
/// block of its old text.
///
/// Removed lines in a file that still exists are in the review's text now
/// (<see cref="Splice"/>), so a window shows them without being cut up; only
/// a file the change deleted, which has no window, needs a block.</summary>
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
    public void AFileWithRemovalsIsOneWholeWindow()
    {
        var (scene, repo) = Repo();
        using (repo) using (scene)
        {
            var c = Removing(Path, (10, 10, ["a", "b"]), (20, 22, ["c"]));
            Assert.Equal("[0-29]", Shape(ChangeBoard.Build(Set(c), scene, "c")));
        }
    }

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

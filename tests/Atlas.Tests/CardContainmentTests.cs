using SkiaSharp;

namespace Atlas.Tests;

/// <summary>a card owns its rectangle. The bars tier always clamped itself to
/// the card height, but the text tier did not, so a file taller than its card
/// wrote readable code out of the box and over whatever was below it in the
/// column.
///
/// This renders for real and reads the pixels back, because the bug is not in
/// any one number - it is in what ends up on the canvas. Whatever mechanism
/// might let content escape in future, this catches it.
///
/// The fixture is a repo with exactly one file and folders turned off, so
/// the only thing that can put a pixel on the canvas is that one card. A
/// neighbour or a folder wash would make "is anything drawn out here" an
/// unanswerable question.</summary>
[Collection("render")]
public class CardContainmentTests
{
    const int W = 900, H = 700;

    /// <summary>the map background, and the only colour that may appear
    /// outside a card.</summary>
    static readonly SKColor Empty = new(0x04, 0x07, 0x0f);

    static TempDir OneFile(int lines)
    {
        var dir = new TempDir("atlas_tall");
        dir.File("Huge.cs", SampleRepo.Scene(lines));
        return dir;
    }

    static Scene Look(TempDir repo, float camS, Func<FileRec, float> camY)
    {
        var scene = new Scene(Scanner.Build(repo.Path)) { ShowFolders = false };
        var f = scene.Data.Files[0];
        scene.CamS = camS;
        scene.CamX = f.X + f.W / 2;
        scene.CamY = camY(f);
        return scene;
    }

    /// <summary>draw, having made sure there is really something to draw.
    ///
    /// Source text is read and tokenised on a background thread, so a frame
    /// taken straight away falls back to the bars picture - which clamps
    /// itself and would make every assertion below pass for the wrong reason.
    /// Wait for the text, then draw.</summary>
    static SKBitmap Render(Scene scene)
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);

        scene.Draw(canvas, W, H);          // kicks off the load and queues the card

        if (Scene.TierFor(scene.CamS) == 3)
        {
            var path = scene.Data.Files[0].P;
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (scene.LinesOf(path) is null && DateTime.UtcNow < deadline)
                Thread.Sleep(10);
            Assert.True(scene.LinesOf(path) is { Length: > 0 },
                "the source never loaded, so the text tier was never exercised");
        }

        scene.Draw(canvas, W, H);
        canvas.Flush();
        return bmp;
    }

    static (float X, float Y) ToScreen(Scene scene, float wx, float wy) =>
        (W / 2f + (wx - scene.CamX) * scene.CamS,
         H / 2f + (wy - scene.CamY) * scene.CamS);

    static bool Drawn(SKBitmap bmp, int x, int y) => bmp.GetPixel(x, y) != Empty;

    /// <summary>fail naming the first pixel found outside the card.</summary>
    static void AssertNothingDrawnIn(SKBitmap bmp, int x0, int y0, int x1, int y1, string where)
    {
        for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++)
                if (Drawn(bmp, x, y))
                    Assert.Fail($"content escaped the card {where}, at {x},{y}");
    }

    static int Lit(SKBitmap bmp)
    {
        int n = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (Drawn(bmp, x, y)) n++;
        return n;
    }

    [Fact]
    public void ALongFilesTextStaysInsideItsCard()
    {
        using var repo = OneFile(2200);
        using var scene = Look(repo, 4f, f => f.Y + 60);   // reading zoom, near the top
        var f = scene.Data.Files[0];
        using var bmp = Render(scene);

        var (left, _) = ToScreen(scene, f.X, 0);
        var (right, _) = ToScreen(scene, f.X + f.W, 0);

        AssertNothingDrawnIn(bmp, 0, 0, (int)left - 1, H, "to the left");
        AssertNothingDrawnIn(bmp, (int)right + 2, 0, W, H, "to the right");
    }

    [Fact]
    public void ALongFilesTextDoesNotRunOffTheBottomOfItsCard()
    {
        using var repo = OneFile(2200);
        // park on the very end, where the old cap cut the card off and let the
        // text keep going down the column
        using var scene = Look(repo, 4f, f => f.Y + f.H - 40);
        var f = scene.Data.Files[0];
        using var bmp = Render(scene);

        var (_, bottom) = ToScreen(scene, 0, f.Y + f.H);
        AssertNothingDrawnIn(bmp, 0, (int)bottom + 2, W, H, "below");
    }

    [Fact]
    public void NothingIsDrawnAboveACardEither()
    {
        using var repo = OneFile(2200);
        using var scene = Look(repo, 4f, f => f.Y + 40);
        var f = scene.Data.Files[0];
        using var bmp = Render(scene);

        var (_, top) = ToScreen(scene, 0, f.Y);
        AssertNothingDrawnIn(bmp, 0, 0, W, (int)top - 1, "above");
    }

    [Fact]
    public void AtBarsZoomNothingEscapesEither()
    {
        using var repo = OneFile(2200);
        using var scene = Look(repo, 1f, f => f.Y + 200);
        var f = scene.Data.Files[0];
        using var bmp = Render(scene);

        var (left, _) = ToScreen(scene, f.X, 0);
        var (right, _) = ToScreen(scene, f.X + f.W, 0);

        AssertNothingDrawnIn(bmp, 0, 0, (int)left - 1, H, "to the left at bars zoom");
        AssertNothingDrawnIn(bmp, (int)right + 2, 0, W, H, "to the right at bars zoom");
    }

    [Fact]
    public void AVeryLongLineIsCutOffAtTheCardEdge()
    {
        using var dir = new TempDir("atlas_wide");
        // lines far longer than the card is wide, whatever the font's advance
        // turns out to be. Ordinary tokens rather than one enormous literal:
        // a pathological line is a TextMate problem, not a clipping one
        var wide = string.Join(" + ", Enumerable.Range(0, 80).Select(i => $"value{i}"));
        dir.File("Wide.cs", "namespace Demo;\n\nclass A\n{\n" +
            string.Join("\n", Enumerable.Repeat($"    var x = {wide};", 12)) + "\n}\n");
        using var scene = Look(dir, 6f, f => f.Y + 20);
        var f = scene.Data.Files[0];
        using var bmp = Render(scene);

        var (right, _) = ToScreen(scene, f.X + f.W, 0);
        AssertNothingDrawnIn(bmp, (int)right + 2, 0, W, H, "along a very long line");
    }

    [Fact]
    public void TheTextTierIsReallyBeingExercised()
    {
        using var repo = OneFile(2200);
        using var scene = Look(repo, 4f, f => f.Y + 60);
        using var bmp = Render(scene);

        // if nothing were drawn, or the bars picture were drawn in place of
        // text, every containment test above would pass for the wrong reason
        Assert.Equal(3, scene.Tier);
        Assert.NotNull(scene.LinesOf(scene.Data.Files[0].P));
        Assert.True(Lit(bmp) > 2000, $"the card drew only {Lit(bmp)} pixels");
    }

    [Fact]
    public void TextIsDrawnPastWhereTheOldCapCutTheCardOff()
    {
        using var repo = OneFile(2200);
        // line 900 is well past the 459 lines the old 1400 unit cap allowed
        using var scene = Look(repo, 4f, f => f.Y + 22 + 900 * 3);
        var f = scene.Data.Files[0];
        using var bmp = Render(scene);

        int x0 = Math.Clamp((int)ToScreen(scene, f.X, 0).X, 0, W);
        int x1 = Math.Clamp((int)ToScreen(scene, f.X + f.W, 0).X, 0, W);

        // there is code on screen here, inside the card, where the old build
        // drew it outside one
        int inside = 0;
        for (int y = 0; y < H; y++)
            for (int x = x0; x < x1; x++)
                if (Drawn(bmp, x, y)) inside++;

        Assert.True(inside > 2000, $"only {inside} pixels of code this far down the file");
    }

    // --- the layout rule underneath ---------------------------------------

    [Fact]
    public void ACardIsAsTallAsItsFile()
    {
        using var repo = OneFile(2200);
        var scan = Scanner.Build(repo.Path);
        var f = scan.Files[0];

        // the cap used to stop at 1400, which is 459 lines of a 2200 line file
        Assert.Equal(scan.HeaderH + f.N * scan.LineH, f.H, 1);
    }

    [Fact]
    public void EveryCardIsTallEnoughForItsOwnLines()
    {
        using var repo = SampleRepo.Build();
        var scan = Scanner.Build(repo.Path);

        foreach (var f in scan.Files)
            Assert.True(f.H >= scan.HeaderH + f.N * scan.LineH - 0.01f,
                $"{f.P} has {f.N} lines but a card only {f.H} tall");
    }
}

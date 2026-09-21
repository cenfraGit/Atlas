using SkiaSharp;

namespace Atlas.Tests;

/// <summary>the line numbers down the left of readable source.
///
/// They are the file's own numbers, not the window's: a board window onto
/// lines 40-88 says 40 to 88, because that is the numbering that means
/// anything to somebody opening the file somewhere else.</summary>
[Collection("render")]
public class LineNumberTests
{
    const int W = 700, H = 500;

    static (Scene Scene, TempDir Repo, FileRec File) Zoomed(float zoom, int line = 20)
    {
        var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var f = scene.Data.Files.First(x => x.P == SampleRepo.LongFile);

        // pin the card's left edge just inside the viewport. Centring it
        // puts the numbers off screen the moment the card is wider than the
        // window, which at this zoom it is by a factor of three - the first
        // version of these tests measured empty canvas and read zero
        scene.CamS = zoom;
        scene.CamX = f.X + Math.Min(f.W / 2, (W / 2f - 16) / zoom);
        scene.CamY = f.Y + scene.Data.HeaderH + line * scene.Data.LineH;
        return (scene, repo, f);
    }

    /// <summary>draw, having waited for the source.
    ///
    /// Text is read and tokenised on a background thread, so a frame taken
    /// straight away falls back to the bars picture - which draws no
    /// numbers, and made every one of these read zero while the app was
    /// plainly showing them. The same trap CardContainmentTests documents.</summary>
    static SKColor[] Frame(Scene scene, string? waitFor = null)
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            scene.Draw(canvas, W, H);          // kicks off the load

            if (waitFor is not null && Scene.TierFor(scene.CamS) == 3)
            {
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (scene.LinesOf(waitFor) is null && DateTime.UtcNow < deadline)
                    Thread.Sleep(10);
                Assert.True(scene.LinesOf(waitFor) is { Length: > 0 },
                    "the source never loaded, so the text tier was never exercised");
            }

            scene.Draw(canvas, W, H);
            canvas.Flush();
        }

        var px = new SKColor[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                px[y * W + x] = bmp.GetPixel(x, y);
        bmp.Dispose();
        return px;
    }

    /// <summary>ink in a vertical strip: pixels that differ from whatever
    /// colour that strip is mostly made of.
    ///
    /// Matching the gutter's colour directly does not work - the numbers are
    /// antialiased against the card, so almost no pixel reaches the pure
    /// colour, and a first attempt at this counted zero while the numbers
    /// were plainly on screen.</summary>
    static int InkIn(Scene scene, int x0, int x1, string? waitFor = null)
    {
        var px = Frame(scene, waitFor);
        x0 = Math.Clamp(x0, 0, W);
        x1 = Math.Clamp(x1, 0, W);
        if (x1 <= x0) return 0;

        var tally = new Dictionary<uint, int>();
        for (int y = 0; y < H; y++)
            for (int x = x0; x < x1; x++)
            {
                uint k = (uint)px[y * W + x];
                tally[k] = tally.GetValueOrDefault(k) + 1;
            }
        var background = new SKColor(tally.MaxBy(t => t.Value).Key);

        int n = 0;
        for (int y = 0; y < H; y++)
            for (int x = x0; x < x1; x++)
            {
                var c = px[y * W + x];
                if (Math.Abs(c.Red - background.Red) + Math.Abs(c.Green - background.Green) +
                    Math.Abs(c.Blue - background.Blue) > 40) n++;
            }
        return n;
    }

    /// <summary>the strip the numbers live in, on screen.</summary>
    static (int From, int To) Column(Scene scene, FileRec f)
    {
        float cardLeft = (f.X - scene.CamX) * scene.CamS + W / 2f;
        return ((int)cardLeft, (int)(cardLeft + (6 + scene.GutterFor(f.N)) * scene.CamS));
    }

    // --- how wide the column is --------------------------------------------

    [Fact]
    public void ShortFilesStillGetTwoDigitsOfRoom() =>
        // whatever a one line file needs, it is the same as a ninety line
        // one: a column that changes width between neighbouring cards reads
        // as a mistake
        Assert.Equal(Scene.GutterChars(1), Scene.GutterChars(99));

    [Fact]
    public void ALongerFileGetsAWiderColumn()
    {
        Assert.True(Scene.GutterChars(1000) > Scene.GutterChars(100));
        Assert.True(Scene.GutterChars(100) > Scene.GutterChars(99));
    }

    /// <summary>the width comes from the whole file, not from the lines on
    /// screen, or the code would shift sideways as you scrolled past 99.</summary>
    [Fact]
    public void TheColumnDoesNotMoveAsYouScroll()
    {
        var (scene, repo, f) = Zoomed(8f, line: 20);
        using (repo)
        using (scene)
        {
            Frame(scene, f.P);                  // the draw loop measures a character
            float atTop = scene.GutterFor(f.N);
            Assert.True(atTop > 0, "the column has no width, so this proves nothing");

            scene.CamY = f.Y + scene.Data.HeaderH + 250 * scene.Data.LineH;
            Frame(scene, f.P);
            Assert.Equal(atTop, scene.GutterFor(f.N));
        }
    }

    // --- what is drawn -----------------------------------------------------

    [Fact]
    public void NumbersAreDrawnWhereTheCodeIsReadable()
    {
        var (scene, repo, f) = Zoomed(8f);
        using (repo)
        using (scene)
        {
            Frame(scene, f.P);
            var (x0, x1) = Column(scene, f);
            Assert.True(InkIn(scene, x0, x1, f.P) > 200, "no line numbers on readable code");
        }
    }

    /// <summary>and not where it is bars. A number a pixel tall is a smudge
    /// on every line of every card.</summary>
    [Fact]
    public void NoNumbersAtTheBarsTier()
    {
        var (scene, repo, f) = Zoomed(0.4f);
        using (repo)
        using (scene)
        {
            Frame(scene);
            Assert.True(scene.Tier < 3, "meant to be below the text tier");

            // bars start at the card's left edge, so the strip is not empty -
            // what must be absent is the *text*, and at this zoom a digit is
            // a fraction of a pixel. Compare against the readable case
            var (x0, x1) = Column(scene, f);
            int bars = InkIn(scene, x0, Math.Max(x0 + 1, x1));

            var (close, repo2, f2) = Zoomed(8f);
            using (repo2)
            using (close)
            {
                Frame(close, f2.P);
                var (cx0, cx1) = Column(close, f2);
                Assert.True(bars < InkIn(close, cx0, cx1, f2.P) / 2,
                    $"the bars tier drew {bars} in the column, about what readable code does");
            }
        }
    }

    /// <summary>the code moved right to make room, rather than the numbers
    /// being laid over the first few characters of every line.</summary>
    [Fact]
    public void TheCodeStartsAfterTheColumn()
    {
        var (scene, repo, f) = Zoomed(8f);
        using (repo)
        using (scene)
        {
            Frame(scene, f.P);                  // measure a character first
            var px = Frame(scene, f.P);
            float gutter = scene.GutterFor(f.N);

            // card-local x to screen x
            float cardLeft = (f.X - scene.CamX) * scene.CamS + W / 2f;
            int numbersEnd = (int)(cardLeft + (6 + gutter) * scene.CamS);

            var code = new SKColor(0x9f, 0xd4, 0xea);
            int codeLeftOfColumn = 0;
            for (int y = 0; y < H; y++)
                for (int x = Math.Max(0, (int)cardLeft); x < Math.Min(W, numbersEnd - 2); x++)
                {
                    var c = px[y * W + x];
                    if (Math.Abs(c.Red - code.Red) < 20 &&
                        Math.Abs(c.Green - code.Green) < 20 &&
                        Math.Abs(c.Blue - code.Blue) < 20) codeLeftOfColumn++;
                }

            Assert.True(codeLeftOfColumn < 40,
                $"{codeLeftOfColumn} pixels of source are drawn inside the number column");
        }
    }

    /// <summary>a blank line still has a number. A gap in the column reads
    /// as a fault, and the line is still a line.
    ///
    /// The file is almost entirely blank on purpose: with no source beside
    /// them, the numbers are the only thing in the column, so this cannot
    /// pass on ink that belongs to something else. An earlier version used
    /// a file with real code in it and went on passing with the numbers
    /// taken out.</summary>
    [Fact]
    public void BlankLinesAreNumberedToo()
    {
        const char lf = (char)10;
        using var repo = SampleRepo.Build();
        repo.File("app/Gappy.cs", "class A" + lf + new string(lf, 30) + "}" + lf);

        using var scene = new Scene(Scanner.Build(repo.Path));
        var f = scene.Data.Files.First(x => x.P == "app/Gappy.cs");
        scene.CamS = 8f;
        scene.CamX = f.X + Math.Min(f.W / 2, (W / 2f - 16) / 8f);
        scene.CamY = f.Y + f.H / 2;

        Frame(scene, f.P);
        var (x0, x1) = Column(scene, f);
        int ink = InkIn(scene, x0, x1, f.P);
        Assert.True(ink > 120, $"a file that is mostly blank drew only {ink} pixels of numbers");
    }
}

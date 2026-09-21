using System.Diagnostics;
using SkiaSharp;

namespace Atlas.Tests;

/// <summary>what a board costs to draw as it fills up.
///
/// The map solved this long ago: bar geometry is recorded once into an
/// SKPicture and replayed, so panning is a pure canvas transform. A board
/// rebuilds every item every frame, which is fine at tens of items and the
/// open question at hundreds - and a drawing surface is somewhere people
/// make hundreds.
///
/// This measures rather than assumes. A timing test cannot be strict without
/// being flaky, so the budget here is loose: it is here to notice a board
/// becoming unusable, not to police milliseconds.</summary>
[Collection("render")]
public class BoardDrawCostTests
{
    const int W = 900, H = 700;
    const int Frames = 12;

    static Board WithStrokes(int count, int pointsEach = 40)
    {
        var board = new Board { Id = "b", Name = "busy" };
        var rng = new Random(7);

        for (int s = 0; s < count; s++)
        {
            var it = new BoardItem { Id = "s" + s, Kind = "stroke", Weight = 3 };
            float x = rng.Next(0, 4000), y = rng.Next(0, 3000);
            for (int i = 0; i < pointsEach; i++)
                Strokes.Add(it, x + i * 6, y + MathF.Sin(i * 0.4f) * 30, minStep: 0);
            board.Items.Add(it);
        }
        return board;
    }

    /// <summary>the cheapest of several frames, in milliseconds.
    ///
    /// The fastest run, not the median: contention only ever adds time, so
    /// the minimum is the closest thing to what the code costs on its own.
    /// A median failed once under load and passed alone, which is exactly
    /// what a timing test is worth when it measures the machine as well as
    /// the program.</summary>
    static double FrameCost(Board board, TempDir repo)
    {
        using var scene = new Scene(Scanner.Build(repo.Path))
        {
            ActiveBoard = board, CamX = 2000, CamY = 1500, CamS = 0.25f,
        };

        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);

        scene.Draw(canvas, W, H);          // warm whatever caches exist

        var times = new List<double>();
        for (int i = 0; i < Frames; i++)
        {
            var sw = Stopwatch.StartNew();
            scene.Draw(canvas, W, H);
            canvas.Flush();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        bmp.Dispose();
        scene.ActiveBoard = null;
        return times.Min();
    }

    [Fact]
    public void AHundredStrokesDrawWellInsideAFrame()
    {
        using var repo = SampleRepo.Build();

        double ms = FrameCost(WithStrokes(100), repo);

        // 16.6ms is the 60fps budget; a board this size should not be near it
        Assert.True(ms < 16.6, $"100 strokes cost {ms:0.00}ms a frame");
    }

    [Fact]
    public void AThousandStrokesStillDrawInAFrame()
    {
        using var repo = SampleRepo.Build();

        double ms = FrameCost(WithStrokes(1000), repo);

        // the number that decides whether an SKPicture is needed. Generous,
        // because a test machine is not a promise about anyone's laptop -
        // but a board that blows well past this is not usable
        Assert.True(ms < 33, $"1000 strokes cost {ms:0.00}ms a frame");
    }

    /// <summary>a first frame against a median of later ones was the obvious
    /// way to show the cache working, and it passed alone and failed under
    /// load: at a thousand strokes the recording frame is not much dearer
    /// than a replay of the same thousand paths, so the margin is inside the
    /// noise. Counting re-recordings says the same thing and cannot be told
    /// a lie by the machine being busy.</summary>
    [Fact]
    public void TheInkIsRecordedOnceAndReplayedAfterThat()
    {
        using var repo = SampleRepo.Build();
        var board = WithStrokes(200);

        using var scene = new Scene(Scanner.Build(repo.Path))
        {
            ActiveBoard = board, CamX = 2000, CamY = 1500, CamS = 0.25f,
        };
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);

        for (int i = 0; i < 20; i++) scene.Draw(canvas, W, H);

        Assert.Equal(1, scene.StrokeRebuilds);

        bmp.Dispose();
        scene.ActiveBoard = null;
    }

    [Fact]
    public void TheStrokeUnderTheHandDoesNotReRecordEveryFrame()
    {
        using var repo = SampleRepo.Build();
        var board = WithStrokes(5);

        using var scene = new Scene(Scanner.Build(repo.Path))
        {
            ActiveBoard = board, CamX = 0, CamY = 0, CamS = 1f,
        };
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);

        scene.Draw(canvas, W, H);
        int settled = scene.StrokeRebuilds;

        // the draft changes every frame by definition, so it is drawn live
        var draft = new BoardItem { Id = "draft", Kind = "stroke", Weight = 3 };
        scene.StrokeDraft = draft;
        for (int i = 0; i < 10; i++)
        {
            Strokes.Add(draft, i * 20, i * 5, minStep: 0);
            scene.Draw(canvas, W, H);
        }
        Assert.Equal(settled, scene.StrokeRebuilds);

        // and joins the recording once it is let go
        scene.StrokeDraft = null;
        board.Items.Add(draft);
        scene.Draw(canvas, W, H);
        Assert.Equal(settled + 1, scene.StrokeRebuilds);

        bmp.Dispose();
        scene.ActiveBoard = null;
    }

    [Fact]
    public void EditingAStrokeIsSeenRatherThanCached()
    {
        using var repo = SampleRepo.Build();
        var board = WithStrokes(3);
        var ink = board.Items[0];

        using var scene = new Scene(Scanner.Build(repo.Path))
        {
            ActiveBoard = board, CamX = 0, CamY = 0, CamS = 1f,
        };
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        scene.Draw(canvas, W, H);

        // a cache keyed on something too coarse would miss each of these
        foreach (var edit in new Action[]
        {
            () => Strokes.Move(ink, 50, 50),
            () => ink.Color = "#3fb96a",
            () => ink.Weight = 12,
            () => Strokes.ScaleInto(ink, new SKRect(0, 0, 500, 500)),
            () => board.Items.RemoveAt(0),
        })
        {
            long before = Scene.StrokeSignature(board);
            edit();
            Assert.NotEqual(before, Scene.StrokeSignature(board));
        }

        bmp.Dispose();
        scene.ActiveBoard = null;
    }

    [Fact]
    public void TheCostGrowsWithTheWorkAndNotFasterThanIt()
    {
        using var repo = SampleRepo.Build();

        double small = FrameCost(WithStrokes(100), repo);
        double large = FrameCost(WithStrokes(800), repo);

        // eight times the strokes should not be far more than eight times the
        // cost. Worse than that means something per-frame is quadratic, which
        // is the failure worth catching early
        Assert.True(large < Math.Max(small, 0.05) * 24,
            $"100 strokes {small:0.00}ms, 800 strokes {large:0.00}ms");
    }
}

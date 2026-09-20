using SkiaSharp;

namespace Atlas.Tests;

/// <summary>the canvas used to pan into empty space forever, with F the only
/// way back. The camera is now held near whatever there is to look at.</summary>
public class CameraBoundsTests
{
    const float Vw = 1400, Vh = 900;

    static Scene MapScene(TempDir repo) => new(Scanner.Build(repo.Path));

    [Fact]
    public void PanningFarOffTheMapIsPulledBack()
    {
        using var repo = SampleRepo.Build();
        using var scene = MapScene(repo);
        scene.CamS = 1f;

        scene.CamX = 5_000_000;
        scene.CamY = -5_000_000;
        scene.ClampCamera(Vw, Vh);

        var b = scene.ContentBounds();
        Assert.InRange(scene.CamX, b.Left - Vw, b.Right + Vw);
        Assert.InRange(scene.CamY, b.Top - Vh, b.Bottom + Vh);
    }

    [Fact]
    public void ACameraAlreadyOverTheContentIsLeftAlone()
    {
        using var repo = SampleRepo.Build();
        using var scene = MapScene(repo);
        scene.CamS = 1f;
        scene.CamX = scene.Data.World.W / 2;
        scene.CamY = scene.Data.World.H / 2;

        float x = scene.CamX, y = scene.CamY;
        scene.ClampCamera(Vw, Vh);

        Assert.Equal(x, scene.CamX);
        Assert.Equal(y, scene.CamY);
    }

    [Fact]
    public void ThereIsStillSlackToWorkBesideTheContent()
    {
        using var repo = SampleRepo.Build();
        using var scene = MapScene(repo);
        scene.CamS = 1f;

        // a screen past the edge must survive the clamp: you should be able to
        // pull something off centre to look at it
        scene.CamX = scene.Data.World.W + Vw / 2;
        scene.ClampCamera(Vw, Vh);

        Assert.True(scene.CamX > scene.Data.World.W, $"the slack was clamped away, got {scene.CamX}");
    }

    [Fact]
    public void ClampingIsIdempotent()
    {
        using var repo = SampleRepo.Build();
        using var scene = MapScene(repo);
        scene.CamS = 1f;
        scene.CamX = 999_999;

        scene.ClampCamera(Vw, Vh);
        float once = scene.CamX;
        scene.ClampCamera(Vw, Vh);

        Assert.Equal(once, scene.CamX);
    }

    [Fact]
    public void FitLandsInsideTheBounds()
    {
        using var repo = SampleRepo.Build();
        using var scene = MapScene(repo);

        scene.Fit(Vw, Vh);
        float x = scene.CamX, y = scene.CamY;
        scene.ClampCamera(Vw, Vh);

        Assert.Equal(x, scene.CamX, 1);
        Assert.Equal(y, scene.CamY, 1);
    }

    [Fact]
    public void TheMapBoundsAreTheScannedWorld()
    {
        using var repo = SampleRepo.Build();
        using var scene = MapScene(repo);

        var b = scene.ContentBounds();

        Assert.Equal(0, b.Left);
        Assert.Equal(0, b.Top);
        Assert.Equal(scene.Data.World.W, b.Right);
        Assert.Equal(scene.Data.World.H, b.Bottom);
    }

    // --- boards ----------------------------------------------------------

    [Fact]
    public void ABoardIsBoundedByWhatIsOnIt()
    {
        using var repo = SampleRepo.Build();
        using var scene = MapScene(repo);
        var board = new Board { Id = "b", Name = "b" };
        board.Items.Add(new BoardItem { Id = "n", Kind = "note", X = 100, Y = 200, W = 300, H = 150 });
        scene.ActiveBoard = board;

        var b = scene.ContentBounds();

        Assert.Equal(100, b.Left);
        Assert.Equal(200, b.Top);
        Assert.Equal(400, b.Right);
        Assert.Equal(350, b.Bottom);

        scene.ActiveBoard = null;
    }

    [Fact]
    public void AnArrowsFarEndCountsTowardsTheBounds()
    {
        using var repo = SampleRepo.Build();
        using var scene = MapScene(repo);
        var board = new Board { Id = "b", Name = "b" };
        board.Items.Add(new BoardItem { Id = "a", Kind = "arrow", X = 0, Y = 0, W = 0, X2 = 900, Y2 = 700 });
        scene.ActiveBoard = board;

        var b = scene.ContentBounds();

        Assert.True(b.Right >= 900, $"the arrow's tip is outside the bounds, right is {b.Right}");
        Assert.True(b.Bottom >= 700, $"the arrow's tip is outside the bounds, bottom is {b.Bottom}");

        scene.ActiveBoard = null;
    }

    [Fact]
    public void AnEmptyBoardStillHasSomewhereToBe()
    {
        using var repo = SampleRepo.Build();
        using var scene = MapScene(repo);
        scene.ActiveBoard = new Board { Id = "b", Name = "nothing on it yet" };
        scene.CamS = 1f;

        var b = scene.ContentBounds();
        Assert.True(b.Width > 0 && b.Height > 0, "an empty board has no area to pan around");

        scene.CamX = 500_000;
        scene.ClampCamera(Vw, Vh);
        Assert.True(scene.CamX < 500_000, "an empty board pans away forever");

        scene.ActiveBoard = null;
    }

    [Fact]
    public void PanningOffABoardIsPulledBack()
    {
        using var repo = SampleRepo.Build();
        using var scene = MapScene(repo);
        var board = new Board { Id = "b", Name = "b" };
        board.Items.Add(new BoardItem { Id = "n", Kind = "note", X = 0, Y = 0, W = 300, H = 150 });
        scene.ActiveBoard = board;
        scene.CamS = 1f;

        scene.CamX = 80_000;
        scene.CamY = 80_000;
        scene.ClampCamera(Vw, Vh);

        Assert.True(scene.CamX < 10_000, $"still way off the board at {scene.CamX}");
        Assert.True(scene.CamY < 10_000, $"still way off the board at {scene.CamY}");

        scene.ActiveBoard = null;
    }

    // --- zoom ------------------------------------------------------------

    [Fact]
    public void YouCannotZoomOutUntilTheContentIsASpeck()
    {
        using var repo = SampleRepo.Build();
        using var scene = MapScene(repo);

        float min = scene.MinZoomFor(Vw, Vh);
        var b = scene.ContentBounds();

        // at the minimum the world still covers a decent part of the window
        Assert.True(b.Width * min > Vw * 0.2f, $"the world shrinks to {b.Width * min}px across");
    }

    [Fact]
    public void TheMinimumZoomStillLeavesRoomToPullBack()
    {
        using var repo = SampleRepo.Build();
        using var scene = MapScene(repo);

        scene.Fit(Vw, Vh);
        Assert.True(scene.MinZoomFor(Vw, Vh) < scene.CamS,
            "you should be able to zoom out past a plain fit");
    }

    [Fact]
    public void TheMinimumZoomStaysWithinTheOldHardLimits() =>
        Assert.InRange(new Scene(new Scan { World = new WorldSize { W = 50_000, H = 50_000 } })
            .MinZoomFor(Vw, Vh), 0.006f, 1f);
}

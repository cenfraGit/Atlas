namespace Atlas.Tests;

/// <summary>the grid used to be one fixed cell, which turns to mush zoomed out
/// and leaves nothing to align to zoomed in. It now steps through a ruler's
/// 1-2-5 ladder to stay near a readable size on screen.</summary>
public class GridTests
{
    const float Cell = 40f;
    const float MinPx = 14f;

    [Theory]
    [InlineData(0.01f)]
    [InlineData(0.05f)]
    [InlineData(0.25f)]
    [InlineData(1f)]
    [InlineData(4f)]
    [InlineData(20f)]
    [InlineData(40f)]
    public void ACellIsAlwaysBigEnoughToSee(float camS)
    {
        float step = Scene.GridStepFor(Cell, camS, MinPx);
        Assert.True(step * camS >= MinPx, $"a cell is {step * camS}px at zoom {camS}");
    }

    [Theory]
    [InlineData(0.01f)]
    [InlineData(0.25f)]
    [InlineData(1f)]
    [InlineData(4f)]
    [InlineData(40f)]
    public void ACellIsNeverSoLargeThatTheGridDisappears(float camS)
    {
        // the next rung down is always too small, or we would have taken it
        float step = Scene.GridStepFor(Cell, camS, MinPx);
        Assert.True(step * camS < MinPx * 10, $"a cell is {step * camS}px at zoom {camS}");
    }

    [Fact]
    public void TheCellCoarsensAsYouZoomOut()
    {
        float close = Scene.GridStepFor(Cell, 4f, MinPx);
        float mid = Scene.GridStepFor(Cell, 0.5f, MinPx);
        float far = Scene.GridStepFor(Cell, 0.02f, MinPx);

        Assert.True(mid > close, $"{mid} should be coarser than {close}");
        Assert.True(far > mid, $"{far} should be coarser than {mid}");
    }

    [Fact]
    public void TheCellSubdividesAsYouZoomIn()
    {
        // past the base cell there is still room for a finer one
        Assert.True(Scene.GridStepFor(Cell, 30f, MinPx) < Cell,
            "zoomed right in, the grid should be finer than the base cell");
    }

    [Fact]
    public void TheCellNeverGoesBackwardsAsYouZoomOut()
    {
        float last = 0;
        for (float s = 40f; s > 0.005f; s /= 1.1f)
        {
            float step = Scene.GridStepFor(Cell, s, MinPx);
            Assert.True(step >= last, $"the cell shrank from {last} to {step} on zooming out to {s}");
            last = step;
        }
    }

    [Fact]
    public void EveryCellIsARungOfTheLadder()
    {
        float[] allowed = [0.0625f, 0.125f, 0.25f, 0.5f, 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000];

        for (float s = 40f; s > 0.005f; s /= 1.07f)
        {
            float m = Scene.GridStepFor(Cell, s, MinPx) / Cell;
            Assert.Contains(allowed, a => Math.Abs(a - m) < 1e-4f);
        }
    }

    [Fact]
    public void AtTheBaseCellsOwnScaleTheBaseCellIsUsed() =>
        Assert.Equal(Cell, Scene.GridStepFor(Cell, MinPx / Cell, MinPx));

    [Fact]
    public void NoGridMeansNoStep()
    {
        Assert.Equal(0, Scene.GridStepFor(0, 1f, MinPx));
        Assert.Equal(0, Scene.GridStepFor(Cell, 0, MinPx));
    }

    [Fact]
    public void SnappingFollowsTheGridThatIsShowing()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path)) { Grid = Cell, CamS = 0.1f };

        // what you snap to is what you can see
        Assert.Equal(Scene.GridStepFor(Cell, 0.1f), scene.SnapStep(Cell));
    }

    [Fact]
    public void WithTheGridOffSnappingFallsBackToTheBaseCell()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path)) { Grid = 0, CamS = 1f };

        Assert.Equal(Cell, scene.SnapStep(Cell));
    }
}

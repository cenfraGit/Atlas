namespace Atlas.Tests;

/// <summary>the wheel moves a target and the camera chases it. The two things
/// worth pinning down are that it always arrives, and that it takes the same
/// wall-clock time however often it is stepped - a fixed fraction per frame
/// would settle twice as fast at 120fps as at 60.</summary>
public class GlideTests
{
    static Scene Parked(TempDir repo, float x = 0, float y = 0, float s = 1f)
    {
        var scene = new Scene(Scanner.Build(repo.Path));
        scene.CamX = x;
        scene.CamY = y;
        scene.CamS = s;
        return scene;
    }

    /// <summary>run to a standstill, or give up.</summary>
    static int RunOut(Glide glide, Scene scene, float dt = 1 / 60f, int limit = 2000)
    {
        int frames = 0;
        while (glide.Step(scene, dt) && frames < limit) frames++;
        return frames;
    }

    [Fact]
    public void AFreshGlideIsNotRunning() => Assert.False(new Glide().Running);

    [Fact]
    public void SteppingAStoppedGlideDoesNothing()
    {
        using var repo = SampleRepo.Build();
        using var scene = Parked(repo, 10, 20, 2f);
        var glide = new Glide();

        Assert.False(glide.Step(scene, 1 / 60f));
        Assert.Equal(10, scene.CamX);
        Assert.Equal(20, scene.CamY);
    }

    [Fact]
    public void TheCameraArrivesAtTheTarget()
    {
        using var repo = SampleRepo.Build();
        using var scene = Parked(repo);
        var glide = new Glide();
        glide.To(500, 300, 2f);

        RunOut(glide, scene);

        Assert.False(glide.Running);
        Assert.Equal(500, scene.CamX, 1);
        Assert.Equal(300, scene.CamY, 1);
        Assert.Equal(2f, scene.CamS, 2);
    }

    [Fact]
    public void ItArrivesRatherThanApproachingForever()
    {
        using var repo = SampleRepo.Build();
        using var scene = Parked(repo);
        var glide = new Glide();
        glide.To(500, 300, 1f);

        // an exponential approach never reaches its target; without a floor
        // the canvas would redraw for the rest of the session
        int frames = RunOut(glide, scene);

        Assert.True(frames < 120, $"took {frames} frames to settle");
        Assert.False(glide.Running);
    }

    [Fact]
    public void ItMovesMostOfTheWayQuickly()
    {
        using var repo = SampleRepo.Build();
        using var scene = Parked(repo);
        var glide = new Glide();
        glide.To(1000, 0, 1f);

        // a tenth of a second should be most of the journey, or it reads as
        // lag rather than as easing
        for (int i = 0; i < 6; i++) glide.Step(scene, 1 / 60f);

        Assert.True(scene.CamX > 700, $"only got to {scene.CamX} of 1000 in 100ms");
    }

    [Fact]
    public void ItNeverOvershoots()
    {
        using var repo = SampleRepo.Build();
        using var scene = Parked(repo);
        var glide = new Glide();
        glide.To(1000, -400, 4f);

        while (glide.Step(scene, 1 / 60f))
        {
            Assert.InRange(scene.CamX, 0, 1000);
            Assert.InRange(scene.CamY, -400, 0);
            Assert.InRange(scene.CamS, 1f, 4f);
        }
    }

    [Fact]
    public void ProgressNeverReverses()
    {
        using var repo = SampleRepo.Build();
        using var scene = Parked(repo);
        var glide = new Glide();
        glide.To(800, 0, 1f);

        float last = scene.CamX;
        while (glide.Step(scene, 1 / 60f))
        {
            Assert.True(scene.CamX >= last - 0.001f, $"went backwards: {scene.CamX} after {last}");
            last = scene.CamX;
        }
    }

    [Fact]
    public void TheSameJourneyTakesTheSameTimeAtAnyFrameRate()
    {
        using var repo = SampleRepo.Build();

        using var slow = Parked(repo);
        var a = new Glide();
        a.To(1000, 0, 1f);
        for (int i = 0; i < 6; i++) a.Step(slow, 1 / 60f);       // 100ms at 60fps

        using var fast = Parked(repo);
        var b = new Glide();
        b.To(1000, 0, 1f);
        for (int i = 0; i < 24; i++) b.Step(fast, 1 / 240f);     // 100ms at 240fps

        // a fixed fraction per frame would put these a long way apart
        Assert.Equal(slow.CamX, fast.CamX, 1);
    }

    [Fact]
    public void RetargetingMidGlideHeadsSomewhereElse()
    {
        using var repo = SampleRepo.Build();
        using var scene = Parked(repo);
        var glide = new Glide();

        glide.To(1000, 0, 1f);
        for (int i = 0; i < 3; i++) glide.Step(scene, 1 / 60f);

        glide.To(-500, 0, 1f);
        RunOut(glide, scene);

        Assert.Equal(-500, scene.CamX, 1);
    }

    [Fact]
    public void StopLeavesTheCameraWhereItGotTo()
    {
        using var repo = SampleRepo.Build();
        using var scene = Parked(repo);
        var glide = new Glide();
        glide.To(1000, 0, 1f);
        for (int i = 0; i < 3; i++) glide.Step(scene, 1 / 60f);
        float at = scene.CamX;

        glide.Stop();

        Assert.False(glide.Step(scene, 1 / 60f));
        Assert.Equal(at, scene.CamX);
    }

    [Fact]
    public void AZeroLengthFrameChangesNothingButKeepsGoing()
    {
        using var repo = SampleRepo.Build();
        using var scene = Parked(repo);
        var glide = new Glide();
        glide.To(1000, 0, 1f);

        Assert.True(glide.Step(scene, 0));
        Assert.Equal(0, scene.CamX);
    }

    // --- zoom -------------------------------------------------------------

    [Fact]
    public void ZoomingOutTakesAsLongAsZoomingIn()
    {
        // eased linearly, halving would take a quarter as long as doubling
        float inward = Glide.EaseZoom(1f, 2f, 0.05f);
        float outward = Glide.EaseZoom(1f, 0.5f, 0.05f);

        Assert.Equal(MathF.Log(inward), -MathF.Log(outward), 4);
    }

    [Fact]
    public void ZoomNeverPassesThroughZero()
    {
        using var repo = SampleRepo.Build();
        using var scene = Parked(repo, s: 12f);
        var glide = new Glide();
        glide.To(0, 0, 0.01f);

        while (glide.Step(scene, 1 / 60f))
            Assert.True(scene.CamS > 0, $"zoom collapsed to {scene.CamS}");

        Assert.Equal(0.01f, scene.CamS, 4);
    }

    [Fact]
    public void ADegenerateZoomIsHandedStraightOver()
    {
        // a zoom of zero has no logarithm, so there is nothing to ease along;
        // the target is the only sane answer
        Assert.Equal(2f, Glide.EaseZoom(0f, 2f, 0.05f));
        Assert.Equal(0f, Glide.EaseZoom(1f, 0f, 0.05f));
    }

    // --- settling ---------------------------------------------------------

    [Fact]
    public void SettlingIsJudgedOnScreenNotInTheWorld()
    {
        // half a world unit is invisible zoomed right out and a mile zoomed in
        Assert.True(Glide.Settled(0, 0.5f, 0, 0, 0.02f, 0.02f));
        Assert.False(Glide.Settled(0, 0.5f, 0, 0, 20f, 20f));
    }

    [Fact]
    public void ALargeGapIsNotSettled() =>
        Assert.False(Glide.Settled(0, 500, 0, 0, 1f, 1f));

    [Fact]
    public void AZoomThatHasNotArrivedIsNotSettled() =>
        Assert.False(Glide.Settled(0, 0, 0, 0, 1f, 4f));

    [Fact]
    public void EaseIsFrameRateIndependent()
    {
        float once = Glide.Ease(0, 100, 0.1f);

        float twice = 0;
        for (int i = 0; i < 10; i++) twice = Glide.Ease(twice, 100, 0.01f);

        Assert.Equal(once, twice, 2);
    }
}

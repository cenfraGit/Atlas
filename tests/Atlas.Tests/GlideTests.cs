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

    // --- the flick ---------------------------------------------------------

    [Fact]
    public void AFlickCarriesTheCameraOn()
    {
        using var repo = SampleRepo.Build();
        using var scene = Parked(repo);
        var glide = new Glide();

        glide.Flick(scene, vx: 2000, vy: 0);
        Assert.True(glide.Running);

        RunOut(glide, scene);
        Assert.True(scene.CamX > 300, $"barely moved: {scene.CamX}");
    }

    [Fact]
    public void AFasterFlickGoesFurther()
    {
        using var repo = SampleRepo.Build();

        using var slow = Parked(repo);
        var a = new Glide();
        a.Flick(slow, 1000, 0);
        RunOut(a, slow);

        using var fast = Parked(repo);
        var b = new Glide();
        b.Flick(fast, 4000, 0);
        RunOut(b, fast);

        Assert.True(fast.CamX > slow.CamX * 2, $"{fast.CamX} should dwarf {slow.CamX}");
    }

    [Fact]
    public void LettingGoWithoutMovingDoesNotThrow()
    {
        using var repo = SampleRepo.Build();
        using var scene = Parked(repo);
        var glide = new Glide();

        // without a floor, every drag would end in a small unasked-for slide
        glide.Flick(scene, vx: 10, vy: 10);

        Assert.False(glide.Running);
        Assert.Equal(0, scene.CamX);
    }

    [Fact]
    public void AWildGestureIsCappedRatherThanObeyed()
    {
        using var repo = SampleRepo.Build();

        using var hard = Parked(repo);
        var a = new Glide();
        a.Flick(hard, 100_000, 0);
        RunOut(a, hard);

        using var capped = Parked(repo);
        var b = new Glide();
        b.Flick(capped, Glide.FlickCeiling, 0);
        RunOut(b, capped);

        Assert.Equal(capped.CamX, hard.CamX, 1);
    }

    [Fact]
    public void AFlickKeepsItsDirection()
    {
        using var repo = SampleRepo.Build();
        using var scene = Parked(repo);
        var glide = new Glide();

        glide.Flick(scene, vx: -1500, vy: 2500);
        RunOut(glide, scene);

        Assert.True(scene.CamX < 0, "should have gone left");
        Assert.True(scene.CamY > 0, "should have gone down");
    }

    [Fact]
    public void AFlickDoesNotChangeTheZoom()
    {
        using var repo = SampleRepo.Build();
        using var scene = Parked(repo, s: 3.5f);
        var glide = new Glide();

        glide.Flick(scene, 3000, 0);
        RunOut(glide, scene);

        Assert.Equal(3.5f, scene.CamS, 3);
    }

    [Fact]
    public void AFlickCoastsLongerThanAWheelNotch()
    {
        using var repo = SampleRepo.Build();

        using var flicked = Parked(repo);
        var a = new Glide();
        a.Flick(flicked, 3000, 0);
        int flickFrames = RunOut(a, flicked);

        using var wheeled = Parked(repo);
        var b = new Glide();
        b.ToAtOnce(flicked.CamX, 0, 1f);      // the same distance, asked for
        int wheelFrames = RunOut(b, wheeled);

        // the wheel answers a request; a flick carries momentum
        Assert.True(flickFrames > wheelFrames,
            $"flick settled in {flickFrames} frames, wheel in {wheelFrames}");
    }

    [Fact]
    public void AWheelNotchAfterAFlickIsBackToTheQuickSpeed()
    {
        using var repo = SampleRepo.Build();
        using var scene = Parked(repo);
        var glide = new Glide();

        glide.Flick(scene, 3000, 0);
        glide.Step(scene, 1 / 60f);

        // otherwise every scroll after a throw feels sluggish
        glide.ToAtOnce(scene.CamX + 1000, scene.CamY, scene.CamS);
        int frames = RunOut(glide, scene);

        using var fresh = Parked(repo);
        var plain = new Glide();
        plain.To(1000, 0, 1f);
        Assert.InRange(frames, 1, RunOut(plain, fresh) + 4);
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

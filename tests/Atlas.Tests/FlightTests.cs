namespace Atlas.Tests;

/// <summary>van Wijk and Nuij zoom-and-pan. The curve is the feature: a linear
/// lerp drags the viewport across the world at full magnification and feels
/// awful, so "arcs out, travels, arcs back" is what gets asserted.</summary>
public class FlightTests
{
    const float Vw = 1400;

    static Flight Far() => Flight.To(Vw, 0, 0, 4f, 20000, 15000, 4f)!;

    [Fact]
    public void AFlightStartsWhereTheCameraIs()
    {
        var f = Far();
        f.Sample(0, out var x, out var y, out var s);

        Assert.Equal(0, x, 1);
        Assert.Equal(0, y, 1);
        Assert.Equal(4f, s, 2);
    }

    [Fact]
    public void AFlightEndsOnItsTarget()
    {
        var f = Far();
        f.Sample(f.DurationMs, out var x, out var y, out var s);

        Assert.Equal(20000, x, 1);
        Assert.Equal(15000, y, 1);
        Assert.Equal(4f, s, 2);
    }

    [Fact]
    public void ALongTraverseZoomsOutOnTheWay()
    {
        var f = Far();
        f.Sample(0, out _, out _, out var start);
        f.Sample(f.DurationMs / 2, out _, out _, out var middle);

        Assert.True(middle < start * 0.5f, $"mid zoom {middle} should be far below {start}");
    }

    [Fact]
    public void APureZoomStaysPut()
    {
        var f = Flight.To(Vw, 500, 500, 0.05f, 500, 500, 5f)!;
        f.Sample(f.DurationMs, out var x, out var y, out var s);

        Assert.Equal(500, x, 1);
        Assert.Equal(500, y, 1);
        Assert.Equal(5f, s, 1);
    }

    [Fact]
    public void ProgressNeverReverses()
    {
        var f = Far();
        float prev = float.NegativeInfinity;

        for (int i = 0; i <= 200; i++)
        {
            f.Sample(f.DurationMs * i / 200.0, out var x, out _, out _);
            Assert.True(x >= prev - 0.5f, $"x went backwards at step {i}: {x} after {prev}");
            prev = x;
        }
    }

    [Fact]
    public void TheZoomStaysPositiveThroughout()
    {
        var f = Far();
        for (int i = 0; i <= 200; i++)
        {
            f.Sample(f.DurationMs * i / 200.0, out _, out _, out var s);
            Assert.True(s > 0, $"zoom collapsed to {s} at step {i}");
        }
    }

    [Fact]
    public void DurationIsClampedToSomethingWatchable()
    {
        Assert.InRange(Far().DurationMs, 260, 1600);

        // a tiny hop and a huge one both land inside the same bounds
        Assert.InRange(Flight.To(Vw, 0, 0, 4f, 1, 1, 4.001f)!.DurationMs, 260, 1600);
        Assert.InRange(Flight.To(Vw, 0, 0, 12f, 900000, 900000, 0.01f)!.DurationMs, 260, 1600);
    }

    [Fact]
    public void AMoveToWhereYouAlreadyAreIsNotAFlight() =>
        Assert.Null(Flight.To(Vw, 10, 10, 2f, 10, 10, 2f));

    [Fact]
    public void AFlightAlwaysTerminates()
    {
        var f = Far();
        Assert.True(f.Sample(f.DurationMs - 1, out _, out _, out _), "still running just before the end");
        Assert.False(f.Sample(f.DurationMs + 1, out _, out _, out _), "finished past its duration");
    }

    [Fact]
    public void SamplingPastTheEndStillReportsTheTarget()
    {
        var f = Far();
        f.Sample(f.DurationMs * 10, out var x, out var y, out _);

        Assert.Equal(20000, x, 1);
        Assert.Equal(15000, y, 1);
    }
}

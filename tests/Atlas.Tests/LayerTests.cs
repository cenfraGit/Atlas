namespace Atlas.Tests;

/// <summary>Escape peels one layer off at a time.
///
/// Dialogs used to strand themselves: each handled Escape inside its own text
/// box, so the key only worked while that box held focus, and focus arrives a
/// frame late and can be taken by anything. These pin down the ordering and
/// the arithmetic; that the window sees the key at all is the other half, and
/// only a running app can show that.</summary>
public class LayerTests
{
    /// <summary>a layer that can be opened and shut, standing in for a dialog.</summary>
    sealed class Fake
    {
        public bool Open = true;
        public int Closed;
        public void Close() { Open = false; Closed++; }
    }

    static (Layers L, Fake A, Fake B, Fake C) Three()
    {
        var layers = new Layers();
        var a = new Fake();
        var b = new Fake();
        var c = new Fake();
        layers.Add("a", () => a.Open, a.Close);
        layers.Add("b", () => b.Open, b.Close);
        layers.Add("c", () => c.Open, c.Close);
        return (layers, a, b, c);
    }

    [Fact]
    public void NothingOpenMeansNothingToDismiss()
    {
        var layers = new Layers();
        layers.Add("a", () => false, () => Assert.Fail("closed a layer that was not open"));

        Assert.Null(layers.Dismiss());
        Assert.False(layers.AnyOpen);
    }

    [Fact]
    public void AnEmptyStackIsSafe() => Assert.Null(new Layers().Dismiss());

    [Fact]
    public void TheInnermostOpenLayerGoesFirst()
    {
        var (layers, a, b, c) = Three();

        Assert.Equal("a", layers.Dismiss());

        Assert.False(a.Open);
        Assert.True(b.Open);
        Assert.True(c.Open);
    }

    [Fact]
    public void OnlyOneLayerClosesPerPress()
    {
        var (layers, a, b, c) = Three();

        layers.Dismiss();

        Assert.Equal(1, a.Closed);
        Assert.Equal(0, b.Closed);
        Assert.Equal(0, c.Closed);
    }

    [Fact]
    public void RepeatedPressesWorkOutwards()
    {
        var (layers, _, _, _) = Three();

        Assert.Equal("a", layers.Dismiss());
        Assert.Equal("b", layers.Dismiss());
        Assert.Equal("c", layers.Dismiss());
        Assert.Null(layers.Dismiss());
    }

    [Fact]
    public void AClosedLayerIsSkipped()
    {
        var (layers, a, _, _) = Three();
        a.Close();

        Assert.Equal("b", layers.Dismiss());
    }

    [Fact]
    public void OpenReportsWhatIsShowingInnermostFirst()
    {
        var (layers, _, b, _) = Three();
        b.Close();

        Assert.Equal(["a", "c"], layers.Open);
    }

    [Fact]
    public void DismissAllShutsEverything()
    {
        var (layers, a, b, c) = Three();

        layers.DismissAll();

        Assert.False(a.Open || b.Open || c.Open);
        Assert.False(layers.AnyOpen);
    }

    [Fact]
    public void DismissAllTerminatesEvenWhenClosingOneOpensAnother()
    {
        // a panel that puts its parent back is a plausible thing to write;
        // it must not hang the app
        var layers = new Layers();
        var ping = new Fake();
        var pong = new Fake { Open = false };

        layers.Add("ping", () => ping.Open, () => { ping.Open = false; pong.Open = true; });
        layers.Add("pong", () => pong.Open, () => { pong.Open = false; ping.Open = true; });

        layers.DismissAll();   // must return rather than spin

        Assert.True(layers.Count == 2);
    }

    [Fact]
    public void AnyOpenTracksTheLayers()
    {
        var (layers, a, b, c) = Three();
        Assert.True(layers.AnyOpen);

        a.Close(); b.Close(); c.Close();
        Assert.False(layers.AnyOpen);
    }

    [Fact]
    public void ALayerIsAskedFreshEveryTime()
    {
        // the state lives in the overlay, not in a flag copied in here, or the
        // two drift and a dialog closed by other means still looks open
        int asked = 0;
        bool open = true;
        var layers = new Layers();
        layers.Add("a", () => { asked++; return open; }, () => open = false);

        layers.Dismiss();
        open = true;                 // opened again behind the stack's back
        Assert.Equal("a", layers.Dismiss());
        Assert.True(asked >= 2);
    }
}

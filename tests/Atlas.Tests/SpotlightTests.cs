using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using SkiaSharp;

namespace Atlas.Tests;

/// <summary>a spotlight for explaining something on a call: tab dims
/// everything but a circle round the pointer, so the people watching look
/// where you point.</summary>
[Collection("render")]
public class SpotlightTests
{
    static SKBitmap Frame(Scene scene)
    {
        var bmp = new SKBitmap(800, 600);
        using var canvas = new SKCanvas(bmp);
        scene.Draw(canvas, 800, 600);
        return bmp;
    }

    static int Brightness(SKColor c) => c.Red + c.Green + c.Blue;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]      // on a board too
    public void OutsideTheCircleIsDimmedAndInsideIsNot(bool board)
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));
        if (board)
        {
            var b = new Board { Id = "b", Name = "b" };
            // a light shape everywhere, so there is something to dim
            b.Items.Add(new BoardItem { Id = "s", Kind = "shape", X = -1000, Y = -1000, W = 2000, H = 2000, Fill = "#ffffff" });
            scene.ActiveBoard = b;
        }
        (scene.CamX, scene.CamY, scene.CamS) = (0, 0, 1);

        using var plain = Frame(scene);
        scene.Spotlight = new SKPoint(200, 300);
        using var lit = Frame(scene);

        Assert.Equal(plain.GetPixel(200, 300), lit.GetPixel(200, 300));
        var far = (x: 700, y: 50);
        Assert.True(Brightness(lit.GetPixel(far.x, far.y)) < Brightness(plain.GetPixel(far.x, far.y)) * 0.5,
            "the far corner was not dimmed");
        scene.ActiveBoard = null;
    }

    /// <summary>the soft edge only darkens. It blended from clear white to
    /// the dim, so the band between came out paler than the scene under it -
    /// a white ring round the circle.</summary>
    [Fact]
    public void TheEdgeIsNeverPalerThanTheSceneUnderIt()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));
        (scene.CamX, scene.CamY, scene.CamS) = (-100000, -100000, 1);   // bare background

        using var plain = Frame(scene);
        scene.Spotlight = new SKPoint(400, 300);
        using var lit = Frame(scene);

        float r = scene.SpotlightRadius;
        for (float d = r - 5; d < r * 1.35f; d += 2)
        {
            int x = (int)(400 + d);
            Assert.True(Brightness(lit.GetPixel(x, 300)) <= Brightness(plain.GetPixel(x, 300)) + 1,
                $"{d:0} from the centre it was paler than the scene: {lit.GetPixel(x, 300)} over {plain.GetPixel(x, 300)}");
        }
    }

    [AvaloniaFact]
    public void TabTurnsItOnItFollowsThePointerAndEscapeTurnsItOff()
    {
        using var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        var view = new SceneView(scene);
        view.AttachBoards(store, new BoardOverlay(store));
        view.BuildLayers();
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        view.Focus();

        window.MouseMove(new Avalonia.Point(100, 100));
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Assert.Equal(new SKPoint(100, 100), scene.Spotlight);

        window.MouseMove(new Avalonia.Point(300, 250));
        Assert.Equal(new SKPoint(300, 250), scene.Spotlight);

        float r = scene.SpotlightRadius;
        window.MouseWheel(new Avalonia.Point(300, 250), new Avalonia.Vector(0, 1), RawInputModifiers.Alt);
        Assert.True(scene.SpotlightRadius > r);

        Assert.True(view.Escape());
        Assert.False(view.SpotlightOn);
        // it fades out over a few frames, then is gone
        Thread.Sleep(300);
        // the next frame is posted, as the app keeps drawing while it fades
        view.InvalidateVisual();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using (window.CaptureRenderedFrame()) { }
        Assert.True(scene.Spotlight is null, $"amount {scene.SpotlightAmount} on {view.SpotlightOn} bounds {view.Bounds}");
    }

    /// <summary>it eases in: straight after tab the dimming is partway, and
    /// a moment later it is all the way.</summary>
    [AvaloniaFact]
    public void ItComesInRatherThanAppearing()
    {
        using var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        var view = new SceneView(scene);
        view.AttachBoards(store, new BoardOverlay(store));
        view.BuildLayers();
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();

        view.ToggleSpotlight();
        using (window.CaptureRenderedFrame()) { }
        Assert.True(scene.SpotlightAmount < 1, "it arrived all at once");

        Thread.Sleep(300);
        // the next frame is posted, as the app keeps drawing while it fades
        view.InvalidateVisual();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using (window.CaptureRenderedFrame()) { }
        Assert.Equal(1f, scene.SpotlightAmount);
    }
}

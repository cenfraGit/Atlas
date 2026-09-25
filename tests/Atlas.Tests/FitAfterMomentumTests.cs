using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Atlas.Tests;

/// <summary>F fits the view even while the camera is still easing.
///
/// A wheel notch or a flick leaves the camera gliding toward where it was
/// heading, and pressing F then framed the view for one frame before the
/// glide dragged it back to where the momentum was taking it.</summary>
public class FitAfterMomentumTests
{
    const int W = 900, H = 700;

    static (SceneView View, Window Window, Scene Scene, TempDir Repo) Open(bool board)
    {
        var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        if (board)
        {
            var b = store.Create("fit");
            b.Items.Add(new BoardItem { Id = "a", Kind = "shape", X = 0, Y = 0, W = 300, H = 200 });
            b.Items.Add(new BoardItem { Id = "b", Kind = "shape", X = 2000, Y = 1500, W = 300, H = 200 });
            scene.ActiveBoard = b;
        }
        (scene.CamX, scene.CamY, scene.CamS) = (500, 400, 1);

        var view = new SceneView(scene);
        view.AttachBoards(store, new BoardOverlay(store));
        view.BuildLayers();
        var window = new Window { Width = W, Height = H, Content = view };
        window.Show();
        window.Measure(new Avalonia.Size(W, H));
        window.Arrange(new Avalonia.Rect(0, 0, W, H));
        return (view, window, scene, repo);
    }

    static void Frames(SceneView view, Window window, double seconds)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            view.InvalidateVisual();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (window.CaptureRenderedFrame()) { }
            Thread.Sleep(16);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FitAfterAWheelNotchStaysFitted(bool board)
    {
        var (view, window, scene, repo) = Open(board);
        using var _ = repo;

        // settle where F leaves the camera with nothing moving
        view.HandleKey(Key.F);
        Frames(view, window, 1.2);
        var fitted = (scene.CamX, scene.CamY, scene.CamS);

        // start a glide, then fit before it has finished
        (scene.CamX, scene.CamY, scene.CamS) = (500, 400, 1);
        window.MouseWheel(new Avalonia.Point(100, 100), new Avalonia.Vector(0, 3));
        Frames(view, window, 0.03);
        view.HandleKey(Key.F);
        Frames(view, window, 1.2);

        Assert.Equal(fitted.CamX, scene.CamX, 1);
        Assert.Equal(fitted.CamY, scene.CamY, 1);
        Assert.Equal(fitted.CamS, scene.CamS, 3);
        scene.ActiveBoard = null;
    }

    /// <summary>and after a flick - a drag let go of while still moving,
    /// which throws the canvas.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FitAfterAFlickStaysFitted(bool board)
    {
        var (view, window, scene, repo) = Open(board);
        using var _ = repo;
        view.HandleKey(Key.F);
        Frames(view, window, 1.2);
        var fitted = (scene.CamX, scene.CamY, scene.CamS);

        (scene.CamX, scene.CamY, scene.CamS) = (500, 400, 1);
        window.MouseDown(new Avalonia.Point(400, 300), MouseButton.Left);
        for (int i = 1; i <= 6; i++)
        {
            window.MouseMove(new Avalonia.Point(400 - i * 40, 300 - i * 25), RawInputModifiers.LeftMouseButton);
            Thread.Sleep(8);
        }
        window.MouseUp(new Avalonia.Point(160, 150), MouseButton.Left);
        Frames(view, window, 0.03);
        view.HandleKey(Key.F);
        Frames(view, window, 1.2);

        Assert.Equal(fitted.CamX, scene.CamX, 1);
        Assert.Equal(fitted.CamY, scene.CamY, 1);
        Assert.Equal(fitted.CamS, scene.CamS, 3);
        scene.ActiveBoard = null;
    }
}

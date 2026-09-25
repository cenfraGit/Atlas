using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Atlas.Tests;

/// <summary>a rubberband begun while the last one is still fading shows.
///
/// Each release started a fade timer of its own and nothing stopped it, so
/// the old timer went on fading - and then clearing - the new band while it
/// was being dragged. It stayed invisible until let go, when its own fade
/// began at full strength.</summary>
public class RubberbandFadeTests
{
    [AvaloniaFact]
    public void ABandStartedDuringTheLastOnesFadeStaysVisible()
    {
        using var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        scene.ActiveBoard = store.Create("band");
        (scene.CamX, scene.CamY, scene.CamS) = (0, 0, 1);
        var view = new SceneView(scene);
        view.AttachBoards(store, new BoardOverlay(store));
        view.BuildLayers();
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        window.Measure(new Avalonia.Size(800, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        view.SetEditing(true);

        void Band(double x0, double x1, bool release)
        {
            window.MouseDown(new Avalonia.Point(x0, 200), MouseButton.Left);
            window.MouseMove(new Avalonia.Point((x0 + x1) / 2, 300), RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Avalonia.Point(x1, 400), RawInputModifiers.LeftMouseButton);
            if (release) window.MouseUp(new Avalonia.Point(x1, 400), MouseButton.Left);
        }

        Band(100, 300, release: true);          // its fade starts
        Band(400, 600, release: false);         // and this one is being dragged
        var until = DateTime.UtcNow.AddMilliseconds(300);
        while (DateTime.UtcNow < until)
        {
            view.InvalidateVisual();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (window.CaptureRenderedFrame()) { }
            Thread.Sleep(16);
        }

        Assert.NotNull(scene.Rubberband);
        Assert.Equal(1f, scene.RubberbandFade);
        // and it still fades out once let go
        window.MouseUp(new Avalonia.Point(600, 400), MouseButton.Left);
        until = DateTime.UtcNow.AddMilliseconds(400);
        while (DateTime.UtcNow < until)
        {
            view.InvalidateVisual();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (window.CaptureRenderedFrame()) { }
            Thread.Sleep(16);
        }
        Assert.Null(scene.Rubberband);
        scene.ActiveBoard = null;
    }
}

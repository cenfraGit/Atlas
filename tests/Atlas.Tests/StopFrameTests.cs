using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using SkiaSharp;

namespace Atlas.Tests;

/// <summary>where the tour's stops are, on the board.
///
/// The tour panel listed "stop 4" and nothing on the board said which region
/// that was. While the panel is open each stop is a faint numbered frame,
/// the selected one brighter.</summary>
public class StopFrameTests
{
    static (Scene Scene, TempDir Repo) Board()
    {
        var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var board = new Board { Id = "b", Name = "b" };
        board.Items.Add(new BoardItem { Id = "n", Kind = "note", Text = "x", X = -50, Y = -20, W = 100 });
        board.Stops.Add(new Stop { Name = "one", X = 0, Y = 0, W = 400, H = 200 });
        scene.ActiveBoard = board;
        (scene.CamX, scene.CamY, scene.CamS) = (0, 0, 1);
        return (scene, repo);
    }

    /// <summary>the pixel on the frame's left edge, halfway down - on a dash,
    /// since the dash pattern starts at the corner and 100 is inside one.</summary>
    static SKColor LeftEdge(Scene scene)
    {
        using var bmp = new SKBitmap(800, 400);
        using (var canvas = new SKCanvas(bmp)) scene.Draw(canvas, 800, 400);
        // the frame runs from x = -200 to 200 around the middle of the image;
        // scan a few pixels either side of its left edge for anything drawn
        return Enumerable.Range(198, 5).Select(x => bmp.GetPixel(x, 200 - 96)).MaxBy(c => c.Red + c.Green + c.Blue);
    }

    [Fact]
    public void AFrameIsDrawnOnlyWhileStopsAreShown()
    {
        var (scene, repo) = Board();
        using (repo) using (scene)
        {
            var hidden = LeftEdge(scene);
            scene.StopsShown = true;
            var shown = LeftEdge(scene);

            Assert.True(shown.Red + shown.Green + shown.Blue > hidden.Red + hidden.Green + hidden.Blue + 60,
                $"no frame: {hidden} then {shown}");
            scene.ActiveBoard = null;
        }
    }

    [AvaloniaFact]
    public void TheTourPanelShowsThem()
    {
        var repo = SampleRepo.Build();
        using var _ = repo;
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        var board = store.Create("t");
        board.Stops.Add(new Stop { X = 0, Y = 0, W = 400, H = 200 });
        scene.ActiveBoard = board;

        var view = new SceneView(scene);
        var panel = new TourPanel { Transitions = null };
        view.AttachBoards(store, new BoardOverlay(store));
        view.AttachTour(panel);
        view.BuildLayers();
        var grid = new Grid();
        grid.Children.Add(view);
        grid.Children.Add(panel);
        var window = new Window { Width = 800, Height = 600, Content = grid };
        window.Show();

        using (window.CaptureRenderedFrame()) { }
        Assert.False(scene.StopsShown);

        view.HandleKey(Key.M, KeyModifiers.Shift);
        using (window.CaptureRenderedFrame()) { }
        Assert.True(scene.StopsShown);
        Assert.Equal(0, scene.StopPicked);

        view.HandleKey(Key.M, KeyModifiers.Shift);
        using (window.CaptureRenderedFrame()) { }
        Assert.False(scene.StopsShown);
        scene.ActiveBoard = null;
    }
}

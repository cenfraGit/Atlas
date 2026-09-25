using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Atlas.Tests;

/// <summary>snapping to the grid applies to resizing as well as moving.
///
/// Only a move snapped, so boxes could be lined up by where they started but
/// not by where they ended - which is most of what lining up means.</summary>
public class ResizeSnapTests
{
    const int W = 800, H = 600;

    static (SceneView View, Window Window, Scene Scene, BoardItem Box, TempDir Repo) Open(bool snap)
    {
        var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        var board = store.Create("snap");
        var box = new BoardItem { Id = "s", Kind = "shape", X = -100, Y = -50, W = 200, H = 100 };
        board.Items.Add(box);
        scene.ActiveBoard = board;
        (scene.CamX, scene.CamY, scene.CamS) = (0, 0, 1);

        var view = new SceneView(scene);
        view.AttachBoards(store, new BoardOverlay(store));
        view.BuildLayers();
        var window = new Window { Width = W, Height = H, Content = view };
        window.Show();
        window.Measure(new Avalonia.Size(W, H));
        window.Arrange(new Avalonia.Rect(0, 0, W, H));
        view.SetEditing(true);
        view.SnapToGrid = snap;
        scene.Picked.Add("s");
        return (view, window, scene, box, repo);
    }

    static Avalonia.Point At(float x, float y) => new(W / 2 + x, H / 2 + y);

    static void Drag(Window w, Avalonia.Point from, Avalonia.Point to)
    {
        w.MouseMove(from);
        w.MouseDown(from, MouseButton.Left);
        w.MouseMove(new Avalonia.Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
        w.MouseMove(to, RawInputModifiers.LeftMouseButton);
        w.MouseUp(to, MouseButton.Left);
    }

    static bool OnGrid(float v, float cell) => Math.Abs(v / cell - MathF.Round(v / cell)) < 0.001f;

    [AvaloniaFact]
    public void ACornerDraggedWithSnapOnLandsOnTheGrid()
    {
        var (_, window, scene, box, repo) = Open(snap: true);
        using var _ = repo;
        float cell = scene.SnapStep(0);
        Assert.True(cell > 0);

        Drag(window, At(100, 50), At(133, 71));

        Assert.True(OnGrid(box.X + box.W, cell), $"right edge {box.X + box.W} is off a {cell} grid");
        Assert.True(OnGrid(box.Y + box.H, cell), $"bottom edge {box.Y + box.H} is off a {cell} grid");
        Assert.Equal((-100f, -50f), (box.X, box.Y));     // the far corner stays put
    }

    [AvaloniaFact]
    public void AWallDraggedWithSnapOnLandsOnTheGrid()
    {
        var (_, window, scene, box, repo) = Open(snap: true);
        using var _ = repo;
        float cell = scene.SnapStep(0);

        Drag(window, At(100, 0), At(137, 0));

        Assert.True(OnGrid(box.X + box.W, cell), $"right edge {box.X + box.W} is off a {cell} grid");
        Assert.Equal(100f, box.H);
    }

    [AvaloniaFact]
    public void WithSnapOffTheEdgeGoesWhereItIsLetGo()
    {
        var (_, window, _, box, repo) = Open(snap: false);
        using var _ = repo;

        Drag(window, At(100, 50), At(133, 71));

        Assert.Equal((133f, 71f), (box.X + box.W, box.Y + box.H));
    }
}

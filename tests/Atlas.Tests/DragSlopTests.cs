using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Atlas.Tests;

/// <summary>a click is not a drag.
///
/// A press on an item that wobbled a pixel or two moved it by that much,
/// and letting go re-pinned and saved the board even when nothing moved -
/// so clicking a box rewrote its file. A press only turns into a move, a
/// resize or an arrow-end drag once the pointer has really travelled.</summary>
public class DragSlopTests
{
    const int W = 800, H = 600;

    static (Window Window, Scene Scene, Board Board, TempDir Repo) Open()
    {
        var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        var board = store.Create("slop");
        board.Items.Add(new BoardItem { Id = "s", Kind = "shape", X = -100, Y = -50, W = 200, H = 100 });
        store.Save(board);
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
        return (window, scene, board, repo);
    }

    static Avalonia.Point At(double x, double y) => new(W / 2 + x, H / 2 + y);

    [AvaloniaTheory]
    [InlineData(0, 0)]          // the middle: a move
    [InlineData(100, 50)]       // the corner: a resize
    [InlineData(0, 50)]         // the bottom wall: a resize
    public void AWobblingClickChangesNothing(double x, double y)
    {
        var (window, scene, board, repo) = Open();
        using var _ = repo;
        scene.Picked.Add("s");
        var before = File.ReadAllText(board.Path);

        window.MouseDown(At(x, y), MouseButton.Left);
        window.MouseMove(At(x + 1, y + 2), RawInputModifiers.LeftMouseButton);
        window.MouseUp(At(x + 1, y + 2), MouseButton.Left);

        var s = board.Items.Single();
        Assert.Equal((-100f, -50f, 200f, 100f), (s.X, s.Y, s.W, s.H));
        Assert.Equal(before, File.ReadAllText(board.Path));
    }

    /// <summary>and past the slop the move is the whole distance from the
    /// press, not what was left after the dead zone.</summary>
    [AvaloniaFact]
    public void ARealDragMovesTheWholeWay()
    {
        var (window, scene, board, repo) = Open();
        using var _ = repo;
        scene.Picked.Add("s");

        window.MouseDown(At(0, 0), MouseButton.Left);
        window.MouseMove(At(2, 0), RawInputModifiers.LeftMouseButton);
        window.MouseMove(At(30, 0), RawInputModifiers.LeftMouseButton);
        window.MouseUp(At(30, 0), MouseButton.Left);

        Assert.Equal(-70f, board.Items.Single().X, 1);
    }
}

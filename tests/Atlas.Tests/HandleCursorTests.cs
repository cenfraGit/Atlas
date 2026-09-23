using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Atlas.Tests;

/// <summary>the cursor says when a press will resize.
///
/// Over a corner or a wall the cursor stayed the move cross, so there was no
/// telling a grab that would resize from one that would drag the whole
/// thing. It is the matching resize cursor now, asked in the order a press
/// resolves in: corner, then wall.</summary>
public class HandleCursorTests
{
    const int W = 800, H = 600;

    // a shape from -100,-50 to 100,50, picked, at scale 1 around the origin
    static (SceneView View, Window Window, Scene Scene, TempDir Repo) Open(bool editing = true)
    {
        var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        var board = store.Create("cursors", "b");
        board.Items.Add(new BoardItem { Id = "s", Kind = "shape", X = -100, Y = -50, W = 200, H = 100 });
        scene.ActiveBoard = board;
        scene.CamX = 0;
        scene.CamY = 0;
        scene.CamS = 1f;

        var view = new SceneView(scene);
        view.AttachBoards(store, new BoardOverlay(store));
        view.BuildLayers();
        var window = new Window { Width = W, Height = H, Content = view };
        window.Show();
        window.Measure(new Avalonia.Size(W, H));
        window.Arrange(new Avalonia.Rect(0, 0, W, H));
        view.SetEditing(editing);
        // after: changing mode clears what is picked
        scene.Picked.Add("s");
        return (view, window, scene, repo);
    }

    static Avalonia.Point At(float x, float y) => new(W / 2 + x, H / 2 + y);

    [AvaloniaTheory]
    [InlineData(-100f, -50f, StandardCursorType.TopLeftCorner)]
    [InlineData(100f, -50f, StandardCursorType.TopRightCorner)]
    [InlineData(-100f, 50f, StandardCursorType.BottomLeftCorner)]
    [InlineData(100f, 50f, StandardCursorType.BottomRightCorner)]
    [InlineData(0f, -50f, StandardCursorType.SizeNorthSouth)]
    [InlineData(0f, 50f, StandardCursorType.SizeNorthSouth)]
    [InlineData(-100f, 0f, StandardCursorType.SizeWestEast)]
    [InlineData(100f, 0f, StandardCursorType.SizeWestEast)]
    public void EachHandleHasItsCursor(float x, float y, StandardCursorType expected)
    {
        var (view, window, _, repo) = Open();
        using var _ = repo;

        window.MouseMove(At(x, y));

        Assert.Equal(expected, view.HandleCursor);
    }

    [AvaloniaFact]
    public void OffTheHandlesItIsTheMoveCursorAgain()
    {
        var (view, window, _, repo) = Open();
        using var _ = repo;

        window.MouseMove(At(100, 50));
        window.MouseMove(At(0, 0));

        Assert.Null(view.HandleCursor);
    }

    /// <summary>handles only mean anything while editing, and only on
    /// something picked.</summary>
    [AvaloniaFact]
    public void OutsideEditModeThereAreNoHandles()
    {
        var (view, window, _, repo) = Open(editing: false);
        using var _ = repo;

        window.MouseMove(At(100, 50));

        Assert.Null(view.HandleCursor);
    }

    [AvaloniaFact]
    public void SomethingNotPickedHasNoHandles()
    {
        var (view, window, scene, repo) = Open();
        using var _ = repo;
        scene.Picked.Clear();

        window.MouseMove(At(100, 50));

        Assert.Null(view.HandleCursor);
    }

    /// <summary>during a resize the cursor stays what it was when the drag
    /// began, even when the pointer runs ahead of the corner it is pulling.</summary>
    [AvaloniaFact]
    public void AResizeKeepsItsCursor()
    {
        var (view, window, _, repo) = Open();
        using var _ = repo;

        window.MouseMove(At(100, 50));
        window.MouseDown(At(100, 50), MouseButton.Left);
        window.MouseMove(At(300, 250), RawInputModifiers.LeftMouseButton);

        Assert.Equal(StandardCursorType.BottomRightCorner, view.HandleCursor);
        window.MouseUp(At(300, 250), MouseButton.Left);
    }
}

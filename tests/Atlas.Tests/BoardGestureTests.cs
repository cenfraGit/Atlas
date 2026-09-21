using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Atlas.Tests;

/// <summary>what a gesture on a board leaves behind.
///
/// These drive a real SceneView in a real window off screen, with real
/// pointer events, because the rule is about the handlers rather than about
/// anything Scene could be asked.</summary>
public class BoardGestureTests
{
    const int W = 800, H = 600;

    /// <summary>a board with one note sitting under the middle of the window,
    /// so a click at the centre lands on it.</summary>
    static (SceneView View, Window Window, Board Board, BoardItem Note, Scene Scene, TempDir Repo) Board()
    {
        var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));

        // through the store, so the board has a file to be written to
        var store = BoardStore.Load(repo.Path);
        var board = store.Create("gestures", "b");

        var note = new BoardItem
        {
            Id = "n", Kind = "note", X = -100, Y = -60, W = 200, H = 120, Text = "a note",
        };
        board.Items.Add(note);

        scene.ActiveBoard = board;
        scene.CamX = 0;
        scene.CamY = 0;
        scene.CamS = 1f;

        var view = new SceneView(scene);
        view.AttachBoards(store, new BoardOverlay(store));
        var window = new Window { Width = W, Height = H, Content = view };
        window.Show();
        window.Measure(new Avalonia.Size(W, H));
        window.Arrange(new Avalonia.Rect(0, 0, W, H));
        view.SetEditing(true);

        return (view, window, board, note, scene, repo);
    }

    static Avalonia.Point Centre => new(W / 2, H / 2);

    static void Click(Window window, Avalonia.Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
    }

    static void Drag(Window window, Avalonia.Point from, Avalonia.Point to)
    {
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
    }

    /// <summary>clicking something to select it changes nothing, so there is
    /// nothing to undo. It used to record a snapshot on every press, so three
    /// clicks meant three ctrl+Z presses before the last real change came
    /// back.</summary>
    [AvaloniaFact]
    public void SelectingSomethingIsNotAnUndoStep()
    {
        var (view, window, _, _, scene, repo) = Board();
        using (repo)
        using (scene)
        {
            Click(window, Centre);
            Click(window, Centre);
            Click(window, Centre);

            Assert.False(view.CanUndo);
            scene.ActiveBoard = null;
        }
    }

    [AvaloniaFact]
    public void MovingSomethingIs()
    {
        var (view, window, _, note, scene, repo) = Board();
        using (repo)
        using (scene)
        {
            Drag(window, Centre, new Avalonia.Point(Centre.X + 60, Centre.Y));

            Assert.True(note.X > -100, $"the note did not move, it is at {note.X}");
            Assert.True(view.CanUndo);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>one gesture, one step: dragging is a stream of moves and each
    /// one must not record its own.</summary>
    [AvaloniaFact]
    public void ALongDragIsStillOneUndoStep()
    {
        var (view, window, board, note, scene, repo) = Board();
        using (repo)
        using (scene)
        {
            float before = note.X;
            window.MouseDown(Centre, MouseButton.Left);
            for (int i = 1; i <= 20; i++) window.MouseMove(new Avalonia.Point(Centre.X + i * 5, Centre.Y));
            window.MouseUp(new Avalonia.Point(Centre.X + 100, Centre.Y), MouseButton.Left);

            Assert.True(note.X > before);

            view.Focus();
            window.KeyPress(Key.Z, RawInputModifiers.Control);

            Assert.Equal(before, board.Items[0].X, 1);
            Assert.False(view.CanUndo);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>and pressing on empty canvas and letting go, which is how a
    /// selection is cleared, is not a change either.</summary>
    [AvaloniaFact]
    public void ClearingTheSelectionIsNotAnUndoStep()
    {
        var (view, window, _, _, scene, repo) = Board();
        using (repo)
        using (scene)
        {
            Click(window, new Avalonia.Point(40, 40));

            Assert.False(view.CanUndo);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>a stroke is written out the moment it is finished.
    ///
    /// The stroke branch set the dirty flag and returned before the save, so
    /// a drawing lived in memory until some later gesture happened to write
    /// the board. Ink is the one thing you make dozens of in a row.</summary>
    [AvaloniaFact]
    public void AStrokeIsOnDiskAsSoonAsItIsDrawn()
    {
        var (view, window, board, _, scene, repo) = Board();
        using (repo)
        using (scene)
        {
            view.Focus();
            window.KeyPress(Key.B, RawInputModifiers.None);      // brush on

            window.MouseDown(new Avalonia.Point(200, 200), MouseButton.Left);
            for (int i = 1; i <= 10; i++) window.MouseMove(new Avalonia.Point(200 + i * 10, 200 + i * 4));
            window.MouseUp(new Avalonia.Point(300, 240), MouseButton.Left);

            Assert.Contains(board.Items, i => i.Kind == "stroke");
            Assert.Contains("\"stroke\"", File.ReadAllText(board.Path));
            scene.ActiveBoard = null;
        }
    }
}

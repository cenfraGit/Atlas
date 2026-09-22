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
        view.BuildLayers();
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

    static void ClickWith(Window window, Avalonia.Point at, RawInputModifiers mods)
    {
        window.MouseDown(at, MouseButton.Left, mods);
        window.MouseUp(at, MouseButton.Left, mods);
    }

    /// <summary>a second item to add to a selection, well clear of the note
    /// the fixture already has.</summary>
    static BoardItem Second(Board board)
    {
        var it = new BoardItem
        {
            Id = "n2", Kind = "note", X = 60, Y = -60, W = 120, H = 120, Text = "another",
        };
        board.Items.Add(it);
        return it;
    }

    /// <summary>ctrl+click adds to the selection, the same as shift.
    ///
    /// The map accepted both and a board only shift, so ctrl+click on a
    /// board threw the selection away and started a new one - for every
    /// kind of item, whatever it was noticed on.</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void CtrlAndShiftBothAddToTheSelection(bool ctrl)
    {
        var (_, window, board, note, scene, repo) = Board();
        using (repo)
        using (scene)
        {
            Second(board);
            var mods = ctrl ? RawInputModifiers.Control : RawInputModifiers.Shift;

            Click(window, Centre);                              // the first note
            Assert.Equal([note.Id], scene.Picked);

            ClickWith(window, new Avalonia.Point(W / 2 + 120, H / 2), mods);

            Assert.Equal(2, scene.Picked.Count);
            Assert.Contains("n2", scene.Picked);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>and clicking a picked one again with the modifier takes it
    /// out, which is what makes it a toggle rather than an add.</summary>
    [AvaloniaFact]
    public void CtrlClickingAPickedItemDropsIt()
    {
        var (_, window, board, note, scene, repo) = Board();
        using (repo)
        using (scene)
        {
            Second(board);
            Click(window, Centre);
            ClickWith(window, new Avalonia.Point(W / 2 + 120, H / 2), RawInputModifiers.Control);
            Assert.Equal(2, scene.Picked.Count);

            ClickWith(window, Centre, RawInputModifiers.Control);

            Assert.Equal(["n2"], scene.Picked);
            Assert.DoesNotContain(note.Id, scene.Picked);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>without a modifier, clicking something that is not picked
    /// replaces the selection - or there would be no way back to one.
    ///
    /// Clicking one that *is* picked deliberately leaves the selection
    /// alone: that is how a group of things is dragged by grabbing one of
    /// them.</summary>
    [AvaloniaFact]
    public void APlainClickOnSomethingElseReplacesTheSelection()
    {
        var (_, window, board, note, scene, repo) = Board();
        using (repo)
        using (scene)
        {
            Second(board);
            board.Items.Add(new BoardItem
            {
                Id = "n3", Kind = "note", X = -260, Y = -60, W = 120, H = 120, Text = "third",
            });

            Click(window, Centre);
            ClickWith(window, new Avalonia.Point(W / 2 + 120, H / 2), RawInputModifiers.Control);
            Assert.Equal(2, scene.Picked.Count);

            Click(window, new Avalonia.Point(W / 2 - 200, H / 2));

            Assert.Equal(["n3"], scene.Picked);
            scene.ActiveBoard = null;
        }
    }

    [AvaloniaFact]
    public void APlainClickOnOneOfManyKeepsThemAllForDragging()
    {
        var (_, window, board, note, scene, repo) = Board();
        using (repo)
        using (scene)
        {
            Second(board);
            Click(window, Centre);
            ClickWith(window, new Avalonia.Point(W / 2 + 120, H / 2), RawInputModifiers.Control);

            Click(window, Centre);

            Assert.Equal(2, scene.Picked.Count);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>A adds a file to the board, which is what the toolbar's
    /// "file  A" button does.
    ///
    /// The key fell through to the map's A, which refuses outright while a
    /// board is open - so the button worked and the key it advertised did
    /// nothing.</summary>
    [AvaloniaFact]
    public void AOpensTheFilePickerOnABoard()
    {
        var (view, _, _, _, scene, repo) = Board();
        using (repo)
        using (scene)
        {
            int asked = 0;
            view.OpenSearch = () => asked++;

            view.HandleKey(Key.A);

            Assert.Equal(1, asked);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>and on the map it still means "add what I am looking at",
    /// which is a different thing and the reason the two were confused.</summary>
    [AvaloniaFact]
    public void AOnTheMapDoesNotOpenThePicker()
    {
        var (view, _, _, _, scene, repo) = Board();
        using (repo)
        using (scene)
        {
            scene.ActiveBoard = null;
            int asked = 0;
            view.OpenSearch = () => asked++;

            view.HandleKey(Key.A);

            Assert.Equal(0, asked);
        }
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

    /// <summary>Escape does not leave a board.
    ///
    /// It closes what is open - a dialog, a menu, an armed tool, a
    /// selection - and leaving is not closing anything, it is going
    /// somewhere else. Alt+left and the "&lt;" button do that. When Escape
    /// did leave, opening a dialog on a board meant one key both dismissed
    /// the dialog and threw you off the board.</summary>
    [AvaloniaFact]
    public void EscapeDoesNotLeaveABoard()
    {
        var (view, _, _, _, scene, repo) = Board();
        using (repo)
        using (scene)
        {
            Assert.False(view.Escape());
            Assert.NotNull(scene.ActiveBoard);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>what it does instead is clear the selection, and then have
    /// nothing left to do.</summary>
    [AvaloniaFact]
    public void EscapeClearsASelectionAndStopsThere()
    {
        var (view, window, _, _, scene, repo) = Board();
        using (repo)
        using (scene)
        {
            Click(window, Centre);
            Assert.NotEmpty(scene.Picked);

            Assert.True(view.Escape());
            Assert.Empty(scene.Picked);
            Assert.NotNull(scene.ActiveBoard);

            Assert.False(view.Escape());
            Assert.NotNull(scene.ActiveBoard);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>alt+left is the way out, and the bar along the bottom has to
    /// say so - it said "esc" while Escape did nothing, which is how the
    /// key came to be added back.</summary>
    [AvaloniaFact]
    public void AltLeftLeavesTheBoard()
    {
        var (view, _, _, _, scene, repo) = Board();
        using (repo)
        using (scene)
        {
            view.HandleKey(Key.Left, KeyModifiers.Alt);
            Assert.Null(scene.ActiveBoard);
        }
    }
}

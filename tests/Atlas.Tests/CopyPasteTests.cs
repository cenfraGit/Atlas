using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Atlas.Tests;

/// <summary>ctrl+C and ctrl+V on a board, for every kind of item.
///
/// The copy used to list the fields it carried by hand, so anything added
/// to a board item after it was written was dropped on the way through: a
/// stroke pasted as an empty box, an arrow lost its ties, a window lost its
/// anchor. And there was no ctrl+C key at all, only the menu.
///
/// Pasting goes through <see cref="SceneView.Paste"/> rather than ctrl+V,
/// because ctrl+V prefers an image on the real Windows clipboard, and what is
/// on the clipboard of the machine running the suite is nobody's fixture.</summary>
public class CopyPasteTests
{
    const int W = 800, H = 600;

    static (SceneView View, Board Board, Scene Scene, TempDir Repo) Board(params BoardItem[] items)
    {
        var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        var board = store.Create("copies", "b");
        board.Items.AddRange(items);

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
        return (view, board, scene, repo);
    }

    static void CopyAll(SceneView view, Scene scene, Board board)
    {
        scene.Picked.Clear();
        foreach (var it in board.Items) scene.Picked.Add(it.Id);
        view.HandleKey(Key.C, KeyModifiers.Control);
    }

    /// <summary>screen point of a world position, at the fixture's camera.</summary>
    static Avalonia.Point Screen(float x, float y) => new(W / 2 + x, H / 2 + y);

    static List<BoardItem> Pasted(Board board, int before) => board.Items.Skip(before).ToList();

    [AvaloniaFact]
    public void EveryFieldOfAShapeSurvivesTheTrip()
    {
        var shape = new BoardItem
        {
            Id = "s", Kind = "ellipse", X = 0, Y = 0, W = 100, H = 60, Text = "words",
            Color = "80FF0000", Fill = BoardItem.NoFill, Weight = 3, Size = 22,
        };
        var (view, board, scene, repo) = Board(shape);
        using var _ = repo;

        CopyAll(view, scene, board);
        view.Paste(Screen(-300, -200));

        var copy = Assert.Single(Pasted(board, 1));
        Assert.NotEqual("s", copy.Id);
        Assert.Equal(("ellipse", "words", "80FF0000", BoardItem.NoFill, 3f, 22f, 100f, 60f),
            (copy.Kind, copy.Text, copy.Color, copy.Fill, copy.Weight, copy.Size, copy.W, copy.H));
        Assert.Equal((-300f, -200f), (copy.X, copy.Y));
        Assert.Equal((0f, 0f), (shape.X, shape.Y));            // the original stays put
    }

    /// <summary>a stroke is its ink. It pasted as an empty box, because the
    /// points were not one of the fields the copy knew about.</summary>
    [AvaloniaFact]
    public void AStrokeCarriesItsInkAndMovesIt()
    {
        var ink = new BoardItem { Id = "k", Kind = "stroke", Points = [0, 0, 50, 20, 100, 0], Weight = 4 };
        Strokes.Reframe(ink);
        var (view, board, scene, repo) = Board(ink);
        using var _ = repo;

        CopyAll(view, scene, board);
        view.Paste(Screen(200, 100));

        var copy = Assert.Single(Pasted(board, 1));
        // the same ink, shifted as one piece
        float dx = copy.Points![0] - ink.Points![0], dy = copy.Points[1] - ink.Points[1];
        Assert.True(dx > 150 && dy > 50);
        for (int i = 0; i < ink.Points.Count; i += 2)
            Assert.Equal((ink.Points[i] + dx, ink.Points[i + 1] + dy), (copy.Points[i], copy.Points[i + 1]));
        Assert.Equal(4f, copy.Weight);
        Assert.Equal([0, 0, 50, 20, 100, 0], ink.Points!);
        Assert.NotSame(ink.Points, copy.Points);
    }

    [AvaloniaFact]
    public void AnImagePastesOntoTheSameFile()
    {
        var img = new BoardItem { Id = "i", Kind = "image", File = "abc.png", X = 0, Y = 0, W = 200, H = 100 };
        var (view, board, scene, repo) = Board(img);
        using var _ = repo;

        CopyAll(view, scene, board);
        view.Paste(Screen(-200, 0));

        var copy = Assert.Single(Pasted(board, 1));
        Assert.Equal(("image", "abc.png", 200f, 100f), (copy.Kind, copy.File, copy.W, copy.H));
    }

    /// <summary>two windows onto the same file, each cropped to a different
    /// method, is the reason to copy a window at all. The copy is a second
    /// reference with its own range, not a link to the first.</summary>
    [AvaloniaFact]
    public void ACopiedWindowIsASecondIndependentView()
    {
        var win = new BoardItem
        {
            Id = "w", Kind = "file", File = "app/ui/Panel.cs", Key = "k1",
            Line = 2, EndLine = 5, X = -300, Y = -200, W = 300,
            Symbol = "Panel", Offset = 1, Context = "ctx",
        };
        var (view, board, scene, repo) = Board(win);
        using var _ = repo;

        CopyAll(view, scene, board);
        view.Paste(Screen(50, -200));
        var copy = Assert.Single(Pasted(board, 1));

        Assert.Equal(("file", "app/ui/Panel.cs", "k1", 2, 5, "Panel", 1, "ctx"),
            (copy.Kind, copy.File, copy.Key, copy.Line, copy.EndLine, copy.Symbol, copy.Offset, copy.Context));

        copy.Line = 7;
        copy.EndLine = 9;
        Assert.Equal((2, 5), (win.Line, win.EndLine));
    }

    /// <summary>a connector copied with both things it joins joins the
    /// copies, not the originals - otherwise the pasted arrow leaps back
    /// across the board to the boxes it was copied off.</summary>
    [AvaloniaFact]
    public void AnArrowCopiedWithItsEndsIsTiedToTheCopies()
    {
        var a = new BoardItem { Id = "a", Kind = "shape", X = -300, Y = -100, W = 80, H = 60 };
        var b = new BoardItem { Id = "b", Kind = "shape", X = -100, Y = -100, W = 80, H = 60 };
        var arrow = new BoardItem { Id = "r", Kind = "arrow", From = "a", To = "b", FromSide = 1, ToSide = 3 };
        var (view, board, scene, repo) = Board(a, b, arrow);
        using var _ = repo;

        CopyAll(view, scene, board);
        view.Paste(Screen(0, 100));
        var copies = Pasted(board, 3);

        var ca = copies.Single(i => i.Kind == "shape" && i.X == 0);
        var cb = copies.Single(i => i.Kind == "shape" && i.X == 200);
        var ct = copies.Single(i => i.Kind == "arrow");
        Assert.Equal((ca.Id, cb.Id, 1, 3), (ct.From, ct.To, ct.FromSide, ct.ToSide));
    }

    /// <summary>and copied alone, it cannot be tied to something that was
    /// not copied: it becomes a loose line the shape it is drawn now.</summary>
    [AvaloniaFact]
    public void AnArrowCopiedAloneKeepsItsShapeAsALooseLine()
    {
        var a = new BoardItem { Id = "a", Kind = "shape", X = -300, Y = -100, W = 80, H = 60 };
        var b = new BoardItem { Id = "b", Kind = "shape", X = -100, Y = -100, W = 80, H = 60 };
        var arrow = new BoardItem { Id = "r", Kind = "arrow", From = "a", To = "b" };
        var (view, board, scene, repo) = Board(a, b, arrow);
        using var _ = repo;

        var (fa, fb) = scene.ArrowEnds(arrow);
        scene.Picked.Clear();
        scene.Picked.Add("r");
        view.HandleKey(Key.C, KeyModifiers.Control);
        view.Paste(Screen(0, 200));

        var copy = Assert.Single(Pasted(board, 3));
        Assert.Null(copy.From);
        Assert.Null(copy.To);
        var (ca, cb) = scene.ArrowEnds(copy);
        Assert.Equal(fb.X - fa.X, cb.X - ca.X, 2);
        Assert.Equal(fb.Y - fa.Y, cb.Y - ca.Y, 2);
        Assert.Equal((0f, 200f), (Math.Min(ca.X, cb.X), Math.Min(ca.Y, cb.Y)));
    }

    /// <summary>a note pasted along with the window it sat on is about the
    /// copy of that code, so it pins to the new window.</summary>
    [AvaloniaFact]
    public void ADrawingPastedWithItsWindowPinsToTheNewWindow()
    {
        var win = new BoardItem
        {
            Id = "w", Kind = "file", File = "app/ui/Panel.cs", Line = 0, EndLine = -1,
            X = -350, Y = -250, W = 300,
        };
        // just under the header, on the first line of code
        var note = new BoardItem { Id = "n", Kind = "note", X = -340, Y = -250 + Scene.WinHeadH + 2, W = 80, H = 40, Text = "here" };
        var (view, board, scene, repo) = Board(win, note);
        using var _ = repo;
        scene.PinOver(note);
        Assert.Equal("w", note.Host);

        CopyAll(view, scene, board);
        view.Paste(Screen(50, -250));
        var copies = Pasted(board, 2);

        var cw = copies.Single(i => i.Kind == "file");
        var cn = copies.Single(i => i.Kind == "note");
        Assert.Equal(cw.Id, cn.Host);
        Assert.Equal("w", note.Host);
    }

    /// <summary>a generated board is read only: it can be copied from, which
    /// is how a window in the change view gets onto a board of your own, but
    /// not pasted onto.</summary>
    [AvaloniaFact]
    public void NothingPastesOntoAReadOnlyBoard()
    {
        var shape = new BoardItem { Id = "s", Kind = "shape", X = 0, Y = 0, W = 100, H = 60 };
        var (view, board, scene, repo) = Board(shape);
        using var _ = repo;
        CopyAll(view, scene, board);

        scene.BoardReadOnly = true;
        view.Paste(Screen(0, 0));

        Assert.Single(board.Items);
    }
}

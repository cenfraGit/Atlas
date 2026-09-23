using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Atlas.Tests;

/// <summary>a resize is remembered past reopening the board.
///
/// Letting go of a drag re-pins what was dragged to the code under it, but
/// only a move counted: a resize by a corner or a wall left the anchors as
/// they were when the box was drawn. Reopening the board then put the bottom
/// edge back where it had been - a box shrunk to fit one method grew back
/// over the next, and one stretched round a method collapsed to its first
/// line.</summary>
public class ResizeRepinTests
{
    const int W = 900, H = 700;
    const string P = "app/Loop.cs";

    static string Source()
    {
        var t = new System.Text.StringBuilder();
        t.Append("namespace Demo;\n\npublic class Loop\n{\n");
        t.Append("    public void First()\n    {\n");
        for (int i = 0; i < 6; i++) t.Append($"        A({i});\n");
        t.Append("    }\n\n");
        t.Append("    public void Second()\n    {\n");
        for (int i = 0; i < 6; i++) t.Append($"        B({i});\n");
        t.Append("    }\n}\n");
        return t.ToString();
    }

    [AvaloniaTheory]
    [InlineData(-4)]     // shrunk
    [InlineData(3)]      // stretched
    public void AResizedBoxKeepsItsSizeWhenTheBoardOpensAgain(int lines)
    {
        using var repo = SampleRepo.Build();
        repo.File(P, Source());
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        var board = store.Create("resize", "b");
        var f = scene.Data.Files[scene.IndexOfPath(P)];

        var window = new BoardItem { Id = "w", Kind = "file", File = P, Key = scene.KeyFor(P), Line = 0, EndLine = -1, X = 0, Y = 0, W = 620 };
        board.Items.Add(window);
        scene.ActiveBoard = board;
        scene.Reanchor(window);
        float step = scene.LineStepIn(window, f);
        float top = Scene.WinHeadH;

        // round First(), from its declaration (line 4) to its closing brace (line 11)
        var box = new BoardItem { Id = "box", Kind = "shape", X = 10, W = 500, Y = top + 4 * step, H = 8 * step };
        board.Items.Add(box);
        scene.PinOver(box);
        store.Save(board);

        scene.CamX = box.X + box.W / 2;
        scene.CamY = box.Y + box.H;
        scene.CamS = 1f;
        var view = new SceneView(scene);
        view.AttachBoards(store, new BoardOverlay(store));
        view.BuildLayers();
        var win = new Window { Width = W, Height = H, Content = view };
        win.Show();
        win.Measure(new Avalonia.Size(W, H));
        win.Arrange(new Avalonia.Rect(0, 0, W, H));
        view.SetEditing(true);
        scene.Picked.Add("box");

        // the middle of the bottom wall, dragged down or up a few lines
        var from = new Avalonia.Point(W / 2, H / 2);
        var to = new Avalonia.Point(W / 2, H / 2 + lines * step);
        win.MouseMove(from);
        win.MouseDown(from, MouseButton.Left);
        win.MouseMove(new Avalonia.Point(W / 2, H / 2 + lines * step / 2), RawInputModifiers.LeftMouseButton);
        win.MouseMove(to, RawInputModifiers.LeftMouseButton);
        win.MouseUp(to, MouseButton.Left);
        float resized = box.H;
        Assert.Equal((8 + lines) * step, resized, 1);

        // open the board again: nothing in the file changed, so nothing moves
        scene.AnchorBoard(board);

        Assert.Equal(resized, box.H, 1);
        scene.ActiveBoard = null;
        scene.Dispose();
    }

    /// <summary>the same for an arrow: dragging its start moves the end it
    /// is pinned by, and reopening put the arrow back where it was drawn.</summary>
    [AvaloniaFact]
    public void AnArrowWhoseStartWasDraggedStaysWhereItWasPut()
    {
        using var repo = SampleRepo.Build();
        repo.File(P, Source());
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        var board = store.Create("arrow", "b");
        var f = scene.Data.Files[scene.IndexOfPath(P)];
        var window = new BoardItem { Id = "w", Kind = "file", File = P, Key = scene.KeyFor(P), Line = 0, EndLine = -1, X = 0, Y = 0, W = 620 };
        board.Items.Add(window);
        scene.ActiveBoard = board;
        scene.Reanchor(window);
        float step = scene.LineStepIn(window, f);

        // loose, starting on line 6 of the window and running off to the right
        var arrow = new BoardItem { Id = "a", Kind = "arrow", X = 100, Y = Scene.WinHeadH + 6.5f * step, X2 = 900, Y2 = 40 };
        board.Items.Add(arrow);
        scene.PinOver(arrow);
        store.Save(board);

        scene.CamX = arrow.X;
        scene.CamY = arrow.Y;
        scene.CamS = 1f;
        var view = new SceneView(scene);
        view.AttachBoards(store, new BoardOverlay(store));
        view.BuildLayers();
        var win = new Window { Width = W, Height = H, Content = view };
        win.Show();
        win.Measure(new Avalonia.Size(W, H));
        win.Arrange(new Avalonia.Rect(0, 0, W, H));
        view.SetEditing(true);
        scene.Picked.Add("a");

        // its start, dragged four lines down
        var from = new Avalonia.Point(W / 2, H / 2);
        var to = new Avalonia.Point(W / 2, H / 2 + 4 * step);
        win.MouseDown(from, MouseButton.Left);
        win.MouseMove(new Avalonia.Point(W / 2, H / 2 + 2 * step), RawInputModifiers.LeftMouseButton);
        win.MouseMove(to, RawInputModifiers.LeftMouseButton);
        win.MouseUp(to, MouseButton.Left);
        float y = arrow.Y;
        Assert.Equal(Scene.WinHeadH + 10.5f * step, y, 1);

        scene.AnchorBoard(board);

        Assert.Equal(y, arrow.Y, 1);
        scene.ActiveBoard = null;
        scene.Dispose();
    }
}

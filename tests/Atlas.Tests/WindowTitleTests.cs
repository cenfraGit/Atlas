using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Atlas.Tests;

/// <summary>a file window can be named.
///
/// Its header said only Scene.cs:280, so on a board of several windows
/// onto the same file nothing told them apart but their line numbers. The
/// window's Text is its title, typed into by double clicking the header.</summary>
public class WindowTitleTests
{
    const int W = 900, H = 700;

    static (Window Window, Scene Scene, InlineEditor Editor, BoardItem Win, TempDir Repo) Open()
    {
        var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        var board = store.Create("titles");
        var win = new BoardItem { Id = "w", Kind = "file", File = SampleRepo.LongFile, Line = 0, EndLine = 30, X = 0, Y = 0, W = 620 };
        board.Items.Add(win);
        scene.ActiveBoard = board;
        (scene.CamX, scene.CamY, scene.CamS) = (310, 100, 1);

        var view = new SceneView(scene);
        var editor = new InlineEditor();
        view.AttachBoards(store, new BoardOverlay(store));
        view.AttachEditor(editor);
        view.BuildLayers();
        var grid = new Grid();
        grid.Children.Add(view);
        grid.Children.Add(editor);
        var window = new Window { Width = W, Height = H, Content = grid };
        window.Show();
        window.Measure(new Avalonia.Size(W, H));
        window.Arrange(new Avalonia.Rect(0, 0, W, H));
        view.SetEditing(true);
        return (window, scene, editor, win, repo);
    }

    // board point to window point, for the camera above
    static Avalonia.Point At(float x, float y) => new(W / 2 + x - 310, H / 2 + y - 100);

    static void DoubleClick(Window w, Avalonia.Point p)
    {
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
    }

    [AvaloniaFact]
    public void DoubleClickingTheHeaderEditsTheTitle()
    {
        var (window, scene, editor, _, repo) = Open();
        using var _ = repo;

        DoubleClick(window, At(200, Scene.WinHeadH / 2));

        Assert.Equal("w", scene.EditingItem);
        Assert.True(editor.Editing);
        scene.ActiveBoard = null;
    }

    /// <summary>the body is code; double clicking it names nothing.</summary>
    [AvaloniaFact]
    public void DoubleClickingTheCodeDoesNot()
    {
        var (window, scene, editor, _, repo) = Open();
        using var _ = repo;

        DoubleClick(window, At(200, 120));

        Assert.Null(scene.EditingItem);
        Assert.False(editor.Editing);
        scene.ActiveBoard = null;
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace Atlas.Tests;

/// <summary>ctrl+F on a board with two windows onto the same file.
///
/// Every match went to whichever window onto its file was made first, and
/// was clamped to that window's range - so with two windows cropped to two
/// methods, a match in the second could never be reached.</summary>
public class GrepWindowTests
{
    const int W = 800, H = 600;
    const string Path = "src/Long.cs";

    static (SceneView View, Scene Scene, BoardItem First, BoardItem Second, TempDir Repo) Board()
    {
        var repo = SampleRepo.Build();
        repo.File(Path, string.Join("\n", Enumerable.Range(0, 60).Select(n => $"// line {n}")));
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        var board = store.Create("two windows", "b");

        var first = new BoardItem { Id = "one", Kind = "file", File = Path, Line = 0, EndLine = 9, X = -700, Y = 0, W = 300 };
        var second = new BoardItem { Id = "two", Kind = "file", File = Path, Line = 20, EndLine = 29, X = 700, Y = 0, W = 300 };
        board.Items.Add(first);
        board.Items.Add(second);
        scene.ActiveBoard = board;
        scene.CamS = 1f;

        var view = new SceneView(scene);
        view.AttachBoards(store, new BoardOverlay(store));
        view.BuildLayers();
        var window = new Window { Width = W, Height = H, Content = view };
        window.Show();
        window.Measure(new Avalonia.Size(W, H));
        window.Arrange(new Avalonia.Rect(0, 0, W, H));
        return (view, scene, first, second, repo);
    }

    static Found At(Scene scene, int line) =>
        new(scene.IndexOfPath(Path), Path, line, 3, $"// line {line}");

    [AvaloniaFact]
    public void AMatchInTheSecondWindowGoesToTheSecondWindow()
    {
        var (view, scene, _, second, repo) = Board();
        using var _ = repo;

        var m = Assert.Single(view.PerWindow([At(scene, 25)]));
        Assert.Equal("two", m.Window);

        view.GoToMatch(m);
        Assert.Equal(second.X + second.W / 2, view.Aim().X, 1);
    }

    /// <summary>two windows showing the same line are two places to look,
    /// so the line is listed once for each.</summary>
    [AvaloniaFact]
    public void ALineBothWindowsShowIsListedForEach()
    {
        var (view, scene, first, second, repo) = Board();
        using var _ = repo;
        second.Line = 5;

        var found = view.PerWindow([At(scene, 7)]);

        Assert.Equal(["one", "two"], found.Select(f => f.Window));
        Assert.Equal([1, 2], found.Select(f => f.Copy));
    }

    /// <summary>a match neither window shows is still worth knowing about. It
    /// is listed once and goes to the window nearest to it.</summary>
    [AvaloniaFact]
    public void AMatchNoWindowShowsGoesToTheNearest()
    {
        var (view, scene, _, second, repo) = Board();
        using var _ = repo;

        var m = Assert.Single(view.PerWindow([At(scene, 40)]));
        Assert.Null(m.Window);

        view.GoToMatch(m);
        Assert.Equal(second.X + second.W / 2, view.Aim().X, 1);
    }

    [AvaloniaFact]
    public void OffABoardNothingChanges()
    {
        var (view, scene, _, _, repo) = Board();
        using var _ = repo;
        scene.ActiveBoard = null;

        var m = Assert.Single(view.PerWindow([At(scene, 25)]));
        Assert.Null(m.Window);
    }
}

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Atlas.Tests;

/// <summary>the map follows files that change while Atlas is open.
///
/// A scan was taken once, so a file that grew kept its old card height and
/// line count, and a new file never appeared until a restart.</summary>
public class RescanTests
{
    static (SceneView View, Scene Scene, BoardStore Store) Open(TempDir repo)
    {
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        var view = new SceneView(scene);
        view.AttachBoards(store, new BoardOverlay(store));
        view.BuildLayers();
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        view.WatchRepo();
        return (view, scene, store);
    }

    static bool Until(Func<bool> done, double seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(25);
        }
        return done();
    }

    static FileRec? Rec(Scene scene, string path) =>
        scene.IndexOfPath(path) is var i && i >= 0 ? scene.Data.Files[i] : null;

    [AvaloniaFact]
    public void AFileThatGrowsGetsItsNewLength()
    {
        using var repo = SampleRepo.Build();
        var (_, scene, _) = Open(repo);
        int before = Rec(scene, "app/Program.cs")!.N;

        File.AppendAllText(Path.Combine(repo.Path, "app", "Program.cs"), "\n// one\n// two\n// three\n");

        Assert.True(Until(() => Rec(scene, "app/Program.cs")!.N > before), "the card kept its old length");
    }

    [AvaloniaFact]
    public void ANewFileAppearsAndADeletedOneGoes()
    {
        using var repo = SampleRepo.Build();
        var (_, scene, _) = Open(repo);

        repo.File("app/Fresh.cs", "class Fresh { }\n");
        Assert.True(Until(() => Rec(scene, "app/Fresh.cs") is not null), "a new file never appeared");

        File.Delete(Path.Combine(repo.Path, "app", "ui", "Panel.cs"));
        Assert.True(Until(() => Rec(scene, "app/ui/Panel.cs") is null), "a deleted file stayed");
    }

    /// <summary>a build writing into bin/ is not a reason to rescan.</summary>
    [AvaloniaFact]
    public void BuildOutputIsNotWatched()
    {
        using var repo = SampleRepo.Build();
        var (_, scene, _) = Open(repo);
        var data = scene.Data;

        repo.File("bin/Debug/out.cs", "class Out { }\n");
        Until(() => false, 2.5);

        Assert.Same(data, scene.Data);
    }

    /// <summary>and a window on the open board follows its code when lines
    /// are added above it, as it does when the board is opened.</summary>
    [AvaloniaFact]
    public void TheOpenBoardsWindowsFollowTheirCode()
    {
        using var repo = SampleRepo.Build();
        repo.File("app/Loop.cs", "namespace Demo;\n\npublic class Loop\n{\n    public void Run()\n    {\n        Go();\n    }\n}\n");
        var (_, scene, store) = Open(repo);
        var board = store.Create("b");
        var w = new BoardItem { Id = "w", Kind = "file", File = "app/Loop.cs", Key = scene.KeyFor("app/Loop.cs"), Line = 4, EndLine = 7, W = 620 };
        board.Items.Add(w);
        scene.ActiveBoard = board;
        scene.Reanchor(w);
        store.Save(board);

        File.WriteAllText(Path.Combine(repo.Path, "app", "Loop.cs"),
            "namespace Demo;\n\n// a\n// b\n// c\npublic class Loop\n{\n    public void Run()\n    {\n        Go();\n    }\n}\n");

        Assert.True(Until(() => w.Line == 7), $"the window stayed on line {w.Line + 1}");
        scene.ActiveBoard = null;
    }

    /// <summary>only the changed files' text is dropped: dropping all of it
    /// on every save made the whole map fall back to bars for a frame.</summary>
    [Fact]
    public void ARescanKeepsTheTextOfFilesThatDidNotChange()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));
        // text is loaded by drawing a card close enough to read
        using var bmp = new SkiaSharp.SKBitmap(800, 600);
        using var canvas = new SkiaSharp.SKCanvas(bmp);
        foreach (var p in new[] { "app/Program.cs", "app/ui/Panel.cs" })
        {
            var f = scene.Data.Files[scene.IndexOfPath(p)];
            (scene.CamX, scene.CamY, scene.CamS) = (f.X + f.W / 2, f.Y + 30, 3f);
            // a deadline rather than a count: tokenising is behind one lock
            // for the whole process, and the rest of the suite is using it
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (scene.LinesOf(p) is null && DateTime.UtcNow < deadline) { scene.Draw(canvas, 800, 600); Thread.Sleep(10); }
            Assert.NotNull(scene.LinesOf(p));
        }

        scene.ShowScan(Scanner.Build(repo.Path), ["app/Program.cs"]);

        Assert.Null(scene.LinesOf("app/Program.cs"));
        Assert.NotNull(scene.LinesOf("app/ui/Panel.cs"));
    }
}

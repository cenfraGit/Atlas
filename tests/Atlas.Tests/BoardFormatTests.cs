using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace Atlas.Tests;

/// <summary>what a board looks like on disk, and when it is written.
///
/// A board made by hand, with only the fields it needed, was opened, looked
/// at and closed - and came back as a fifteen hundred line diff: opening it
/// filled in fingerprints and anchors and saved for that alone, and the save
/// wrote every field of every item and escaped "+" and "'".</summary>
public class BoardFormatTests
{
    static string Saved(Action<Board> fill)
    {
        using var repo = new TempDir("format");
        var store = BoardStore.Load(repo.Path);
        var b = store.Create("b", "b");
        fill(b);
        store.Save(b);
        return File.ReadAllText(b.Path);
    }

    [Fact]
    public void ValuesAFreshItemAlreadyHasAreLeftOut()
    {
        var json = Saved(b => b.Items.Add(new BoardItem { Id = "n", Kind = "note", Text = "hi", X = 10 }));

        Assert.Contains("\"x\": 10", json);
        Assert.Contains("\"kind\": \"note\"", json);
        foreach (var noise in new[] { "\"x2\"", "\"fromSide\"", "\"weight\"", "\"endLine\"", "\"y\"", "\"w\"" })
            Assert.DoesNotContain(noise, json);
    }

    /// <summary>a value that is left out reads back as the fresh one, so a
    /// real zero where the default is not zero must be written.</summary>
    [Fact]
    public void ARealZeroWhereTheDefaultIsNotZeroSurvives()
    {
        using var repo = new TempDir("format");
        var store = BoardStore.Load(repo.Path);
        var b = store.Create("b", "b");
        b.Items.Add(new BoardItem { Id = "w", Kind = "file", File = "a.cs", Line = 0, EndLine = 0, W = 0 });
        b.Items.Add(new BoardItem { Id = "a", Kind = "arrow", From = "w", To = "w", FromSide = 0, ToSide = 0 });
        store.Save(b);

        var back = BoardStore.Load(repo.Path).Boards.Single().Items;
        Assert.Equal((0, 0f), (back[0].EndLine, back[0].W));
        Assert.Equal(("file", 0, 0), (back[0].Kind, back[1].FromSide, back[1].ToSide));
    }

    [Fact]
    public void TextIsWrittenAsItReads()
    {
        var json = Saved(b => b.Items.Add(new BoardItem { Id = "n", Kind = "note", Text = "shift+M plays Atlas's tour" }));
        Assert.Contains("shift+M plays Atlas's tour", json);
    }

    /// <summary>opening a board that lacks fingerprints and anchors fills
    /// them in, and does not write the file for that alone.</summary>
    [AvaloniaFact]
    public void OpeningABoardThatOnlyNeedsCatchingUpWritesNothing()
    {
        using var repo = SampleRepo.Build();
        var dir = Path.Combine(repo.Path, ".atlas", "boards");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "by-hand.json");
        // as a person or an agent writes one: a window with no key, no anchor
        File.WriteAllText(path, """
            { "id": "h", "name": "by hand", "items": [
              { "id": "w", "kind": "file", "file": "app/Scene.cs", "line": 4, "endLine": 20, "x": 0.0, "y": 0.0 },
              { "id": "n", "kind": "note", "text": "a note", "x": 10.0, "y": 60.0, "w": 300.0 }
            ] }
            """);
        var before = File.ReadAllText(path);

        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        var panel = new BoardOverlay(store) { Transitions = null };
        var view = new SceneView(scene);
        view.AttachBoards(store, panel);
        view.BuildLayers();
        new Window { Width = 800, Height = 600, Content = new Grid { Children = { view, panel } } }.Show();
        panel.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        panel.HandleKey(Avalonia.Input.Key.Enter);

        Assert.Same(store.Boards.Single(), scene.ActiveBoard);
        Assert.NotNull(scene.ActiveBoard!.Items[0].Key);      // caught up in memory
        Assert.Equal(before, File.ReadAllText(path));        // and not written
        scene.ActiveBoard = null;
    }
}

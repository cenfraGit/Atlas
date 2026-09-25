using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Atlas.Tests;

/// <summary>one step back or forward in the drawing order, beside all the
/// way to the back or front.</summary>
public class RestackTests
{
    static List<BoardItem> Items(string order) => order.Select(c => new BoardItem { Id = c.ToString() }).ToList();
    static string Order(List<BoardItem> items) => string.Concat(items.Select(i => i.Id));

    [Theory]
    [InlineData("abcd", "c", -1, "acbd")]      // one step back
    [InlineData("abcd", "b", 1, "acbd")]       // one step forward
    [InlineData("abcd", "a", -1, "abcd")]      // already at the back
    [InlineData("abcd", "d", 1, "abcd")]       // already at the front
    [InlineData("abcd", "bc", -1, "bcad")]     // a group keeps its own order
    [InlineData("abcd", "bd", -1, "badc")]     // apart, each takes a step
    public void OneStep(string order, string picked, int by, string expected)
    {
        var items = Items(order);
        bool moved = Scene.Restack(items, picked.Select(c => c.ToString()).ToHashSet(), by);
        Assert.Equal(expected, Order(items));
        Assert.Equal(expected != order, moved);
    }

    [AvaloniaFact]
    public void CtrlBracketsStepAndShiftGoesAllTheWay()
    {
        using var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BoardStore.Load(repo.Path);
        var board = store.Create("z");
        board.Items.AddRange(Items("abcd"));
        scene.ActiveBoard = board;
        var view = new SceneView(scene);
        view.AttachBoards(store, new BoardOverlay(store));
        view.BuildLayers();
        new Window { Content = view }.Show();
        view.SetEditing(true);
        scene.Picked.Add("c");

        view.HandleKey(Key.OemOpenBrackets, KeyModifiers.Control);
        Assert.Equal("acbd", Order(board.Items));
        view.HandleKey(Key.OemOpenBrackets, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.Equal("cabd", Order(board.Items));
        view.HandleKey(Key.OemCloseBrackets, KeyModifiers.Control);
        Assert.Equal("acbd", Order(board.Items));

        view.HandleKey(Key.Z, KeyModifiers.Control);
        Assert.Equal("cabd", Order(board.Items));
        scene.ActiveBoard = null;
    }

    /// <summary>a small board fits at well over 1x, and could not be zoomed
    /// out past 1x; now it always can to 0.5x.</summary>
    [Fact]
    public void AnyViewZoomsOutToHalf()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));
        var board = new Board { Id = "b", Name = "b" };
        board.Items.Add(new BoardItem { Id = "n", Kind = "shape", W = 50, H = 30 });
        scene.ActiveBoard = board;
        Assert.True(scene.MinZoomFor(1600, 1000) <= 0.5f);
        scene.ActiveBoard = null;
    }
}

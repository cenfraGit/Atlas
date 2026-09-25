using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using SkiaSharp;

namespace Atlas.Tests;

/// <summary>an arrow can say what it means.
///
/// Every other item had words and an arrow could not, so "calls" or
/// "reuses the ladder" took a note parked beside the line, moved separately
/// for ever after. The label is the arrow's Text, drawn on the middle of the
/// shaft with the line stopping either side of it.</summary>
[Collection("render")]
public class ArrowLabelTests
{
    static (Scene Scene, TempDir Repo, BoardItem Arrow) Board(string? text)
    {
        var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var board = new Board { Id = "b", Name = "b" };
        board.Items.Add(new BoardItem { Id = "a", Kind = "shape", X = -300, Y = -20, W = 100, H = 40 });
        board.Items.Add(new BoardItem { Id = "c", Kind = "shape", X = 200, Y = -20, W = 100, H = 40 });
        var arrow = new BoardItem { Id = "x", Kind = "arrow", From = "a", To = "c", W = 0, Text = text, Size = 20 };
        board.Items.Add(arrow);
        scene.ActiveBoard = board;
        (scene.CamX, scene.CamY, scene.CamS) = (0, 0, 1);
        return (scene, repo, arrow);
    }

    /// <summary>the pixel at the middle of the arrow. The label's middle
    /// falls between its two letters, so the patch behind them shows.</summary>
    static SKColor Middle(Scene scene)
    {
        using var bmp = new SKBitmap(800, 400);
        using (var canvas = new SKCanvas(bmp)) scene.Draw(canvas, 800, 400);
        var (a, b) = scene.ArrowEnds(scene.ActiveBoard!.Items[2]);
        return bmp.GetPixel(400 + (int)((a.X + b.X) / 2), 200 + (int)((a.Y + b.Y) / 2));
    }

    [Fact]
    public void ALabelInterruptsTheShaft()
    {
        var (plain, r1, _) = Board(null);
        var (labelled, r2, _) = Board("to");
        using (r1) using (r2) using (plain) using (labelled)
        {
            var bare = Middle(plain);
            var under = Middle(labelled);

            // the line itself, blended at its edges; then the dark patch
            Assert.True(bare.Red > 150, $"no shaft at the middle: {bare}");
            Assert.True(under.Red < 60, $"the shaft ran through the label: {under}");
            plain.ActiveBoard = labelled.ActiveBoard = null;
        }
    }

    /// <summary>and it is where the arrow is, so it follows the boxes.</summary>
    [Fact]
    public void TheLabelFollowsTheArrow()
    {
        var (scene, repo, _) = Board("to");
        using (repo) using (scene)
        {
            scene.ActiveBoard!.Items[1].X = 600;                   // the far box moves right
            var ends = scene.ArrowEnds(scene.ActiveBoard.Items[2]);
            using var bmp = new SKBitmap(1200, 400);
            using (var canvas = new SKCanvas(bmp)) scene.Draw(canvas, 1200, 400);
            int midX = 600 + (int)((ends.A.X + ends.B.X) / 2);
            Assert.True(bmp.GetPixel(midX, 200).Red < 60);
            scene.ActiveBoard = null;
        }
    }

    /// <summary>double clicking an arrow while editing types into it, the
    /// way double clicking a note does.</summary>
    [AvaloniaFact]
    public void DoubleClickingAnArrowEditsItsLabel()
    {
        const int W = 800, H = 400;
        var (scene, repo, arrow) = Board("to");
        using var _ = repo;
        var store = BoardStore.Load(repo.Path);
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

        // on the shaft, off the label
        var on = new Avalonia.Point(W / 2 + 100, H / 2);
        window.MouseDown(on, MouseButton.Left);
        window.MouseUp(on, MouseButton.Left);
        window.MouseDown(on, MouseButton.Left);
        window.MouseUp(on, MouseButton.Left);

        Assert.Equal("x", scene.EditingItem);
        Assert.True(editor.Editing);
        scene.ActiveBoard = null;
    }
}

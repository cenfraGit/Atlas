using SkiaSharp;

namespace Atlas.Tests;

/// <summary>Scene.Draw runs on the render thread, inside Avalonia's custom
/// draw operation. Anything else that reaches into the same Scene reaches it
/// from the UI thread, and the two meet in SkiaSharp's text path.
///
/// That is not a theoretical hazard. Sizing the inline editor called
/// `ItemHeight` from the UI thread every frame; for a note that wraps the
/// text, wrapping measures it with SkiaSharp, and the render thread was
/// measuring with the same typeface at the same time. It did not throw - the
/// process went away, with no dialog, no exception and no log, on every
/// double click.
///
/// So the rule is: the UI thread reads numbers the draw loop left behind, and
/// does not measure anything itself.</summary>
[Collection("render")]
public class SceneThreadingTests
{
    const int W = 500, H = 400;

    static (Scene Scene, Board Board, TempDir Repo) Boarded()
    {
        var repo = SampleRepo.Build();
        var board = new Board { Id = "b", Name = "threads" };
        var scene = new Scene(Scanner.Build(repo.Path))
        {
            ActiveBoard = board, CamX = 0, CamY = 0, CamS = 1f,
        };
        return (scene, board, repo);
    }

    static void Frame(Scene scene)
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp)) { scene.Draw(canvas, W, H); canvas.Flush(); }
        bmp.Dispose();
    }

    /// <summary>the draw loop leaves the height of whatever is being edited
    /// where the UI thread can read it without measuring anything.</summary>
    [Fact]
    public void TheDrawLoopLeavesTheEditedItemsHeightBehind()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var note = new BoardItem
            {
                Id = "n", Kind = "note", X = -150, Y = -100, W = 300,
                Text = "a note with enough words in it to wrap onto several lines",
            };
            board.Items.Add(note);

            scene.EditingItem = "n";
            Assert.Equal(0, scene.EditingHeight);

            Frame(scene);

            Assert.True(scene.EditingHeight > 0);
            Assert.Equal(scene.ItemHeight(note), scene.EditingHeight);

            scene.EditingItem = null;
            scene.ActiveBoard = null;
        }
    }

    [Theory]
    [InlineData("shape")]
    [InlineData("ellipse")]
    [InlineData("diamond")]
    [InlineData("text")]
    public void ItWorksForEveryKindYouCanTypeInto(string kind)
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = new BoardItem
            {
                Id = "e", Kind = kind, X = -100, Y = -60, W = 200, H = 120, Text = "words",
            };
            board.Items.Add(it);
            scene.EditingItem = "e";

            Frame(scene);

            Assert.True(scene.EditingHeight > 0, $"{kind} left no height behind");

            scene.EditingItem = null;
            scene.ActiveBoard = null;
        }
    }

    /// <summary>and only for the item being edited, or the editor would take
    /// the size of whatever happened to be drawn last.</summary>
    [Fact]
    public void NothingElseWritesIt()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            board.Items.Add(new BoardItem
            {
                Id = "a", Kind = "shape", X = -200, Y = -100, W = 150, H = 90, Text = "one",
            });
            board.Items.Add(new BoardItem
            {
                Id = "b2", Kind = "shape", X = 40, Y = -100, W = 150, H = 300, Text = "two",
            });

            scene.EditingItem = "a";
            Frame(scene);

            Assert.Equal(90, scene.EditingHeight);

            scene.EditingItem = null;
            scene.ActiveBoard = null;
        }
    }

    /// <summary>the real shape of the crash: the UI thread measuring text
    /// while the render thread draws. This drives both at once and asserts
    /// only that the process is still here afterwards - which is the whole
    /// of what went wrong, and cannot be asserted any other way.</summary>
    [Fact]
    public void MeasuringWhileDrawingDoesNotTakeTheProcessDown()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            for (int i = 0; i < 12; i++)
                board.Items.Add(new BoardItem
                {
                    Id = "n" + i, Kind = "note", X = -200 + i * 30, Y = -150 + i * 20, W = 260,
                    Text = $"note {i} with enough words in it that wrapping has real work to do",
                });

            // what the UI thread is allowed to do while a frame is in flight:
            // read a number, and nothing else
            var reader = Task.Run(() =>
            {
                float seen = 0;
                for (int i = 0; i < 4000; i++) seen += scene.EditingHeight;
                return seen;
            });

            for (int i = 0; i < 12; i++)
            {
                scene.EditingItem = "n" + (i % 12);
                Frame(scene);
            }

            reader.Wait(TimeSpan.FromSeconds(20));
            Assert.True(reader.IsCompleted);

            scene.EditingItem = null;
            scene.ActiveBoard = null;
        }
    }
}

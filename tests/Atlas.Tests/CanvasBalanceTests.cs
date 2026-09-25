using SkiaSharp;

namespace Atlas.Tests;

/// <summary>drawing a board leaves the canvas as it found it.
///
/// A window's drawing saved the canvas three times and restored it twice, so
/// every window left its own position behind and everything drawn after it
/// was shifted by all the windows before. A board with several windows came
/// out scrambled - and more so zoomed in, because a window off screen is
/// skipped and so did not leave its offset, which made the layout change
/// with the zoom. Every test drew a single window with nothing after it,
/// which is the one board this cannot show on.</summary>
[Collection("render")]
public class CanvasBalanceTests
{
    static (Scene Scene, TempDir Repo, Board Board) Busy()
    {
        var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var board = new Board { Id = "b", Name = "busy" };
        board.Items.Add(new BoardItem { Id = "w1", Kind = "file", File = SampleRepo.LongFile, Line = 0, EndLine = 20, X = 0, Y = 0, W = 620, Text = "a title" });
        board.Items.Add(new BoardItem { Id = "w2", Kind = "file", File = SampleRepo.LongFile, Line = 40, EndLine = 60, X = 700, Y = 0, W = 620 });
        board.Items.Add(new BoardItem { Id = "n", Kind = "note", Text = "after the windows", X = 0, Y = 600, W = 300, H = 80 });
        board.Items.Add(new BoardItem { Id = "r", Kind = "removed", File = "gone.cs", Text = "a\nb", X = 700, Y = 600, W = 620, H = 60 });
        scene.ActiveBoard = board;
        return (scene, repo, board);
    }

    [Theory]
    [InlineData(0.3f)]      // far enough out for bars and the glow bands
    [InlineData(2.5f)]      // close enough for text
    public void ABoardLeavesTheCanvasAsItFoundIt(float zoom)
    {
        var (scene, repo, _) = Busy();
        using (repo) using (scene)
        {
            var change = new FileChange(SampleRepo.LongFile, 2, 0);
            change.AddedLines.AddRange([5, 45]);
            var set = new ChangeSet { Label = "c", Files = { change } };
            set.Index();

            foreach (var review in new[] { null, set })
            {
                scene.Review = review;
                scene.CamX = 660;
                scene.CamY = 300;
                scene.CamS = zoom;
                using var bmp = new SKBitmap(1200, 800);
                using var canvas = new SKCanvas(bmp);
                int before = canvas.SaveCount;

                scene.Draw(canvas, 1200, 800);

                Assert.Equal(before, canvas.SaveCount);
            }
            scene.ActiveBoard = null;
        }
    }

    /// <summary>and so something drawn after a window is drawn where it is.</summary>
    [Fact]
    public void ANoteAfterTwoWindowsIsWhereItWasPut()
    {
        var (scene, repo, board) = Busy();
        using (repo) using (scene)
        {
            // far enough out that both windows are drawn before the note
            var note = board.Items.Single(i => i.Id == "n");
            scene.CamX = 660;
            scene.CamY = 400;
            scene.CamS = 0.6f;
            using var bmp = new SKBitmap(1400, 800);
            using (var canvas = new SKCanvas(bmp)) scene.Draw(canvas, 1400, 800);

            // the middle of the note, on screen, is the note's panel
            int x = (int)(700 + (note.X + note.W / 2 - scene.CamX) * scene.CamS);
            int y = (int)(400 + (note.Y + note.H / 2 - scene.CamY) * scene.CamS);
            Assert.Equal(new SKColor(0x15, 0x1b, 0x12), bmp.GetPixel(x, y));
            scene.ActiveBoard = null;
        }
    }
}

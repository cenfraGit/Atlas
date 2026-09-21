using SkiaSharp;

namespace Atlas.Tests;

/// <summary>how thick a line on a board is.
///
/// A stroke has had a pen width since the brush existed. A shape's border
/// and an arrow's shaft were a hairline - stroke width zero, one screen
/// pixel however far you zoom in - which is the one width that cannot be
/// part of a drawing, because it says nothing about the thing it outlines.
/// They now share the stroke's `Weight` field, so `[` and `]` and the menu
/// mean one thing rather than three.</summary>
public class LineWidthTests
{
    [Fact]
    public void AnUnsetWidthIsTheDefault() =>
        Assert.Equal(Scene.DefaultBorder, Scene.LineWidth(new BoardItem { Kind = "shape" }));

    [Fact]
    public void AWidthThatWasChosenIsUsed() =>
        Assert.Equal(8f, Scene.LineWidth(new BoardItem { Kind = "shape", Weight = 8f }));

    /// <summary>an arrow's shaft is thinner than a shape's border by default,
    /// so it passes its own fallback rather than taking the shape's.</summary>
    [Fact]
    public void EachKindKeepsItsOwnDefault() =>
        Assert.Equal(2.5f, Scene.LineWidth(new BoardItem { Kind = "arrow" }, 2.5f));

    [Fact]
    public void ZeroMeansTheDefaultRatherThanAnInvisibleLine()
    {
        // "default" has to be a value the user can choose from the menu and
        // get back to, and 0 is the only one that cannot mean a real width
        var it = new BoardItem { Kind = "shape", Weight = 12f };
        it.Weight = 0;
        Assert.Equal(Scene.DefaultBorder, Scene.LineWidth(it));
    }

    /// <summary>a border that is drawn wider covers more of the screen. An
    /// empty shape, so the only ink in the picture is the outline itself and
    /// a fill cannot mask the difference.</summary>
    [Collection("render")]
    public class Rendered
    {
        const int W = 400, H = 400;
        static readonly SKColor Empty = new(0x16, 0x10, 0x28);

        static int Ink(BoardItem it)
        {
            using var repo = SampleRepo.Build();
            var board = new Board { Id = "b", Name = "lines" };
            board.Items.Add(it);

            using var scene = new Scene(Scanner.Build(repo.Path))
            {
                ActiveBoard = board, CamX = 0, CamY = 0, CamS = 1f,
            };

            var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bmp))
            {
                scene.Draw(canvas, W, H);
                canvas.Flush();
            }

            int n = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (bmp.GetPixel(x, y) != Empty) n++;

            bmp.Dispose();
            scene.ActiveBoard = null;
            return n;
        }

        static BoardItem Frame(float weight) => new()
        {
            Id = "s", Kind = "shape", X = -120, Y = -120, W = 240, H = 240,
            Color = "#5fd3f3", Fill = BoardItem.NoFill, Weight = weight,
        };

        [Theory]
        [InlineData("shape")]
        [InlineData("ellipse")]
        [InlineData("diamond")]
        public void AThickerBorderIsActuallyThicker(string kind)
        {
            var thin = Frame(1f);
            var fat = Frame(10f);
            thin.Kind = fat.Kind = kind;

            int a = Ink(thin), b = Ink(fat);

            // ten times the width will not be ten times the pixels - the grid
            // behind it is counted too - but it cannot be the same picture
            Assert.True(b > a * 1.5,
                $"{kind}: a 1-unit border lit {a} pixels and a 10-unit one {b}");
        }

        [Fact]
        public void AThickArrowGetsAHeadToMatch()
        {
            static BoardItem Arrow(float w) => new()
            {
                Id = "a", Kind = "arrow", Color = "#ffd166", Weight = w,
                X = -150, Y = 0, X2 = 150, Y2 = 0,
            };

            int thin = Ink(Arrow(1f)), fat = Ink(Arrow(12f));

            // the shaft alone would grow; the head has to grow with it or a
            // thick arrow ends in what looks like a tick
            Assert.True(fat > thin * 3,
                $"a 1-unit arrow lit {thin} pixels and a 12-unit one {fat}");
        }
    }
}

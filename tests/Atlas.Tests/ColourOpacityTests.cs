using SkiaSharp;

namespace Atlas.Tests;

/// <summary>how solid a colour is, stored in the colour.
///
/// A shape's outline is drawn at part opacity unless its colour says
/// otherwise, which makes one read as a frame round code rather than a box
/// in front of it - right for a frame, wrong when you want a line. "Solid"
/// and "soft" rewrite the alpha of the stored colour rather than setting a
/// flag beside it, because a flag and a colour can disagree about whether
/// something is invisible and one string cannot.</summary>
public class ColourOpacityTests
{
    static readonly SKColor Fallback = new(0x5f, 0xd3, 0xf3);

    [Theory]
    [InlineData("#ff5fd3f3")]
    [InlineData("ff5fd3f3")]
    public void EightDigitsNamesAnOpacity(string hex) => Assert.True(Scene.HasOpacity(hex));

    [Theory]
    [InlineData("#5fd3f3")]
    [InlineData("#abc")]
    [InlineData(null)]
    public void AnythingShorterDoesNot(string? hex) => Assert.False(Scene.HasOpacity(hex));

    [Fact]
    public void AColourWithNoOpacityTakesTheDefaultForWhereItIsDrawn() =>
        Assert.Equal(150, Scene.Tinted("#5fd3f3", Fallback, 150).Alpha);

    [Fact]
    public void AColourThatNamedOneIsTakenAtItsWord() =>
        Assert.Equal(255, Scene.Tinted("#ff5fd3f3", Fallback, 150).Alpha);

    [Fact]
    public void AndTheRestOfItSurvives()
    {
        var c = Scene.Tinted("#ff5fd3f3", Fallback, 150);
        Assert.Equal(0x5f, c.Red);
        Assert.Equal(0xd3, c.Green);
        Assert.Equal(0xf3, c.Blue);
    }

    [Fact]
    public void NoColourAtAllIsTheFallbackAtTheDefault()
    {
        var c = Scene.Tinted(null, Fallback, 150);
        Assert.Equal(Fallback.Red, c.Red);
        Assert.Equal(150, c.Alpha);
    }

    // --- writing one back ---------------------------------------------------

    [Fact]
    public void MakingSomethingSolidKeepsItsColour()
    {
        var solid = Scene.WithOpacity("#5fd3f3", Fallback, 255);

        Assert.Equal("#ff5fd3f3", solid);
        Assert.Equal(255, Scene.Tinted(solid, Fallback, 150).Alpha);
    }

    [Fact]
    public void MakingItSoftAgainKeepsItToo()
    {
        var soft = Scene.WithOpacity("#ff5fd3f3", Fallback, 96);

        Assert.Equal("#605fd3f3", soft);
        Assert.Equal(0x5f, Scene.ParseColor(soft, Fallback).Red);
    }

    /// <summary>an item with no colour of its own can still be made solid -
    /// the opacity is applied to whatever it is drawn in.</summary>
    [Fact]
    public void SomethingUncolouredCanStillBeMadeSolid()
    {
        var solid = Scene.WithOpacity(null, Scene.DefaultShape, 255);

        Assert.Equal(255, Scene.Tinted(solid, Fallback, 150).Alpha);
        Assert.Equal(Scene.DefaultShape.Red, Scene.ParseColor(solid, Fallback).Red);
    }

    // --- typed by hand ------------------------------------------------------

    [Theory]
    [InlineData("#5fd3f3", "#5fd3f3")]
    [InlineData("5fd3f3", "#5fd3f3")]
    [InlineData("  #5FD3F3  ", "#5fd3f3")]
    [InlineData("abc", "#abc")]
    [InlineData("#ff5fd3f3", "#ff5fd3f3")]
    public void TheseAreColours(string typed, string expected) =>
        Assert.Equal(expected, Scene.NormaliseHex(typed));

    /// <summary>and these are not. Refused rather than ignored: a typo that
    /// turns a border invisible is worse than being told.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("nope")]
    [InlineData("#12345")]
    [InlineData("#gggggg")]
    [InlineData(null)]
    public void TheseAreNot(string? typed) => Assert.Null(Scene.NormaliseHex(typed));

    // --- what it looks like -------------------------------------------------

    [Collection("render")]
    public class Rendered
    {
        const int W = 400, H = 400;
        static readonly SKColor Empty = new(0x16, 0x10, 0x28);

        static int Ink(string? colour)
        {
            using var repo = SampleRepo.Build();
            var board = new Board { Id = "b", Name = "opacity" };
            board.Items.Add(new BoardItem
            {
                Id = "s", Kind = "shape", X = -140, Y = -100, W = 280, H = 200,
                Color = colour, Fill = BoardItem.NoFill, Weight = 6,
            });

            using var scene = new Scene(Scanner.Build(repo.Path))
            {
                ActiveBoard = board, CamX = 0, CamY = 0, CamS = 1f,
            };

            var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bmp)) { scene.Draw(canvas, W, H); canvas.Flush(); }

            // pixels of the border that are close to the full colour. A soft
            // border blends into what is behind it and reaches none of them
            int n = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    if (c == Empty) continue;
                    if (Math.Abs(c.Red - 0x5f) < 18 && Math.Abs(c.Green - 0xd3) < 18 &&
                        Math.Abs(c.Blue - 0xf3) < 18) n++;
                }

            bmp.Dispose();
            scene.ActiveBoard = null;
            return n;
        }

        [Fact]
        public void ASolidBorderIsDrawnInItsActualColour()
        {
            int soft = Ink("#5fd3f3");
            int solid = Ink("#ff5fd3f3");

            Assert.True(solid > 300, $"a solid border only reached its colour on {solid} pixels");
            Assert.True(solid > soft * 4,
                $"soft drew {soft} pixels of full colour and solid {solid}: they look the same");
        }
    }
}

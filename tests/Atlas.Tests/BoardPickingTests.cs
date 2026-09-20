using SkiaSharp;

namespace Atlas.Tests;

/// <summary>the interaction rules that were only ever checked by eye: hit
/// order, what a rubberband owns, which items resize, and where a click on an
/// arrow goes.</summary>
public class BoardPickingTests : IDisposable
{
    const float Grid = 40f;

    readonly TempDir _repo = SampleRepo.Build();
    readonly Scene _scene;
    readonly Board _board = new() { Id = "b", Name = "test" };

    public BoardPickingTests()
    {
        _scene = new Scene(Scanner.Build(_repo.Path)) { ActiveBoard = _board, CamS = 1f };
    }

    public void Dispose()
    {
        _scene.ActiveBoard = null;
        _scene.Dispose();
        _repo.Dispose();
    }

    static BoardItem Note(string id, float x, float y, float w = 200, float h = 80) =>
        new() { Id = id, Kind = "note", Text = "x", X = x, Y = y, W = w, H = h };

    static BoardItem Shape(string id, float x, float y, float w = 200, float h = 80) =>
        new() { Id = id, Kind = "shape", X = x, Y = y, W = w, H = h };

    /// <summary>the rule a rubberband must follow: start from what was held,
    /// then add exactly what the band covers now.</summary>
    HashSet<string> Sweep(IEnumerable<string> held, SKRect band)
    {
        var picked = new HashSet<string>(held);
        foreach (var it in _scene.ItemsIn(band)) picked.Add(it.Id);
        return picked;
    }

    // --- hit testing -----------------------------------------------------

    [Fact]
    public void TheTopmostItemWinsAClick()
    {
        _board.Items.Add(Note("a", 0, 0));
        _board.Items.Add(Shape("over", 0, 0));

        Assert.Equal("over", _scene.ItemAt(10, 10)?.Id);
    }

    [Fact]
    public void SendingAnItemToTheBackGivesTheClickBack()
    {
        _board.Items.Add(Note("a", 0, 0));
        var over = Shape("over", 0, 0);
        _board.Items.Add(over);

        _board.Items.Remove(over);
        _board.Items.Insert(0, over);

        Assert.Equal("a", _scene.ItemAt(10, 10)?.Id);
    }

    [Fact]
    public void AClickJustOutsideAnEdgeStillLands()
    {
        _board.Items.Add(Note("a", 0, 0));
        Assert.Equal("a", _scene.ItemAt(-2, -2)?.Id);
    }

    [Fact]
    public void AClickWellAwayHitsNothing()
    {
        _board.Items.Add(Note("a", 0, 0));
        Assert.Null(_scene.ItemAt(-400, -400));
    }

    [Fact]
    public void AnArrowDoesNotSwallowAClickMeantForTheCanvas()
    {
        _board.Items.Add(new BoardItem { Id = "ar", Kind = "arrow", X = 0, Y = 0, X2 = 400, Y2 = 400 });
        Assert.Null(_scene.ItemAt(200, 200));
    }

    [Fact]
    public void AnArrowIsPickedOnlyNearItsLine()
    {
        _board.Items.Add(new BoardItem { Id = "ar", Kind = "arrow", X = 0, Y = 0, X2 = 400, Y2 = 400 });

        Assert.Equal("ar", _scene.ArrowAt(200, 200)?.Id);
        Assert.Null(_scene.ArrowAt(200, 320));
    }

    // --- the rubberband --------------------------------------------------

    [Fact]
    public void AWideBandTakesEverythingUnderIt()
    {
        _board.Items.Add(Note("a", 0, 0));
        _board.Items.Add(Note("b", 400, 0));

        var picked = Sweep([], new SKRect(-10, -10, 700, 300));

        Assert.Equal(["a", "b"], picked.Order());
    }

    [Fact]
    public void ShrinkingTheBandLetsGoOfWhatItNoLongerCovers()
    {
        _board.Items.Add(Note("a", 0, 0));
        _board.Items.Add(Note("b", 400, 0));

        var picked = Sweep([], new SKRect(-10, -10, 250, 300));

        Assert.Contains("a", picked);
        Assert.DoesNotContain("b", picked);
    }

    [Fact]
    public void CtrlKeepsWhatWasAlreadyPicked()
    {
        _board.Items.Add(Note("a", 0, 0));
        _board.Items.Add(Note("b", 400, 0));

        var picked = Sweep(["b"], new SKRect(-10, -10, 250, 300));

        Assert.Equal(["a", "b"], picked.Order());
    }

    [Fact]
    public void AnEmptyBandPicksNothing()
    {
        _board.Items.Add(Note("a", 0, 0));
        Assert.Empty(_scene.ItemsIn(new SKRect(900, 900, 950, 950)));
    }

    // --- snapping --------------------------------------------------------

    [Fact]
    public void TheTruePositionAccumulatesWhileOnlyTheShownOneRounds()
    {
        // rounding the live position is what made items feel stuck
        float raw = 0, shown = 0;
        for (int i = 0; i < 8; i++)
        {
            raw += 7f;                                   // every move smaller than a cell
            shown = MathF.Round(raw / Grid) * Grid;
        }

        Assert.True(raw > 50, $"the true position should accumulate, got {raw}");
        Assert.Equal(40f, shown);
    }

    [Fact]
    public void ATinyDragDoesNotJumpAWholeCell() =>
        Assert.Equal(0f, MathF.Round(3f / Grid) * Grid);

    // --- resizing --------------------------------------------------------

    [Theory]
    [InlineData("note", true)]
    [InlineData("shape", true)]
    [InlineData("image", true)]
    [InlineData("file", false)]     // a file window's height follows its range
    public void OnlyDrawingElementsResize(string kind, bool resizable) =>
        Assert.Equal(resizable, Scene.Resizable(new BoardItem { Id = "x", Kind = kind, File = "a.png" }));

    // --- clipboard bitmaps -----------------------------------------------

    [Fact]
    public void AClipboardScreenshotArrivesAsAHeaderlessBitmap()
    {
        // windows hands over a DIB: the file header has to be put back before
        // anything can decode it
        var dib = new byte[40 + 16];
        BitConverter.GetBytes(40).CopyTo(dib, 0);          // header size
        BitConverter.GetBytes(2).CopyTo(dib, 4);           // width
        BitConverter.GetBytes(2).CopyTo(dib, 8);           // height
        BitConverter.GetBytes((short)1).CopyTo(dib, 12);   // planes
        BitConverter.GetBytes((short)32).CopyTo(dib, 14);  // bits per pixel
        BitConverter.GetBytes(16).CopyTo(dib, 20);         // image size

        using var decoded = ImageStore.FromDib(dib);

        Assert.NotNull(decoded);
        Assert.Equal(2, decoded!.Width);
        Assert.Equal(2, decoded.Height);
    }

    [Fact]
    public void RubbishOnTheClipboardIsRefused() => Assert.Null(ImageStore.FromDib([1, 2, 3]));
}

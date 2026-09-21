using SkiaSharp;

namespace Atlas.Tests;

/// <summary>pasted images are re-encoded on the way in and pruned on the way
/// out. Pruning after undo is the one thing uitest.ps1 could see that the old
/// self-checks could not; it is checkable here without a window, because the
/// evidence is a file on disk either way.
///
/// ImageStore keeps a process-wide cache, so these share a collection rather
/// than running alongside each other.</summary>
[Collection("images")]
public class ImageStoreTests
{
    static SKBitmap Bitmap(int w, int h)
    {
        var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(20, 30, 60));
        using var paint = new SKPaint { Color = SKColors.Orange };
        canvas.DrawRect(new SKRect(4, 4, w / 2f, h / 2f), paint);
        return bmp;
    }

    static Board BoardWith(params string[] imageNames)
    {
        var b = new Board { Id = "b1", Name = "board" };
        foreach (var n in imageNames)
            b.Items.Add(new BoardItem { Id = "i" + n, Kind = "image", File = n });
        return b;
    }

    [Fact]
    public void ImagesLiveUnderDotAtlasInTheScannedRepo()
    {
        using var dir = new TempDir();
        Assert.Equal(Path.Combine(dir.Path, ".atlas", "images"), ImageStore.DirFor(dir.Path));
    }

    [Fact]
    public void SavingWritesOneFileAndReturnsItsName()
    {
        using var dir = new TempDir("atlas_img");
        using var bmp = Bitmap(64, 48);

        var name = ImageStore.Save(dir.Path, bmp);

        Assert.NotNull(name);
        Assert.True(File.Exists(Path.Combine(ImageStore.DirFor(dir.Path), name!)));
        Assert.Single(Directory.GetFiles(ImageStore.DirFor(dir.Path)));
    }

    [Fact]
    public void ASavedImageLoadsBackAsAnImage()
    {
        using var dir = new TempDir("atlas_img");
        using var bmp = Bitmap(64, 48);
        var name = ImageStore.Save(dir.Path, bmp)!;

        var loaded = ImageStore.Load(dir.Path, name);

        Assert.NotNull(loaded);
        Assert.Equal(64, loaded!.Width);
        Assert.Equal(48, loaded.Height);
    }

    [Fact]
    public void ALargeImageIsScaledDownOnTheWayIn()
    {
        using var dir = new TempDir("atlas_img");
        using var bmp = Bitmap(3000, 1500);
        var name = ImageStore.Save(dir.Path, bmp)!;

        var loaded = ImageStore.Load(dir.Path, name)!;

        // a raw clipboard screenshot would sit in git forever at full size
        Assert.True(loaded.Width <= 2000, $"still {loaded.Width} wide");
        Assert.Equal(1500 * loaded.Width / 3000, loaded.Height);
    }

    [Fact]
    public void LoadingAMissingImageIsNullRatherThanAThrow()
    {
        using var dir = new TempDir("atlas_img");
        Assert.Null(ImageStore.Load(dir.Path, "nothing-here.png"));
    }

    [Fact]
    public void ImportReadsAFileFromDisk()
    {
        using var dir = new TempDir("atlas_img");
        using var bmp = Bitmap(80, 60);
        var source = Path.Combine(dir.Path, "source.png");
        using (var data = SKImage.FromBitmap(bmp).Encode(SKEncodedImageFormat.Png, 90))
        using (var fs = File.Create(source))
            data.SaveTo(fs);

        var name = ImageStore.Import(dir.Path, source);

        Assert.NotNull(name);
        Assert.True(File.Exists(Path.Combine(ImageStore.DirFor(dir.Path), name!)));
    }

    [Fact]
    public void ImportingSomethingThatIsNotAnImageIsNullRatherThanAThrow()
    {
        using var dir = new TempDir("atlas_img");
        var path = dir.File("notes.txt", "definitely not a png");

        Assert.Null(ImageStore.Import(dir.Path, path));
    }

    [Fact]
    public void PruningKeepsWhatABoardStillPointsAt()
    {
        using var dir = new TempDir("atlas_img");
        using var bmp = Bitmap(32, 32);
        var kept = ImageStore.Save(dir.Path, bmp)!;

        int gone = ImageStore.Prune(dir.Path, [BoardWith(kept)]);

        Assert.Equal(0, gone);
        Assert.True(File.Exists(Path.Combine(ImageStore.DirFor(dir.Path), kept)));
    }

    [Fact]
    public void PruningRemovesWhatUndoThrewAway()
    {
        using var dir = new TempDir("atlas_img");
        using var bmp = Bitmap(32, 32);
        var kept = ImageStore.Save(dir.Path, bmp)!;
        var undone = ImageStore.Save(dir.Path, bmp)!;

        // this is the uitest.ps1 case: ctrl+V wrote a file, ctrl+Z dropped the
        // item, and leaving the board must sweep the orphaned bytes up
        int gone = ImageStore.Prune(dir.Path, [BoardWith(kept)]);

        Assert.Equal(1, gone);
        Assert.True(File.Exists(Path.Combine(ImageStore.DirFor(dir.Path), kept)));
        Assert.False(File.Exists(Path.Combine(ImageStore.DirFor(dir.Path), undone)));
    }

    /// <summary>the app looks at an image the moment it pastes one, to get
    /// its shape. Pruning has to work afterwards.</summary>
    [Fact]
    public void PruningRemovesAnImageThatHasBeenLookedAt()
    {
        using var dir = new TempDir("atlas_img");
        using var bmp = Bitmap(32, 32);
        var orphan = ImageStore.Save(dir.Path, bmp)!;

        Assert.NotNull(ImageStore.Load(dir.Path, orphan));

        Assert.Equal(1, ImageStore.Prune(dir.Path, []));
        Assert.False(File.Exists(Path.Combine(ImageStore.DirFor(dir.Path), orphan)));
    }

    [Fact]
    public void PruningLooksAcrossEveryBoard()
    {
        using var dir = new TempDir("atlas_img");
        using var bmp = Bitmap(32, 32);
        var onA = ImageStore.Save(dir.Path, bmp)!;
        var onB = ImageStore.Save(dir.Path, bmp)!;
        var orphan = ImageStore.Save(dir.Path, bmp)!;

        int gone = ImageStore.Prune(dir.Path, [BoardWith(onA), BoardWith(onB)]);

        Assert.Equal(1, gone);
        Assert.Equal(2, Directory.GetFiles(ImageStore.DirFor(dir.Path)).Length);
        Assert.False(File.Exists(Path.Combine(ImageStore.DirFor(dir.Path), orphan)));
    }

    [Fact]
    public void PruningARepoWithNoImagesIsSafe()
    {
        using var dir = new TempDir("atlas_img");
        Assert.Equal(0, ImageStore.Prune(dir.Path, [BoardWith()]));
    }

    [Fact]
    public void PruningWithNoBoardsLeftClearsEverything()
    {
        using var dir = new TempDir("atlas_img");
        using var bmp = Bitmap(32, 32);
        ImageStore.Save(dir.Path, bmp);
        ImageStore.Save(dir.Path, bmp);

        Assert.Equal(2, ImageStore.Prune(dir.Path, []));
        Assert.Empty(Directory.GetFiles(ImageStore.DirFor(dir.Path)));
    }

    [Fact]
    public void EachSaveGetsItsOwnName()
    {
        using var dir = new TempDir("atlas_img");
        using var bmp = Bitmap(32, 32);

        var names = Enumerable.Range(0, 5).Select(_ => ImageStore.Save(dir.Path, bmp)).ToList();

        Assert.Equal(5, names.Distinct().Count());
        Assert.Equal(5, Directory.GetFiles(ImageStore.DirFor(dir.Path)).Length);
    }
}

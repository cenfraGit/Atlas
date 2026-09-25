namespace Atlas.Tests;

/// <summary>the parts of the canvas that hold real logic rather than pixels:
/// the level-of-detail thresholds, finding a file again after it moved, and
/// what a board window actually shows.</summary>
public class SceneTests
{
    [Theory]
    [InlineData(0.01f, 0)]    // folders
    [InlineData(0.054f, 0)]
    [InlineData(0.055f, 1)]   // cards
    [InlineData(0.44f, 1)]
    [InlineData(0.45f, 2)]    // bars
    [InlineData(0.99f, 2)]
    [InlineData(1.0f, 3)]     // text
    [InlineData(12f, 3)]
    public void TheLodThresholdsAreWhereTheyAreDocumented(float zoom, int tier) =>
        Assert.Equal(tier, Scene.TierFor(zoom));

    [Fact]
    public void TheTierNeverSkipsAStepAsYouZoomIn()
    {
        int last = 0;
        for (float s = 0.005f; s < 20f; s *= 1.02f)
        {
            int tier = Scene.TierFor(s);
            Assert.InRange(tier, last, last + 1);
            last = tier;
        }
        Assert.Equal(3, last);
    }

    [Fact]
    public void EveryScannedFileIsIndexedByItsPath()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        foreach (var (f, i) in scene.Data.Files.Select((f, i) => (f, i)))
            Assert.Equal(i, scene.IndexOfPath(f.P));
    }

    [Fact]
    public void AnUnknownPathIsMinusOneRatherThanAThrow()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        Assert.Equal(-1, scene.IndexOfPath("nope/missing.cs"));
        Assert.Equal(-1, scene.IndexOfPath(""));
    }

    [Fact]
    public void AFileThatMovedIsFoundByItsName()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        // the reference says app/ui/Panel.cs; only one Panel.cs exists, so the
        // name alone is enough even though the directory in the path is wrong
        int found = scene.ResolveFile("old/place/Panel.cs", key: null);

        Assert.Equal(scene.IndexOfPath("app/ui/Panel.cs"), found);
    }

    [Fact]
    public void AFileThatWasRenamedIsFoundByItsContent()
    {
        using var repo = SampleRepo.Build();
        var contents = File.ReadAllLines(Path.Combine(repo.Path, "app", "ui", "Panel.cs"));
        var key = FileKeys.Of(contents);

        // rename it on disk, then rescan: neither the path nor the name matches
        File.Move(Path.Combine(repo.Path, "app", "ui", "Panel.cs"),
                  Path.Combine(repo.Path, "app", "ui", "Sidebar.cs"));
        using var scene = new Scene(Scanner.Build(repo.Path));

        int found = scene.ResolveFile("app/ui/Panel.cs", key);

        Assert.Equal(scene.IndexOfPath("app/ui/Sidebar.cs"), found);
    }

    [Fact]
    public void AFileThatIsGenuinelyGoneResolvesToNothing()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        Assert.Equal(-1, scene.ResolveFile("app/Deleted.cs", "ABCDEF123456"));
    }

    [Fact]
    public void ThePathIsTriedBeforeTheFingerprint()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        // a stale fingerprint must not drag the reference off a path that is
        // still perfectly valid
        Assert.Equal(scene.IndexOfPath("app/Program.cs"),
                     scene.ResolveFile("app/Program.cs", "STALEKEY0000"));
    }

    [Fact]
    public void ClickingLandsOnTheCardUnderThePoint()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));
        int i = scene.IndexOfPath(SampleRepo.LongFile);
        var f = scene.Data.Files[i];

        Assert.Equal(i, scene.FileAt(f.X + f.W / 2, f.Y + f.H / 2));
    }

    [Fact]
    public void ClickingEmptySpaceLandsOnNothing()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        Assert.Equal(-1, scene.FileAt(-5000, -5000));
    }

    // --- board windows ---------------------------------------------------

    static BoardItem Window(string path, int from, int to) =>
        new() { Id = "i1", Kind = "file", File = path, Line = from, EndLine = to, W = 620 };

    [Fact]
    public void AWindowsHeightFollowsItsLineRange()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        float twenty = scene.ItemHeight(Window(SampleRepo.LongFile, 5, 24));
        float hundred = scene.ItemHeight(Window(SampleRepo.LongFile, 5, 104));

        Assert.True(hundred > twenty * 3, $"100 lines ({hundred}) should dwarf 20 ({twenty})");
    }

    [Fact]
    public void ARangePastTheEndOfTheFileClamps()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));
        var file = scene.Data.Files[scene.IndexOfPath(SampleRepo.LongFile)];

        var (from, to) = scene.RangeOf(Window(SampleRepo.LongFile, 0, 999_999), file);

        Assert.Equal(file.N - 1, to);
        Assert.True(from <= to, "the clamped range stays ordered");
    }

    [Fact]
    public void AWindowOnAMissingFileStillHasAHeightToDraw()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        // it draws as "gone", which still needs a box
        Assert.True(scene.ItemHeight(Window("gone/away.cs", 0, 10)) > 0);
    }

    [Fact]
    public void ANoteKeepsItsOwnHeight()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));
        var note = new BoardItem { Id = "n", Kind = "note", Text = "a note", W = 380, H = 120 };

        Assert.Equal(120, scene.ItemHeight(note));
    }
}

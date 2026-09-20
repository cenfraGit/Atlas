namespace Atlas.Tests;

/// <summary>a bookmark anchors to a file and a line, not to camera
/// coordinates, so it still lands correctly after the map is laid out afresh.
/// That is what these check, along with the framing rules.</summary>
public class BookmarkTests
{
    const float Vw = 1400, Vh = 900;

    static Scene SceneOf(TempDir repo) => new(Scanner.Build(repo.Path));

    /// <summary>point the camera. Tier is normally written by the draw loop
    /// once a frame, so a headless test has to set it the same way.</summary>
    static void Look(Scene scene, float x, float y, float s)
    {
        scene.CamX = x;
        scene.CamY = y;
        scene.CamS = s;
        scene.Tier = Scene.TierFor(s);
    }

    [Fact]
    public void BookmarksLiveInTheScannedRepo()
    {
        using var dir = new TempDir();
        Assert.Equal(Path.Combine(dir.Path, ".atlas", "bookmarks.json"), BookmarkStore.PathFor(dir.Path));
    }

    [Fact]
    public void ARepoWithNoBookmarksLoadsEmpty()
    {
        using var dir = new TempDir();
        var store = BookmarkStore.Load(dir.Path);
        Assert.Empty(store.Bookmarks);
        Assert.Empty(store.Tours);
    }

    [Fact]
    public void BookmarksAndToursRoundTrip()
    {
        using var dir = new TempDir();
        var store = BookmarkStore.Load(dir.Path);
        var b = new Bookmark
        {
            Id = "abc123", Name = "where a frame is drawn",
            File = "app/Scene.cs", Line = 446, EndLine = 470,
            X = 12824, Y = 13995.2f, S = 4.08f,
        };
        store.Bookmarks.Add(b);
        store.Tours.Add(new Tour { Id = "t1", Name = "The draw loop", Stops = ["abc123"] });
        store.Save();

        var reread = BookmarkStore.Load(dir.Path);
        var only = Assert.Single(reread.Bookmarks);

        Assert.Equal("where a frame is drawn", only.Name);
        Assert.Equal("app/Scene.cs", only.File);
        Assert.Equal(446, only.Line);
        Assert.Equal(470, only.EndLine);
        Assert.Equal(4.08f, only.S, 3);
        Assert.Equal(["abc123"], Assert.Single(reread.Tours).Stops);
    }

    [Fact]
    public void ABookmarkIsFoundByIdAndByTheTourUsingIt()
    {
        using var dir = new TempDir();
        var store = BookmarkStore.Load(dir.Path);
        store.Bookmarks.Add(new Bookmark { Id = "x1", Name = "one" });
        store.Tours.Add(new Tour { Id = "t1", Name = "tour", Stops = ["x1"] });

        Assert.Equal("one", store.ById("x1")!.Name);
        Assert.Null(store.ById("nope"));
        Assert.Equal("t1", store.TourContaining("x1")!.Id);
        Assert.Null(store.TourContaining("nope"));
    }

    [Fact]
    public void IdsAreUnique()
    {
        var ids = Enumerable.Range(0, 50).Select(_ => BookmarkStore.NewId()).ToList();
        Assert.Equal(50, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Equal(8, id.Length));
    }

    [Fact]
    public void ACorruptFileLoadsEmptyRatherThanThrowing()
    {
        using var dir = new TempDir();
        var path = BookmarkStore.PathFor(dir.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json at all");

        Assert.Empty(BookmarkStore.Load(dir.Path).Bookmarks);
    }

    // --- resolving against a layout -------------------------------------

    [Fact]
    public void AFreeBookmarkKeepsItsCamera()
    {
        using var repo = SampleRepo.Build();
        var scene = SceneOf(repo);
        var b = new Bookmark { Id = "b", Name = "the whole map", X = 500, Y = 600, S = 0.04f };

        var t = BookmarkTargets.Resolve(scene, b, Vw, Vh);

        Assert.Equal(500, t.X);
        Assert.Equal(600, t.Y);
        Assert.Equal(0.04f, t.S);
        Assert.False(t.Orphaned);
    }

    [Fact]
    public void AnAnchoredBookmarkLandsOnItsFileWhereverTheLayoutPutIt()
    {
        using var repo = SampleRepo.Build();
        var scene = SceneOf(repo);
        var file = scene.Data.Files[scene.IndexOfPath(SampleRepo.LongFile)];

        var b = new Bookmark { Id = "b", Name = "a method", File = SampleRepo.LongFile, Line = 100, EndLine = 119 };
        var t = BookmarkTargets.Resolve(scene, b, Vw, Vh);

        Assert.False(t.Orphaned);
        Assert.InRange(t.Y, file.Y, file.Y + file.H);
    }

    [Fact]
    public void TheAnchorWinsOverTheStoredCamera()
    {
        using var repo = SampleRepo.Build();
        var scene = SceneOf(repo);

        // a stale camera from before the layout changed: it must be ignored
        var b = new Bookmark
        {
            Id = "b", Name = "a method", File = SampleRepo.LongFile,
            Line = 100, EndLine = 119, X = 999999, Y = 999999, S = 0.01f,
        };
        var t = BookmarkTargets.Resolve(scene, b, Vw, Vh);

        Assert.NotEqual(999999, t.X);
        Assert.NotEqual(0.01f, t.S);
    }

    [Fact]
    public void AMissingFileIsReportedRatherThanFlownTo()
    {
        using var repo = SampleRepo.Build();
        var scene = SceneOf(repo);
        var b = new Bookmark { Id = "b", Name = "gone", File = "app/Deleted.cs", Line = 3, X = 42, Y = 43, S = 2 };

        var t = BookmarkTargets.Resolve(scene, b, Vw, Vh);

        Assert.True(t.Orphaned);
        Assert.Equal(42, t.X);   // falls back to the stored camera
        Assert.Equal(2, t.S);
    }

    [Fact]
    public void ASmallRegionIsFramedCloserThanALargeOne()
    {
        using var repo = SampleRepo.Build();
        var scene = SceneOf(repo);

        var tight = BookmarkTargets.Resolve(scene,
            new Bookmark { File = SampleRepo.LongFile, Line = 100, EndLine = 109 }, Vw, Vh);
        var wide = BookmarkTargets.Resolve(scene,
            new Bookmark { File = SampleRepo.LongFile, Line = 100, EndLine = 259 }, Vw, Vh);

        Assert.True(tight.S > wide.S, $"a 10 line region ({tight.S}) should be closer than a 160 line one ({wide.S})");
    }

    [Fact]
    public void FramingIsCappedByReadableLineWidth()
    {
        using var repo = SampleRepo.Build();
        var scene = SceneOf(repo);
        var file = scene.Data.Files[scene.IndexOfPath(SampleRepo.LongFile)];

        // a single line would zoom absurdly close if nothing capped it
        var t = BookmarkTargets.Resolve(scene,
            new Bookmark { File = SampleRepo.LongFile, Line = 50, EndLine = 50 }, Vw, Vh);

        Assert.True(t.S <= Vw * 0.9f / (file.W * 0.6f) + 0.001f, $"zoom {t.S} runs code off the side");
        Assert.InRange(t.S, 0.02f, 12f);
    }

    [Fact]
    public void ARegionIsCentredOnItsMiddleLine()
    {
        using var repo = SampleRepo.Build();
        var scene = SceneOf(repo);
        var file = scene.Data.Files[scene.IndexOfPath(SampleRepo.LongFile)];

        var t = BookmarkTargets.Resolve(scene,
            new Bookmark { File = SampleRepo.LongFile, Line = 100, EndLine = 119 }, Vw, Vh);

        float middle = file.Y + scene.Data.HeaderH + 110 * scene.Data.LineH;
        Assert.Equal(middle, t.Y, 1);
    }

    [Fact]
    public void AWholeFileBookmarkKeepsTheCardOnScreen()
    {
        using var repo = SampleRepo.Build();
        var scene = SceneOf(repo);
        var file = scene.Data.Files[scene.IndexOfPath(SampleRepo.LongFile)];

        // line -1 means the whole file; the camera must stay within the card
        var t = BookmarkTargets.Resolve(scene,
            new Bookmark { File = SampleRepo.LongFile, Line = -1, EndLine = -1 }, Vw, Vh);

        Assert.InRange(t.Y, file.Y, file.Y + file.H);
    }

    [Fact]
    public void AnAnchorNearTheEndOfAFileDoesNotFlyPastIt()
    {
        using var repo = SampleRepo.Build();
        var scene = SceneOf(repo);
        var file = scene.Data.Files[scene.IndexOfPath(SampleRepo.LongFile)];

        var t = BookmarkTargets.Resolve(scene,
            new Bookmark { File = SampleRepo.LongFile, Line = SampleRepo.LongFileLines - 1, EndLine = -1 }, Vw, Vh);

        Assert.InRange(t.Y, file.Y, file.Y + file.H);
    }

    [Fact]
    public void CapturingAtMapZoomSavesAViewRatherThanAFile()
    {
        using var repo = SampleRepo.Build();
        var scene = SceneOf(repo);
        Look(scene, 100, 200, 0.03f);

        var b = BookmarkTargets.Capture(scene, "the whole map", Vw, Vh);

        Assert.Null(b.File);
        Assert.Equal(100, b.X);
        Assert.Equal(0.03f, b.S);
    }

    [Fact]
    public void CapturingAtReadingZoomAnchorsToTheFileUnderTheCentre()
    {
        using var repo = SampleRepo.Build();
        var scene = SceneOf(repo);
        var file = scene.Data.Files[scene.IndexOfPath(SampleRepo.LongFile)];

        Look(scene, file.X + file.W / 2, file.Y + scene.Data.HeaderH + 100 * scene.Data.LineH, 4f);

        var b = BookmarkTargets.Capture(scene, "a method", Vw, Vh);

        Assert.Equal(SampleRepo.LongFile, b.File);
        Assert.True(b.EndLine >= b.Line, "the captured region is ordered");
        Assert.InRange(b.Line, 0, SampleRepo.LongFileLines - 1);
        Assert.InRange(b.EndLine, 0, SampleRepo.LongFileLines - 1);
        Assert.InRange(100, b.Line, b.EndLine);
    }

    [Fact]
    public void CaptureThenResolveReturnsToWhereYouWere()
    {
        using var repo = SampleRepo.Build();
        var scene = SceneOf(repo);
        var file = scene.Data.Files[scene.IndexOfPath(SampleRepo.LongFile)];

        Look(scene, file.X + file.W / 2, file.Y + scene.Data.HeaderH + 150 * scene.Data.LineH, 4f);

        var b = BookmarkTargets.Capture(scene, "a method", Vw, Vh);
        var t = BookmarkTargets.Resolve(scene, b, Vw, Vh);

        Assert.False(t.Orphaned);
        // the round trip frames the same code, within half a screen of lines
        Assert.Equal(scene.CamY, t.Y, Vh / 2 / scene.CamS);
    }
}

namespace Atlas.Tests;

/// <summary>the sample boards, annotations and bookmarks that `--samples`
/// writes into a repo.
///
/// The rule worth defending is that generating them twice produces the same
/// files. It did not, twice over: a board took its file name from the random
/// id `Create` had handed out rather than from the `sample-N` it was given
/// straight afterwards, so every regeneration renamed all three boards and
/// git saw a pile of deletions beside a pile of additions - which is how
/// sample boards got deleted from the repo once already.</summary>
[Collection("render")]
public class SamplesTests
{
    static void Generate(string repo) => Samples.Run(["--samples", repo]);

    static Dictionary<string, string> AtlasFiles(string repo)
    {
        var dir = Path.Combine(repo, ".atlas");
        return Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories)
            .ToDictionary(
                p => Path.GetRelativePath(dir, p).Replace('\\', '/'),
                File.ReadAllText,
                StringComparer.Ordinal);
    }

    [Fact]
    public void GeneratingTwiceLeavesTheSameFiles()
    {
        using var repo = SampleRepo.Build("atlas_samples");

        Generate(repo.Path);
        var first = AtlasFiles(repo.Path);
        Generate(repo.Path);
        var second = AtlasFiles(repo.Path);

        Assert.Equal(first.Keys.OrderBy(k => k), second.Keys.OrderBy(k => k));
        foreach (var (name, text) in first)
            Assert.Equal(text, second[name]);
    }

    [Fact]
    public void ABoardIsNamedAfterItsIdRatherThanAFreshOne()
    {
        using var repo = SampleRepo.Build("atlas_samples");
        Generate(repo.Path);

        var names = Directory.GetFiles(BoardStore.DirFor(repo.Path), "*.json")
            .Select(Path.GetFileNameWithoutExtension).ToList();

        // the id is the suffix, so the name says which board it is and says
        // the same thing next time
        Assert.Contains("how-a-frame-is-drawn-sample-1", names);
        Assert.Contains("how-a-note-stays-attached-sample-2", names);
        Assert.Contains("everything-at-once-sample-3", names);
    }

    [Fact]
    public void ThereAreBookmarksAndATourThroughThem()
    {
        using var repo = SampleRepo.Build("atlas_samples");
        Generate(repo.Path);

        var store = BookmarkStore.Load(repo.Path);
        var tour = Assert.Single(store.Tours);

        Assert.NotEmpty(store.Bookmarks);
        Assert.Equal(store.Bookmarks.Count, tour.Stops.Count);
        // a tour with a stop nothing answers to walks into a wall
        foreach (var id in tour.Stops)
            Assert.Contains(store.Bookmarks, b => b.Id == id);
    }

    /// <summary>both kinds: a bookmark anchored to lines in a file, and a
    /// free camera position that is only a view.</summary>
    [Fact]
    public void BothKindsOfBookmarkAreRepresented()
    {
        using var repo = SampleRepo.Build("atlas_samples");
        Generate(repo.Path);

        var marks = BookmarkStore.Load(repo.Path).Bookmarks;

        Assert.Contains(marks, b => b.File is not null && b.EndLine >= b.Line);
        Assert.Contains(marks, b => b.File is null && b.S > 0);
    }

    /// <summary>an anchored sample must frame the lines it names, or the tour
    /// stops somewhere arbitrary in the file.</summary>
    [Fact]
    public void AnAnchoredBookmarkResolvesToItsOwnLines()
    {
        using var repo = SampleRepo.Build("atlas_samples");
        Generate(repo.Path);

        using var scene = new Scene(Scanner.Build(repo.Path));
        var marks = BookmarkStore.Load(repo.Path).Bookmarks;
        var anchored = marks.First(b => b.File is not null);

        var target = BookmarkTargets.Resolve(scene, anchored, 1200, 800);

        Assert.False(target.Orphaned);
        Assert.True(target.S > 0);
    }

    [Fact]
    public void RegeneratingReplacesTheSamplesRatherThanPilingThemUp()
    {
        using var repo = SampleRepo.Build("atlas_samples");

        Generate(repo.Path);
        int boards = BoardStore.Load(repo.Path).Boards.Count;
        int marks = BookmarkStore.Load(repo.Path).Bookmarks.Count;

        Generate(repo.Path);

        Assert.Equal(boards, BoardStore.Load(repo.Path).Boards.Count);
        Assert.Equal(marks, BookmarkStore.Load(repo.Path).Bookmarks.Count);
    }

    /// <summary>a board or bookmark the user made is not a sample and must
    /// survive a regeneration. The scratch board in this repo is named
    /// "sample", which is exactly the near miss worth testing.</summary>
    [Fact]
    public void SomethingTheUserMadeIsLeftAlone()
    {
        using var repo = SampleRepo.Build("atlas_samples");
        Generate(repo.Path);

        var boards = BoardStore.Load(repo.Path);
        var mine = boards.Create("sample");
        mine.Items.Add(new BoardItem { Id = "x", Kind = "note", Text = "mine" });
        boards.Save(mine);

        var marks = BookmarkStore.Load(repo.Path);
        marks.Bookmarks.Add(new Bookmark { Id = "mine", Name = "my place", X = 1, Y = 2, S = 3 });
        marks.Save();

        Generate(repo.Path);

        Assert.Contains(BoardStore.Load(repo.Path).Boards, b => b.Id == mine.Id);
        Assert.Contains(BookmarkStore.Load(repo.Path).Bookmarks, b => b.Id == "mine");
    }
}

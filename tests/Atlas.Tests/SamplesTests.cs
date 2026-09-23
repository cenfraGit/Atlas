namespace Atlas.Tests;

/// <summary>the sample boards and annotations that `--samples`
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

    /// <summary>a sample board comes with a tour through it, or there is no
    /// way to see one without making it by hand first.</summary>
    [Fact]
    public void TheTidyBoardsComeWithATour()
    {
        using var repo = SampleRepo.Build("atlas_samples");
        Generate(repo.Path);

        var tidy = BoardStore.Load(repo.Path).Boards.Where(b => b.Id is "sample-1" or "sample-2").ToList();
        Assert.Contains(tidy, b => b.Stops.Count > 0);
        foreach (var b in tidy)
        {
            // the whole board, then one stop per window - and no tour at all
            // on a board the fixture had none of the files for
            int windows = b.Items.Count(i => i.Kind == "file");
            Assert.Equal(windows == 0 ? 0 : windows + 1, b.Stops.Count);
            Assert.All(b.Stops, s => Assert.True(s.W > 0 && s.H > 0));
        }
    }

    /// <summary>and every stop frames something that is on the board.</summary>
    [Fact]
    public void EveryStopLooksAtItsWindow()
    {
        using var repo = SampleRepo.Build("atlas_samples");
        Generate(repo.Path);

        var b = BoardStore.Load(repo.Path).Boards.First(x => x.Id == "sample-1");
        var windows = b.Items.Where(i => i.Kind == "file").ToList();
        for (int n = 0; n < windows.Count; n++)
        {
            var stop = b.Stops[n + 1];
            Assert.InRange(windows[n].Y, stop.Y - stop.H / 2, stop.Y + stop.H / 2);
        }
    }

    [Fact]
    public void RegeneratingReplacesTheSamplesRatherThanPilingThemUp()
    {
        using var repo = SampleRepo.Build("atlas_samples");

        Generate(repo.Path);
        int boards = BoardStore.Load(repo.Path).Boards.Count;

        Generate(repo.Path);

        Assert.Equal(boards, BoardStore.Load(repo.Path).Boards.Count);
    }

    /// <summary>a board the user made is not a sample and must
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

        Generate(repo.Path);

        Assert.Contains(BoardStore.Load(repo.Path).Boards, b => b.Id == mine.Id);
    }
}

namespace Atlas.Tests;

/// <summary>searching what the files say.
///
/// The lines come from a provider rather than from disk, so every one of
/// these is a couple of arrays and no I/O - which is also the point of the
/// design: the same code searches the working copy, a commit's tree, and
/// this.</summary>
public class GrepTests
{
    static Scan ScanOf(params string[] paths)
    {
        var files = paths.Select((p, i) => new FileRec { P = p, N = 10, X = 0, Y = i * 50, W = 240, H = 40 }).ToList();
        return new Scan
        {
            Root = "/repo", LineH = 3, HeaderH = 22, Files = files,
            Folders = [], World = new WorldSize { W = 1, H = 1 },
        };
    }

    static Func<string, string[]> Lines(Dictionary<string, string[]> by) =>
        p => by.GetValueOrDefault(p, []);

    [Fact]
    public void AnEmptyQueryFindsNothing()
    {
        var scan = ScanOf("a.cs");
        Assert.Empty(Grep.Run(scan, "", Lines(new() { ["a.cs"] = ["hello"] })));
    }

    [Fact]
    public void AMatchCarriesItsFileLineAndColumn()
    {
        var scan = ScanOf("a.cs");
        var found = Grep.Run(scan, "needle",
            Lines(new() { ["a.cs"] = ["nothing", "a needle here", "nothing"] }));

        var hit = Assert.Single(found);
        Assert.Equal("a.cs", hit.Path);
        Assert.Equal(0, hit.File);
        Assert.Equal(1, hit.Line);
        Assert.Equal(2, hit.Col);
    }

    [Fact]
    public void MatchingIgnoresCase()
    {
        var scan = ScanOf("a.cs");
        var found = Grep.Run(scan, "NEEDLE", Lines(new() { ["a.cs"] = ["a needle"] }));
        Assert.Single(found);
    }

    /// <summary>one row per line, not per occurrence: a line with the word
    /// twice is still one place to go and look.</summary>
    [Fact]
    public void ALineWithTwoMatchesIsOneRow()
    {
        var scan = ScanOf("a.cs");
        var found = Grep.Run(scan, "x", Lines(new() { ["a.cs"] = ["x and x and x"] }));

        var hit = Assert.Single(found);
        Assert.Equal(0, hit.Col);
    }

    [Fact]
    public void EveryFileIsSearched()
    {
        var scan = ScanOf("a.cs", "b.cs", "c.cs");
        var found = Grep.Run(scan, "hit", Lines(new()
        {
            ["a.cs"] = ["hit"],
            ["b.cs"] = ["no"],
            ["c.cs"] = ["also a hit"],
        }));

        Assert.Equal(2, found.Count);
        Assert.Equal(["a.cs", "c.cs"], found.Select(f => f.Path));
    }

    /// <summary>a board searches the files it has windows onto. Searching
    /// the whole repo from a board answers a question nobody asked.</summary>
    [Fact]
    public void AScopeLimitsWhichFilesAreRead()
    {
        var scan = ScanOf("a.cs", "b.cs");
        var read = new List<string>();

        var found = Grep.Run(scan, "hit", p =>
        {
            read.Add(p);
            return ["hit"];
        }, only: new HashSet<string> { "b.cs" });

        Assert.Equal(["b.cs"], read);
        Assert.Equal("b.cs", Assert.Single(found).Path);
    }

    [Fact]
    public void TheListIsCapped()
    {
        var scan = ScanOf("a.cs");
        var many = Enumerable.Repeat("hit", 500).ToArray();

        Assert.Equal(20, Grep.Run(scan, "hit", Lines(new() { ["a.cs"] = many }), limit: 20).Count);
    }

    /// <summary>capping has to stop the reading too, or a one word query on
    /// a large repo reads every file to throw the results away.</summary>
    [Fact]
    public void ReachingTheCapStopsReading()
    {
        var scan = ScanOf("a.cs", "b.cs", "c.cs");
        var read = new List<string>();

        Grep.Run(scan, "hit", p => { read.Add(p); return ["hit", "hit"]; }, limit: 2);

        Assert.Equal(["a.cs"], read);
    }

    [Fact]
    public void ACancelledSearchStops()
    {
        var scan = ScanOf("a.cs", "b.cs", "c.cs");
        var cts = new CancellationTokenSource();
        var read = new List<string>();

        Grep.Run(scan, "hit", p =>
        {
            read.Add(p);
            cts.Cancel();
            return ["hit"];
        }, cancel: cts.Token);

        Assert.Equal(["a.cs"], read);
    }

    /// <summary>a preview is for reading, so the indentation goes - twelve
    /// spaces of it turns a list of matches into a list of blanks.</summary>
    [Fact]
    public void ThePreviewIsTrimmed()
    {
        var scan = ScanOf("a.cs");
        var found = Grep.Run(scan, "deep", Lines(new() { ["a.cs"] = ["            deep inside"] }));

        Assert.Equal("deep inside", Assert.Single(found).Text);
    }

    /// <summary>and clipped: a minified file is one line of forty thousand
    /// characters, and a row of the list cannot be.</summary>
    [Fact]
    public void ThePreviewIsClipped()
    {
        var scan = ScanOf("a.cs");
        var huge = "x" + new string('y', 40_000);
        var found = Grep.Run(scan, "x", Lines(new() { ["a.cs"] = [huge] }));

        Assert.True(Assert.Single(found).Text.Length < 500);
    }

    [Fact]
    public void AFileThatCannotBeReadIsSkippedRatherThanFatal()
    {
        var scan = ScanOf("a.cs", "b.cs");
        var found = Grep.Run(scan, "hit", p => p == "a.cs" ? [] : ["hit"]);

        Assert.Equal("b.cs", Assert.Single(found).Path);
    }

    [Fact]
    public void TheFileCountIsOfFilesNotLines()
    {
        var scan = ScanOf("a.cs", "b.cs");
        var found = Grep.Run(scan, "hit", Lines(new()
        {
            ["a.cs"] = ["hit", "hit", "hit"],
            ["b.cs"] = ["hit"],
        }));

        Assert.Equal(4, found.Count);
        Assert.Equal(2, Grep.FilesIn(found));
    }
}

/// <summary>where the lines come from when it is not a test.</summary>
[Collection("render")]
public class GrepReadingTests
{
    [Fact]
    public void ReadingForSearchDoesNotPoisonTheDrawCache()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));
        var path = SampleRepo.LongFile;

        Assert.Null(scene.LinesOf(path));

        var lines = scene.ReadLines(path);
        Assert.True(lines.Length > 0);

        // still not in the draw cache. Putting it there without the syntax
        // runs that are normally filled in beside it would make DrawCode
        // paint the whole file in one colour - a search that touched every
        // file would leave the map grey
        Assert.Null(scene.LinesOf(path));
    }

    [Fact]
    public void ReadingForSearchUsesWhatIsAlreadyLoaded()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));
        var path = SampleRepo.LongFile;

        // the real loader is asynchronous; the snapshot source is not, and
        // it is the case that matters - a commit's text is not on disk
        scene.ShowSnapshot(scene.Data, p => p == path ? ["from the commit"] : null);

        Assert.Equal(["from the commit"], scene.ReadLines(path));
    }

    [Fact]
    public void AFileTheSnapshotDoesNotHaveReadsAsEmpty()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        scene.ShowSnapshot(scene.Data, _ => null);

        Assert.Empty(scene.ReadLines(SampleRepo.LongFile));
    }

    [Fact]
    public void AMissingFileReadsAsEmptyRatherThanThrowing()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        Assert.Empty(scene.ReadLines("nowhere/at/all.cs"));
    }

    /// <summary>end to end against real files on disk.</summary>
    [Fact]
    public void SearchingARealRepoFindsARealLine()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        var found = Grep.Run(scene.Data, "class Panel", scene.ReadLines);

        var hit = Assert.Single(found);
        Assert.Equal("app/ui/Panel.cs", hit.Path);
        Assert.Contains("class Panel", hit.Text);
    }
}

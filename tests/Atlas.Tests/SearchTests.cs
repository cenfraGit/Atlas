namespace Atlas.Tests;

/// <summary>ranking is the whole feature: a search that finds the file but puts
/// it fourth is a search you stop using. The fixture is a fixed list of paths
/// so the expected winner is stated outright rather than inferred from
/// whatever repo the test happened to run against.</summary>
public class SearchTests
{
    static readonly string[] Paths =
    [
        "app/Scene.cs",
        "app/Scanner.cs",
        "app/SearchOverlay.cs",
        "app/Search.cs",
        "app/BoardOverlay.cs",
        "app/ui/panel_view.cs",
        "docs/scene-notes.md",
        "tests/SceneTests.cs",
    ];

    static Scan Fixture() => new()
    {
        Root = "/repo",
        LineH = 3,
        HeaderH = 22,
        Files = Paths.Select(p => new FileRec { P = p, N = 10, D = new int[30] }).ToList(),
    };

    static List<string> Hits(string query) =>
        Search.Run(Fixture(), query).Select(h => h.Path).ToList();

    [Fact]
    public void AnEmptyQueryFindsNothing()
    {
        Assert.Empty(Search.Run(Fixture(), ""));
        Assert.Empty(Search.Run(Fixture(), "   "));
    }

    [Fact]
    public void NonsenseFindsNothing() => Assert.Empty(Hits("zzqqxxjjwwvv"));

    [Fact]
    public void AnExactFileNameRanksFirst() => Assert.Equal("app/Scene.cs", Hits("Scene.cs")[0]);

    [Fact]
    public void AnExactNamePrefixBeatsALongerFileThatAlsoMatches() =>
        Assert.Equal("app/Search.cs", Hits("Search")[0]);

    [Fact]
    public void MatchingTheNameBeatsMatchingTheDirectory()
    {
        // "app" is in every directory here, but panel_view has no name match
        var hits = Hits("Scan");
        Assert.Equal("app/Scanner.cs", hits[0]);
    }

    [Fact]
    public void SearchIsCaseInsensitive() =>
        Assert.Equal(Hits("Scene.cs"), Hits("scene.cs"));

    [Fact]
    public void AnAbbreviationFindsTheFile()
    {
        // subsequence matching: S-O-v for SearchOverlay
        Assert.Contains("app/SearchOverlay.cs", Hits("SOv"));
    }

    [Fact]
    public void SpacesInAQueryAreIgnored() =>
        Assert.Equal(Hits("Scene.cs"), Hits("Sce ne.cs"));

    [Fact]
    public void AQueryCanSpanTheDirectoryAndTheName() =>
        Assert.Contains("app/ui/panel_view.cs", Hits("uipanel"));

    [Fact]
    public void ResultsComeOutBestFirst()
    {
        var hits = Search.Run(Fixture(), "sc");
        Assert.True(hits.Count > 1, "the fixture should produce several hits");
        Assert.Equal(hits.OrderByDescending(h => h.Score).Select(h => h.Score),
                     hits.Select(h => h.Score));
    }

    [Fact]
    public void TiesAreBrokenByTheShorterPath()
    {
        var hits = Search.Run(Fixture(), "sc");
        foreach (var (a, b) in hits.Zip(hits.Skip(1)))
            if (a.Score == b.Score)
                Assert.True(a.Path.Length <= b.Path.Length, $"{a.Path} should not follow {b.Path}");
    }

    [Fact]
    public void EveryHitIndexesBackIntoTheScan()
    {
        var scan = Fixture();
        foreach (var hit in Search.Run(scan, "s"))
            Assert.Equal(hit.Path, scan.Files[hit.Index].P);
    }

    [Fact]
    public void TheResultCountIsCapped()
    {
        var scan = new Scan
        {
            Files = Enumerable.Range(0, 200)
                .Select(i => new FileRec { P = $"app/file{i}.cs", N = 1, D = new int[3] })
                .ToList(),
        };
        Assert.Equal(25, Search.Run(scan, "file").Count);
        Assert.Equal(5, Search.Run(scan, "file", limit: 5).Count);
    }

    [Fact]
    public void ScanningAnEmptyRepoIsSafe() =>
        Assert.Empty(Search.Run(new Scan(), "anything"));
}

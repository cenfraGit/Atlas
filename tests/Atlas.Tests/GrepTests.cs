using SkiaSharp;

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

/// <summary>marking every occurrence in the code that is on screen.</summary>
[Collection("render")]
public class FindHighlightTests
{
    const int W = 700, H = 500;

    static (Scene Scene, TempDir Repo, FileRec File) Reading()
    {
        var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var f = scene.Data.Files.First(x => x.P == SampleRepo.LongFile);
        scene.CamS = 8f;
        scene.CamX = f.X + Math.Min(f.W / 2, (W / 2f - 16) / 8f);
        scene.CamY = f.Y + scene.Data.HeaderH + 20 * scene.Data.LineH;
        return (scene, repo, f);
    }

    static SKColor[] Frame(Scene scene, string waitFor)
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            scene.Draw(canvas, W, H);

            // only the text tier loads source, and only the text tier can
            // show a mark. Waiting below it waits for something that is
            // never going to happen
            if (Scene.TierFor(scene.CamS) == 3)
            {
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (scene.LinesOf(waitFor) is null && DateTime.UtcNow < deadline) Thread.Sleep(10);
                Assert.True(scene.LinesOf(waitFor) is { Length: > 0 }, "the source never loaded");
            }

            scene.Draw(canvas, W, H);
            canvas.Flush();
        }
        var px = new SKColor[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                px[y * W + x] = bmp.GetPixel(x, y);
        bmp.Dispose();
        return px;
    }

    static int Changed(SKColor[] a, SKColor[] b)
    {
        int n = 0;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) n++;
        return n;
    }

    [Fact]
    public void TypingSomethingMarksItWhereverItIsDrawn()
    {
        var (scene, repo, f) = Reading();
        using (repo)
        using (scene)
        {
            var plain = Frame(scene, f.P);
            scene.Find = Grep.Pattern("Step");
            var marked = Frame(scene, f.P);

            Assert.True(Changed(plain, marked) > 200,
                "typing a word that is all over the file marked nothing");
        }
    }

    [Fact]
    public void SomethingThatIsNotThereMarksNothing()
    {
        var (scene, repo, f) = Reading();
        using (repo)
        using (scene)
        {
            var plain = Frame(scene, f.P);
            scene.Find = Grep.Pattern("zzzzzznotpresent");

            Assert.Equal(0, Changed(plain, Frame(scene, f.P)));
        }
    }

    [Fact]
    public void ClearingTheSearchTakesTheMarksAway()
    {
        var (scene, repo, f) = Reading();
        using (repo)
        using (scene)
        {
            var plain = Frame(scene, f.P);
            scene.Find = Grep.Pattern("Step");
            Frame(scene, f.P);
            scene.Find = null;

            Assert.Equal(0, Changed(plain, Frame(scene, f.P)));
        }
    }

    /// <summary>the one being looked at is drawn differently from the rest,
    /// or two matches on one screen lose your place.</summary>
    [Fact]
    public void TheCurrentMatchStandsOutFromTheOthers()
    {
        var (scene, repo, f) = Reading();
        using (repo)
        using (scene)
        {
            scene.Find = Grep.Pattern("Step");
            var all = Frame(scene, f.P);

            int file = scene.IndexOfPath(f.P);
            var line = scene.LinesOf(f.P)!;
            int at = -1, col = -1;
            for (int i = 20; i < Math.Min(line.Length, 60) && at < 0; i++)
            {
                col = line[i].IndexOf("Step", StringComparison.Ordinal);
                if (col >= 0) at = i;
            }
            Assert.True(at >= 0, "the fixture has no match on screen to single out");

            scene.FindAt = (file, at, col);
            Assert.True(Changed(all, Frame(scene, f.P)) > 20,
                "singling out the current match changed nothing");
        }
    }

    /// <summary>nothing at the bars tier: there is no text to mark, and a
    /// highlight on a line you cannot read is a claim about nothing.</summary>
    [Fact]
    public void NothingIsMarkedWhereTheCodeIsNotDrawn()
    {
        var (scene, repo, f) = Reading();
        using (repo)
        using (scene)
        {
            scene.CamS = 0.4f;
            scene.CamX = f.X + f.W / 2;
            scene.CamY = f.Y + f.H / 2;

            var plain = Frame(scene, f.P);
            scene.Find = Grep.Pattern("Step");

            Assert.Equal(0, Changed(plain, Frame(scene, f.P)));
        }
    }
}

using SkiaSharp;

namespace Atlas.Tests;

/// <summary>TextMate's registry and grammars are not thread safe: tokenising
/// two files at once corrupts the shared rule registry into unbounded
/// recursion, which takes the process down with a stack overflow that cannot
/// be caught. The lock inside Highlighter is what stops that, and this is what
/// proves the lock is still there.
///
/// These share a collection because the TextMate registry is process wide -
/// two Highlighters built at once is the very thing being guarded against.</summary>
[Collection("highlighter")]
public class HighlighterTests
{
    static readonly SKColor Fallback = new(0x9f, 0xd4, 0xea);

    static Highlighter New() => new(Fallback);

    [Fact]
    public void TokenisesCSharpIntoColouredRuns()
    {
        var runs = New().Tokenize(["public class Panel", "{", "}"], ".cs");

        Assert.NotNull(runs);
        Assert.Equal(3, runs!.Length);
        Assert.NotEmpty(runs[0]);
    }

    [Fact]
    public void AKeywordIsNotTheSameColourAsAnIdentifier()
    {
        var runs = New().Tokenize(["public class Panel"], ".cs")!;
        var colours = runs[0].Select(r => r.Color).Distinct().ToList();

        Assert.True(colours.Count > 1, "the whole line came back one colour");
    }

    [Fact]
    public void RunsCoverTheLineInOrderWithoutOverlapping()
    {
        var line = "var host = BuildHost();";
        var runs = New().Tokenize([line], ".cs")![0];

        int at = 0;
        foreach (var r in runs)
        {
            Assert.True(r.Start >= at, $"run at {r.Start} overlaps the one ending at {at}");
            Assert.True(r.End > r.Start, "an empty run was emitted");
            Assert.True(r.End <= line.Length, $"run ends at {r.End}, past the line");
            at = r.End;
        }
    }

    [Fact]
    public void AdjacentRunsOfOneColourAreMerged()
    {
        var runs = New().Tokenize(["            indented;"], ".cs")![0];

        foreach (var (a, b) in runs.Zip(runs.Skip(1)))
            Assert.False(a.End == b.Start && a.Color == b.Color, "two touching runs share a colour");
    }

    [Theory]
    [InlineData(".cs")]
    [InlineData(".ts")]
    [InlineData(".js")]
    [InlineData(".py")]
    [InlineData(".json")]
    [InlineData(".md")]
    public void TheLanguagesTheScannerAcceptsHaveGrammars(string ext) =>
        Assert.NotNull(New().Tokenize(["x"], ext));

    [Fact]
    public void AnUnknownLanguageIsNullRatherThanAThrow() =>
        Assert.Null(New().Tokenize(["x"], ".zzz"));

    [Fact]
    public void AnEmptyFileTokenisesToNothing() =>
        Assert.Empty(New().Tokenize([], ".cs")!);

    [Fact]
    public void AVeryLongFileIsDeclinedRatherThanDrawn()
    {
        // past the cap the canvas falls back to the bars tier
        var huge = Enumerable.Repeat("var x = 1;", 50_001).ToArray();
        Assert.Null(New().Tokenize(huge, ".cs"));
    }

    [Fact]
    public void AMultiLineCommentCarriesStateAcrossLines()
    {
        var runs = New().Tokenize(["/* opens here", "still inside", "*/ out again"], ".cs")!;

        // if state were not carried, line 1 would tokenise as plain code
        Assert.Equal(runs[0][^1].Color, runs[1][0].Color);
    }

    [Fact]
    public void TokenisingManyFilesAtOnceDoesNotCorruptTheRegistry()
    {
        // the ponytail: one Highlighter, many threads, the way the canvas
        // loads cards. Without the lock this does not fail - it takes the
        // whole process down
        var hl = New();
        var sources = new[]
        {
            (Lines: Enumerable.Repeat("public class A { void M() { } }", 200).ToArray(), Ext: ".cs"),
            (Enumerable.Repeat("const x: number = 1;", 200).ToArray(), ".ts"),
            (Enumerable.Repeat("def f(): return 1", 200).ToArray(), ".py"),
            (Enumerable.Repeat("# heading", 200).ToArray(), ".md"),
            (Enumerable.Repeat("{ \"a\": 1 }", 200).ToArray(), ".json"),
        };

        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        int done = 0;

        Parallel.For(0, 120, new ParallelOptions { MaxDegreeOfParallelism = 12 }, i =>
        {
            var (lines, ext) = sources[i % sources.Length];
            try
            {
                if (hl.Tokenize(lines, ext) is not null) Interlocked.Increment(ref done);
                else failures.Add($"{ext} came back null");
            }
            catch (Exception ex) { failures.Add($"{ext}: {ex.GetType().Name}: {ex.Message}"); }
        });

        Assert.Empty(failures);
        Assert.Equal(120, done);
    }

    [Fact]
    public void TokenisingIsDeterministic()
    {
        var hl = New();
        var lines = new[] { "public class Panel", "{", "    public int Width;", "}" };

        var first = hl.Tokenize(lines, ".cs")!;
        var again = hl.Tokenize(lines, ".cs")!;

        Assert.Equal(first.Length, again.Length);
        for (int i = 0; i < first.Length; i++)
            Assert.Equal(first[i], again[i]);
    }
}

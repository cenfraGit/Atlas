using Avalonia.Headless.XUnit;

namespace Atlas.Tests;

/// <summary>searching by regular expression and by whole word.
///
/// Plain case-insensitive substring was all there was, so finding a method
/// named `Step` meant wading through every `Stepped` and `StepMatch`. One
/// pattern drives both the list and the marks on screen, so the two can
/// never disagree about what matched.</summary>
public class GrepPatternTests
{
    static Scan ScanOf(string path) => new()
    {
        Root = "/repo", LineH = 3, HeaderH = 22, Folders = [], World = new WorldSize { W = 1, H = 1 },
        Files = [new FileRec { P = path, N = 10, W = 240, H = 40 }],
    };

    static List<Found> Run(string query, string[] lines, bool regex = false, bool word = false) =>
        Grep.Run(ScanOf("a.cs"), query, _ => lines, regex: regex, word: word);

    [Fact]
    public void ARegexMatchesByPatternAndReportsWhereItStarts()
    {
        var found = Run(@"Step\(\d+\)", ["Stepped();", "    Step(12);", "Step(x);"], regex: true);

        var hit = Assert.Single(found);
        Assert.Equal((1, 4), (hit.Line, hit.Col));
    }

    [Fact]
    public void AWholeWordLeavesOutLongerWords()
    {
        var found = Run("step", ["Stepped();", "StepMatch();", "a step here", "step"], word: true);
        Assert.Equal([2, 3], found.Select(f => f.Line));
    }

    /// <summary>and plain text stays plain: a dot is a dot unless regex is on.</summary>
    [Fact]
    public void PlainTextIsNotAPattern()
    {
        Assert.Single(Run("a.b", ["axb", "a.b"]));
        Assert.Equal(2, Run("a.b", ["axb", "a.b"], regex: true).Count);
    }

    /// <summary>a pattern still being typed - "Step(" - is not an error to
    /// throw, and it finds nothing rather than everything.</summary>
    [Theory]
    [InlineData("Step(")]
    [InlineData("(?<=a)b")]          // lookbehind: the engine that cannot hang refuses it
    public void APatternThatIsNotOneFindsNothing(string query)
    {
        Assert.Null(Grep.Pattern(query, regex: true));
        Assert.Empty(Run(query, ["Step(1)", "ab"], regex: true));
    }

    /// <summary>`x*` matches nothing at the start of every line, and that is
    /// not a line containing an x.</summary>
    [Fact]
    public void AnEmptyMatchIsNotAMatch()
    {
        var found = Run("x*", ["abc", "an x", "def"], regex: true);
        Assert.Equal((1, 3), (Assert.Single(found).Line, found[0].Col));
    }

    /// <summary>alt+R and alt+W flip the mode and search again with the
    /// same text, marks included.</summary>
    [AvaloniaFact]
    public void TogglingAModeSearchesAgain()
    {
        var panel = new GrepOverlay();
        var typed = new List<string>();
        panel.Typed += typed.Add;

        panel.Toggle(regex: true);
        Assert.True(panel.Regex);
        panel.Toggle(word: true);
        Assert.True(panel.Word);
        panel.Toggle(regex: true);
        Assert.False(panel.Regex);
        Assert.Equal(3, typed.Count);
    }
}

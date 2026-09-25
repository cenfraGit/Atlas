using SkiaSharp;

namespace Atlas.Tests;

[CollectionDefinition("console", DisableParallelization = true)]
public class ConsoleCollection;

/// <summary>`atlas board`, the command line an agent builds boards with.
///
/// The point of it is that the agent names code - a file, a symbol, a line -
/// and the real anchoring code works out where things go, so these check the
/// anchors it leaves behind as much as the positions.</summary>
[Collection("console")]
public class BoardCliTests
{
    const string P = "app/Loop.cs";

    // First() is lines 5-13 (1-based), Second() 15-23, Second(int) 25
    static string Source()
    {
        var t = new System.Text.StringBuilder();
        t.Append("namespace Demo;\n\npublic class Loop\n{\n");
        t.Append("    public void First()\n    {\n");
        for (int i = 0; i < 6; i++) t.Append($"        A({i});\n");
        t.Append("    }\n\n");
        t.Append("    public void Second()\n    {\n");
        for (int i = 0; i < 6; i++) t.Append($"        B({i});\n");
        t.Append("    }\n\n");
        t.Append("    public void Second(int n) { }\n}\n");
        return t.ToString();
    }

    static TempDir Repo()
    {
        var repo = SampleRepo.Build();
        repo.File(P, Source());
        return repo;
    }

    /// <summary>runs a script against the repo, as `atlas board NAME &lt; script`.</summary>
    static (int Code, string Out, string Err) Run(TempDir repo, string board, string script)
    {
        var (inp, output, error) = (Console.In, Console.Out, Console.Error);
        var o = new StringWriter();
        var e = new StringWriter();
        try
        {
            Console.SetIn(new StringReader(script));
            Console.SetOut(o);
            Console.SetError(e);
            int code = BoardCli.Run(["board", "--repo", repo.Path, board]);
            return (code, o.ToString(), e.ToString());
        }
        finally
        {
            Console.SetIn(inp);
            Console.SetOut(output);
            Console.SetError(error);
        }
    }

    static Board Stored(TempDir repo, string name) =>
        BoardStore.Load(repo.Path).Boards.Single(b => b.Name == name);

    [Fact]
    public void AWindowByItsSymbolShowsThatDeclarationAndIsAnchored()
    {
        using var repo = Repo();
        var (code, _, err) = Run(repo, "doc", "new\nwindow w Loop.cs First --title \"the loop\"\n");
        Assert.True(code == 0, err);

        var w = Stored(repo, "doc").Items.Single();
        Assert.Equal("the loop", w.Text);
        Assert.Equal((P, 4, 12), (w.File, w.Line, w.EndLine));
        Assert.Equal("Demo.Loop.First(0)", w.Symbol);
        Assert.NotNull(w.Key);
        Assert.NotNull(w.Context);
    }

    /// <summary>a frame round a method is pinned to that method, top and
    /// bottom, the same as one drawn by hand and let go of there.</summary>
    [Fact]
    public void ABoxAroundASymbolIsPinnedToItsFirstAndLastLine()
    {
        using var repo = Repo();
        var (code, _, err) = Run(repo, "doc", """
            new
            window w Loop.cs Loop
            box b --around w:Second(0) --color red
            """);
        Assert.True(code == 0, err);

        var board = Stored(repo, "doc");
        var b = board.Items.Single(i => i.Id == "b");
        Assert.Equal("w", b.Host);
        Assert.Equal(("Demo.Loop.Second(0)", 0), (b.Symbol, b.Offset));
        Assert.Equal("Demo.Loop.Second(0)", b.EndSymbol);
        Assert.Equal("#d95c5c", b.Color);

        // and it follows the method when the file changes above it
        float before = b.Y, h = b.H;
        File.WriteAllText(Path.Combine(repo.Path, "app", "Loop.cs"),
            Source().Replace("    public void Second()\n", "    // one\n    // two\n    public void Second()\n"));
        Symbols.Forget(Path.Combine(repo.Path, "app", "Loop.cs"));
        using var edited = new Scene(Scanner.Build(repo.Path)) { ActiveBoard = board };
        edited.AnchorBoard(board);
        float step = edited.LineStepIn(board.Items.Single(i => i.Id == "w"), edited.Data.Files[edited.IndexOfPath(P)]);
        Assert.Equal(before + 2 * step, b.Y, 1);
        Assert.Equal(h, b.H, 1);
    }

    /// <summary>a script is all or nothing: a mistake on line three leaves
    /// no half-built board behind, so the fixed script can simply be run
    /// again.</summary>
    [Fact]
    public void AFailingScriptSavesNothing()
    {
        using var repo = Repo();
        var (code, _, err) = Run(repo, "doc", "new\nwindow w Loop.cs First\nwindow x Loop.cs Nope\n");

        Assert.Equal(1, code);
        Assert.Contains("line 3", err);
        Assert.Empty(BoardStore.Load(repo.Path).Boards);

        // and on a board that exists, what it had is kept
        Assert.Equal(0, Run(repo, "doc", "new\nwindow w Loop.cs First\n").Code);
        Assert.Equal(1, Run(repo, "doc", "rm w\nwindow x Loop.cs Nope\n").Code);
        Assert.Equal("w", Stored(repo, "doc").Items.Single().Id);
    }

    /// <summary>an error an agent can act on: it names what is there.</summary>
    [Theory]
    [InlineData("window x Loop.cs Frist", "First")]
    [InlineData("window x Loop.cs Second", "Second(1)")]     // two overloads: say which
    [InlineData("window x Lop.cs", "no file")]
    [InlineData("box b --around w:15-16", "shows lines 5-13")]    // not in that window
    [InlineData("note n hi --on nothing:1", "ids: w")]
    public void MistakesSayWhatIsThere(string line, string hint)
    {
        using var repo = Repo();
        var (code, _, err) = Run(repo, "doc", $"new\nwindow w Loop.cs First\n{line}\n");
        Assert.Equal(1, code);
        Assert.Contains(hint, err);
    }

    [Fact]
    public void AnOverloadCanBeNamedByItsParameterCount()
    {
        using var repo = Repo();
        Assert.Equal(0, Run(repo, "doc", "new\nwindow w Loop.cs \"Second(1)\"\n").Code);
        Assert.Equal(24, Stored(repo, "doc").Items.Single().Line);
    }

    /// <summary>relative placement, and nothing placed that way lands on
    /// top of something else.</summary>
    [Fact]
    public void PlacedItemsSitBesideEachOtherAndNeverOverlap()
    {
        using var repo = Repo();
        var (code, output, err) = Run(repo, "doc", """
            new
            window a Loop.cs First
            window b Loop.cs Second(0) --right-of a
            note n1 "about First" --on a:First
            note n2 "more about First" --on a:First
            label t "title" --above a
            show
            """);
        Assert.True(code == 0, err);

        var items = Stored(repo, "doc").Items.ToDictionary(i => i.Id);
        Assert.Equal(items["a"].X + items["a"].W + 60, items["b"].X);
        Assert.Equal(items["a"].Y, items["b"].Y);
        Assert.True(items["t"].Y < items["a"].Y);
        // n1 would have landed on b, which is right of a; n2 on n1
        Assert.True(items["n1"].Y > items["b"].Y);
        Assert.True(items["n2"].Y > items["n1"].Y);
        Assert.Contains("no warnings", output);
    }

    [Fact]
    public void AnArrowIsTiedAndAStopFramesWhatItNames()
    {
        using var repo = Repo();
        var (code, _, err) = Run(repo, "doc", """
            new
            window a Loop.cs First
            window b Loop.cs Second(0) --right-of a
            arrow x a b --to-side left
            stop "both"
            stop "just a" --frame a --pad 0 --at 1
            """);
        Assert.True(code == 0, err);

        var board = Stored(repo, "doc");
        var x = board.Items.Single(i => i.Id == "x");
        Assert.Equal(("a", "b", Scene.Left), (x.From, x.To, x.ToSide));
        Assert.Equal(["just a", "both"], board.Stops.Select(s => s.Name));
        var a = board.Items.Single(i => i.Id == "a");
        Assert.Equal(a.W, board.Stops[0].W, 1);
        Assert.Equal(a.X + a.W / 2, board.Stops[0].X, 1);
    }

    /// <summary>moving a window takes what is drawn on it along, and a
    /// second `new` without --replace does not wipe a board.</summary>
    [Fact]
    public void MovingAWindowCarriesItsDrawings()
    {
        using var repo = Repo();
        Assert.Equal(0, Run(repo, "doc", "new\nwindow w Loop.cs Loop\nbox b --around w:First\n").Code);
        float by = Stored(repo, "doc").Items.Single(i => i.Id == "b").Y;

        Assert.Equal(0, Run(repo, "doc", "move w --at 0,500\n").Code);
        Assert.Equal(by + 500, Stored(repo, "doc").Items.Single(i => i.Id == "b").Y, 1);

        var (code, _, err) = Run(repo, "doc", "new\n");
        Assert.Equal(1, code);
        Assert.Contains("--replace", err);
        Assert.Equal(0, Run(repo, "doc", "new --replace\n").Code);
        Assert.Empty(Stored(repo, "doc").Items);
    }

    [Fact]
    public void RenderWritesTheBoardAsAnImage()
    {
        using var repo = Repo();
        var png = Path.Combine(repo.Path, "out", "b.png");
        var (code, _, err) = Run(repo, "doc", $"new\nwindow w Loop.cs First\nrender \"{png}\" --px 800\n");
        Assert.True(code == 0, err);

        using var bmp = SKBitmap.Decode(png);
        Assert.Equal(800, bmp.Width);
        // something besides the background was drawn
        var bg = bmp.GetPixel(0, 0);
        Assert.Contains(Enumerable.Range(0, 800), x => bmp.GetPixel(x, bmp.Height / 2) != bg);
    }

    /// <summary>a stop made by framing items follows them, so moving a
    /// window does not leave the tour looking at empty canvas.</summary>
    [Fact]
    public void AStopReframesWhenItsItemsMove()
    {
        using var repo = Repo();
        Assert.Equal(0, Run(repo, "doc", "new\nwindow a Loop.cs First\nwindow b Loop.cs Second(0)\nstop s --frame a\nstop all\n").Code);
        Assert.Equal(0, Run(repo, "doc", "move a --at 5000,0\n").Code);

        var board = Stored(repo, "doc");
        var a = board.Items.Single(i => i.Id == "a");
        Assert.Equal(a.X + a.W / 2, board.Stops[0].X, 1);
        // the whole-board stop grew to take the move in
        Assert.True(board.Stops[1].X + board.Stops[1].W / 2 >= a.X + a.W);
    }

    /// <summary>a heading is as wide as its words: at the old fixed width a
    /// title at heading size wrapped onto three lines.</summary>
    [Fact]
    public void ALabelIsAsWideAsItsWords()
    {
        using var repo = Repo();
        Assert.Equal(0, Run(repo, "doc", "new\nlabel t \"How anchoring works, from end to end\" --text-size 40\n").Code);

        var t = Stored(repo, "doc").Items.Single();
        using var scene = new Scene(Scanner.Build(repo.Path));
        Assert.Equal(Scene.LineStep(40), scene.ItemHeight(t), 1);
        Assert.True(t.W > 520);
    }

    /// <summary>--row lines up with X's top whatever hangs off the row, where
    /// --right-of a note placed lower down started a staircase.</summary>
    [Fact]
    public void RowPlacementKeepsTheRowLevel()
    {
        using var repo = Repo();
        Assert.Equal(0, Run(repo, "doc", """
            new
            window a Loop.cs First
            note n "down the side" --on a:10
            window b Loop.cs Second(0) --row a
            """).Code);

        var items = Stored(repo, "doc").Items.ToDictionary(i => i.Id);
        Assert.Equal(items["a"].Y, items["b"].Y);
        Assert.True(items["b"].X > items["n"].X + items["n"].W);
    }

    [Fact]
    public void ShowWarnsAboutAnArrowThroughSomething()
    {
        using var repo = Repo();
        var (code, output, err) = Run(repo, "doc", """
            new
            note a "left" --at 0,0
            note mid "in the way" --at 500,0
            note b "right" --at 1000,0
            arrow x a b
            show
            """);
        Assert.True(code == 0, err);
        Assert.Contains("arrow x crosses mid", output);
    }

    [Theory]
    [InlineData("note n \"line one\\nline two\"", new[] { "note", "n", "line one\nline two" })]
    [InlineData("note n \"two words\" --on w:5", new[] { "note", "n", "two words", "--on", "w:5" })]
    [InlineData("label t 'it\\'s' ", new[] { "label", "t", "it's" })]
    [InlineData("note n \"\"", new[] { "note", "n", "" })]
    public void AScriptLineSplitsLikeAShellWould(string line, string[] words) =>
        Assert.Equal(words, BoardCli.Split(line));
}

namespace Atlas.Tests;

/// <summary>a board survives a week of edits, not just one.
///
/// Every other anchoring test makes one edit to a fresh board. Real use is
/// cumulative: somebody edits, opens the board, edits again, opens it again,
/// and each open re-anchors from what the last one left behind. A rule that
/// holds for one edit and drifts a line on the second is only visible here.
///
/// One board, four things on it that all mean "this method":
/// a window cropped to exactly the method, a rectangle drawn round it in a
/// second window onto the whole file, a note on one line inside it, and an
/// annotation on that same line. After every edit each must still be on the
/// method - all of it, not just its first line.</summary>
[Collection("render")]
public class EditScenarioTests
{
    const string Path = "app/Scenario.cs";
    const string Decl = "public int Target()";
    const string Marked = "// marked";

    static List<string> Start() =>
    [
        "namespace Demo;",
        "",
        "public class Scenario",
        "{",
        "    public int First()",
        "    {",
        "        return 1;",
        "    }",
        "",
        "    public int Target()",
        "    {",
        "        int a = 1;",
        "        int b = 2; // marked",
        "        return a + b;",
        "    }",
        "",
        "    public int Last()",
        "    {",
        "        return 3;",
        "    }",
        "}",
    ];

    static int Find(List<string> src, string needle, int from = 0)
    {
        for (int i = from; i < src.Count; i++)
            if (src[i].Contains(needle, StringComparison.Ordinal)) return i;
        throw new InvalidOperationException($"no line contains {needle}");
    }

    static void Write(TempDir repo, List<string> src) => repo.File(Path, string.Join("\n", src) + "\n");

    sealed class Fixture : IDisposable
    {
        public required TempDir Repo;
        public required Scene Scene;
        public required Board Board;
        public required BoardItem Cropped, Whole, Box, Note;
        public required Annotation Annotation;
        public List<string> Src = Start();

        public void Dispose()
        {
            Scene.ActiveBoard = null;
            Scene.Dispose();
            Repo.Dispose();
        }

        /// <summary>which source line a board y lands on, inside the whole-file
        /// window.</summary>
        public float RowOf(float y)
        {
            var f = Scene.Data.Files[Scene.IndexOfPath(Path)];
            return (y - Whole.Y - Scene.WinHeadH) / Scene.LineStepIn(Whole, f);
        }
    }

    static Fixture Open()
    {
        var repo = SampleRepo.Build();
        var src = Start();
        Write(repo, src);

        var scene = new Scene(Scanner.Build(repo.Path));
        var board = new Board { Id = "b", Name = "scenario" };
        scene.ActiveBoard = board;
        var f = scene.Data.Files[scene.IndexOfPath(Path)];

        int decl = Find(src, Decl), close = Find(src, "    }", decl), mark = Find(src, Marked);

        var cropped = new BoardItem
        {
            Id = "cropped", Kind = "file", File = Path, Key = scene.KeyFor(Path),
            X = 1000, Y = 0, W = 620, Line = decl, EndLine = close,
        };
        var whole = new BoardItem
        {
            Id = "whole", Kind = "file", File = Path, Key = scene.KeyFor(Path),
            X = 0, Y = 0, W = 620, Line = 0, EndLine = -1,
        };
        board.Items.Add(cropped);
        board.Items.Add(whole);
        scene.Reanchor(cropped);
        scene.Reanchor(whole);

        float step = scene.LineStepIn(whole, f);
        float top = whole.Y + Scene.WinHeadH;
        var box = new BoardItem
        {
            Id = "box", Kind = "shape", X = 5, W = 600,
            Y = top + decl * step, H = (close - decl + 1) * step,
        };
        var note = new BoardItem
        {
            Id = "note", Kind = "note", X = 400, W = 150, H = 40, Text = "this one",
            Y = top + (mark + 0.25f) * step,
        };
        board.Items.Add(box);
        board.Items.Add(note);
        scene.PinOver(box);
        scene.PinOver(note);

        var full = System.IO.Path.Combine(repo.Path, Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var annotation = Anchors.Create(Path, full, scene.ReadLines(Path), mark, "look here");

        return new Fixture
        {
            Repo = repo, Scene = scene, Board = board,
            Cropped = cropped, Whole = whole, Box = box, Note = note, Annotation = annotation,
        };
    }

    /// <summary>write the edit, open the board the way the app does, and
    /// check that all four are still on the method.</summary>
    static void Edit(Fixture fx, string what, Action<List<string>> change)
    {
        change(fx.Src);
        Write(fx.Repo, fx.Src);
        fx.Scene.AnchorBoard(fx.Board);

        int decl = Find(fx.Src, Decl), close = Find(fx.Src, "    }", decl), mark = Find(fx.Src, Marked);

        Assert.True(decl == fx.Cropped.Line, $"{what}: the cropped window starts at {fx.Cropped.Line}, the method at {decl}");
        Assert.True(close == fx.Cropped.EndLine, $"{what}: the cropped window ends at {fx.Cropped.EndLine}, the method at {close}");

        float boxTop = fx.RowOf(fx.Box.Y), boxBottom = fx.RowOf(fx.Box.Y + fx.Box.H);
        Assert.True(Math.Abs(boxTop - decl) < 0.1f, $"{what}: the box's top is on line {boxTop:F2}, the declaration is {decl}");
        Assert.True(Math.Abs(boxBottom - (close + 1)) < 0.1f, $"{what}: the box's bottom is at {boxBottom:F2}, the method ends at {close + 1}");

        int noteRow = (int)MathF.Floor(fx.RowOf(fx.Note.Y));
        Assert.True(noteRow == mark, $"{what}: the note is on line {noteRow}, the marked line is {mark}");

        var full = System.IO.Path.Combine(fx.Repo.Path, Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var at = Anchors.Resolve(fx.Annotation, full, fx.Src.ToArray());
        Assert.True(at.Resolved && at.Line == mark, $"{what}: the annotation resolves to {at.Line}, the marked line is {mark}");
    }

    [Fact]
    public void EverythingStaysOnTheMethodThroughAWeekOfEdits()
    {
        using var fx = Open();

        Edit(fx, "nothing changed", _ => { });

        Edit(fx, "five lines above everything", s =>
            s.InsertRange(Find(s, "public class Scenario") + 2, Enumerable.Repeat("    // filler", 5)));

        Edit(fx, "the method above grows", s =>
            s.InsertRange(Find(s, "return 1;"), Enumerable.Range(0, 7).Select(n => $"        First({n});")));

        Edit(fx, "lines inside, above the marked line", s =>
            s.InsertRange(Find(s, "int a = 1;") + 1, Enumerable.Range(0, 3).Select(n => $"        int above{n} = {n};")));

        Edit(fx, "lines inside, below the marked line", s =>
            s.InsertRange(Find(s, Marked) + 1, Enumerable.Range(0, 4).Select(n => $"        int below{n} = {n};")));

        Edit(fx, "some of those taken out again", s =>
            s.RemoveRange(Find(s, "int below0"), 2));

        Edit(fx, "filler above removed", s =>
            s.RemoveRange(Find(s, "// filler"), 2));

        Edit(fx, "a new method between", s =>
            s.InsertRange(Find(s, Decl), ["    public int Middle()", "    {", "        return 0;", "    }", ""]));

        Edit(fx, "the method above renamed", s =>
            s[Find(s, "public int First()")] = "    public int Renamed()");

        Edit(fx, "the method below grows", s =>
            s.InsertRange(Find(s, "return 3;"), Enumerable.Range(0, 6).Select(n => $"        Last({n});")));

        Edit(fx, "and the board opened again with nothing new", _ => { });
    }

    /// <summary>a window cropped before ends were anchored, on a file that
    /// has changed since. Its end was anchored at the stored line number -
    /// which is from before the edit - so the first open collapsed a window
    /// whose code had moved down onto a single line.</summary>
    [Fact]
    public void AWindowCroppedBeforeEndsWereAnchoredKeepsItsWholeRange()
    {
        using var fx = Open();
        fx.Cropped.EndSymbol = null;
        fx.Cropped.EndOffset = null;
        int length = fx.Cropped.EndLine - fx.Cropped.Line;

        fx.Src.InsertRange(Find(fx.Src, "public class Scenario") + 2, Enumerable.Repeat("    // filler", 30));
        Write(fx.Repo, fx.Src);
        fx.Scene.AnchorBoard(fx.Board);

        int decl = Find(fx.Src, Decl);
        Assert.Equal((decl, decl + length), (fx.Cropped.Line, fx.Cropped.EndLine));
        Assert.NotNull(fx.Cropped.EndSymbol);

        // and from then on it follows the method
        fx.Src.InsertRange(Find(fx.Src, Marked) + 1, Enumerable.Repeat("        int more = 0;", 3));
        Write(fx.Repo, fx.Src);
        fx.Scene.AnchorBoard(fx.Board);
        Assert.Equal(Find(fx.Src, "    }", decl), fx.Cropped.EndLine);
    }

    /// <summary>a rectangle round two methods grows when the second one
    /// does. Its bottom used to be measured from the end of the first
    /// method - the one its top was on - so growth in the second slid out
    /// from under it.</summary>
    [Fact]
    public void ABoxRoundTwoMethodsGrowsWithTheSecond()
    {
        using var fx = Open();
        var f = fx.Scene.Data.Files[fx.Scene.IndexOfPath(Path)];
        float step = fx.Scene.LineStepIn(fx.Whole, f);
        float top = fx.Whole.Y + Scene.WinHeadH;

        int first = Find(fx.Src, Decl), lastDecl = Find(fx.Src, "public int Last()");
        int lastClose = Find(fx.Src, "    }", lastDecl);
        var pair = new BoardItem
        {
            Id = "pair", Kind = "shape", X = 5, W = 600,
            Y = top + first * step, H = (lastClose - first + 1) * step,
        };
        fx.Board.Items.Add(pair);
        fx.Scene.PinOver(pair);

        fx.Src.InsertRange(Find(fx.Src, "return 3;"), Enumerable.Range(0, 6).Select(n => $"        Last({n});"));
        Write(fx.Repo, fx.Src);
        fx.Scene.AnchorBoard(fx.Board);

        int close = Find(fx.Src, "    }", Find(fx.Src, "public int Last()"));
        Assert.Equal(close + 1, fx.RowOf(pair.Y + pair.H), 1);
    }
}

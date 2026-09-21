namespace Atlas.Tests;

/// <summary>where an annotation shows.
///
/// An annotation is attached to code, so by default it appears wherever that
/// code does - the map, and every board with a window onto the file. That is
/// right for "this is the hot path" and wrong for "this is step 2 of what
/// this board explains", which is about the board.
///
/// The board's id is the scope: null means everywhere. There is no such
/// thing as local to nothing, so there is no separate flag to disagree with
/// it. What these pin down is the filtering, and that filtering never
/// interferes with the anchoring - where a note is attached and where it is
/// shown are different questions.</summary>
public class AnnotationScopeTests
{
    sealed class Fixture : IDisposable
    {
        public readonly TempDir Repo = SampleRepo.Build();
        public readonly Scene Scene;
        public readonly AnnotationStore Store;
        public readonly string File = SampleRepo.LongFile;

        public Fixture()
        {
            Store = AnnotationStore.Load(Repo.Path);
            Scene = new Scene(Scanner.Build(Repo.Path)) { Notes = Store };
        }

        /// <summary>an annotation on a known line, global unless told.</summary>
        public Annotation Note(string text, string? board = null, int line = 10)
        {
            var full = Path.Combine(Repo.Path, File.Replace('/', Path.DirectorySeparatorChar));
            var lines = System.IO.File.ReadAllLines(full);
            var a = Anchors.Create(File, full, lines, line, line, text);
            a.Board = board;
            Store.Annotations.Add(a);
            Scene.Reanchor(File, full, lines);
            return a;
        }

        public Board OpenBoard(string id)
        {
            var board = new Board { Id = id, Name = "board " + id };
            Scene.ActiveBoard = board;
            return board;
        }

        public void Dispose()
        {
            Scene.ActiveBoard = null;
            Scene.Dispose();
            Repo.Dispose();
        }
    }

    // --- the default ------------------------------------------------------

    [Fact]
    public void AnAnnotationIsGlobalUnlessGivenABoard()
    {
        using var f = new Fixture();
        var a = f.Note("the hot path");

        Assert.True(a.Global);
        Assert.Null(a.Board);
    }

    [Fact]
    public void AGlobalNoteShowsOnTheMap()
    {
        using var f = new Fixture();
        f.Note("the hot path");

        Assert.Single(f.Scene.AnchorsFor(f.File));
    }

    [Fact]
    public void AGlobalNoteShowsOnEveryBoard()
    {
        using var f = new Fixture();
        f.Note("the hot path");

        f.OpenBoard("b1");
        Assert.Single(f.Scene.AnchorsFor(f.File));

        f.OpenBoard("b2");
        Assert.Single(f.Scene.AnchorsFor(f.File));
    }

    // --- kept to one board ------------------------------------------------

    [Fact]
    public void ALocalNoteShowsOnItsOwnBoard()
    {
        using var f = new Fixture();
        f.Note("step 2", board: "b1");

        f.OpenBoard("b1");
        Assert.Single(f.Scene.AnchorsFor(f.File));
    }

    [Fact]
    public void ALocalNoteDoesNotShowOnAnotherBoard()
    {
        using var f = new Fixture();
        f.Note("step 2", board: "b1");

        f.OpenBoard("b2");
        Assert.Empty(f.Scene.AnchorsFor(f.File));
    }

    [Fact]
    public void ALocalNoteDoesNotShowOnTheMap()
    {
        using var f = new Fixture();
        f.Note("step 2", board: "b1");

        // the map has no board, so nothing is local to it
        Assert.Empty(f.Scene.AnchorsFor(f.File));
    }

    [Fact]
    public void ABoardShowsItsOwnAndTheGlobalOnes()
    {
        using var f = new Fixture();
        f.Note("everywhere", line: 10);
        f.Note("only here", board: "b1", line: 40);
        f.Note("only there", board: "b2", line: 70);

        f.OpenBoard("b1");
        var shown = f.Scene.AnchorsFor(f.File).Select(x => x.A.Text).ToList();

        Assert.Equal(["everywhere", "only here"], shown.Order());
    }

    [Fact]
    public void TheGatheredChangeViewShowsGlobalOnesOnly()
    {
        using var f = new Fixture();
        f.Note("everywhere", line: 10);
        f.Note("only here", board: "changes", line: 40);

        // the gathered view belongs to a commit, not to the repo. It has an
        // id like any board, and nothing may be kept on something that is
        // thrown away when you leave it
        f.OpenBoard("changes");
        f.Scene.BoardReadOnly = true;

        var shown = f.Scene.AnchorsFor(f.File).Select(x => x.A.Text).ToList();
        Assert.Equal(["everywhere"], shown);

        // and an ordinary board with that id would show both, so the flag is
        // doing the work rather than the name
        f.Scene.BoardReadOnly = false;
        Assert.Equal(2, f.Scene.AnchorsFor(f.File).Count);
    }

    // --- the list sees everything ----------------------------------------

    [Fact]
    public void TheFullListIgnoresScope()
    {
        using var f = new Fixture();
        f.Note("everywhere", line: 10);
        f.Note("only here", board: "b1", line: 40);

        // on the map, where one of them is hidden
        Assert.Single(f.Scene.AnchorsFor(f.File));
        Assert.Equal(2, f.Scene.AllAnchorsFor(f.File).Count);
    }

    [Fact]
    public void AFileWithNoNotesIsEmptyEitherWay()
    {
        using var f = new Fixture();

        Assert.Empty(f.Scene.AnchorsFor("app/Program.cs"));
        Assert.Empty(f.Scene.AllAnchorsFor("app/Program.cs"));
    }

    // --- scope and anchoring are separate ---------------------------------

    [Fact]
    public void ChangingScopeDoesNotDisturbTheAnchor()
    {
        using var f = new Fixture();
        var a = f.Note("step 2", line: 40);

        f.OpenBoard("b1");
        var before = f.Scene.AnchorsFor(f.File).Single().R;

        a.Board = "b1";
        var after = f.Scene.AnchorsFor(f.File).Single().R;

        // no re-anchoring, and the same answer
        Assert.Equal(before.Line, after.Line);
        Assert.Equal(before.Kind, after.Kind);
    }

    [Fact]
    public void ALocalNoteStillFollowsTheCodeItIsAttachedTo()
    {
        using var f = new Fixture();
        var a = f.Note("step 2", board: "b1", line: 40);
        f.OpenBoard("b1");

        int was = f.Scene.AnchorsFor(f.File).Single().R.Line;

        // push the file down; the scope has nothing to do with the anchor
        var full = Path.Combine(f.Repo.Path, f.File.Replace('/', Path.DirectorySeparatorChar));
        var shifted = Enumerable.Repeat("// pushed down", 12)
            .Concat(System.IO.File.ReadAllLines(full)).ToArray();
        System.IO.File.WriteAllLines(full, shifted);
        Symbols.Forget(full);
        f.Scene.Reanchor(f.File, full, shifted);

        int now = f.Scene.AnchorsFor(f.File).Single().R.Line;

        Assert.True(now > was, $"the note should have moved down, {was} -> {now}");
        Assert.Equal("b1", a.Board);
    }

    // --- storage ----------------------------------------------------------

    [Fact]
    public void ScopeRoundTrips()
    {
        using var dir = new TempDir();
        var store = AnnotationStore.Load(dir.Path);
        store.Annotations.Add(new Annotation { Id = "a1", Text = "everywhere", File = "a.cs" });
        store.Annotations.Add(new Annotation { Id = "a2", Text = "here", File = "a.cs", Board = "b1" });
        store.Save();

        var reread = AnnotationStore.Load(dir.Path).Annotations;

        Assert.True(reread.Single(a => a.Id == "a1").Global);
        Assert.Equal("b1", reread.Single(a => a.Id == "a2").Board);
    }

    [Fact]
    public void AGlobalNoteWritesNoBoardField()
    {
        using var dir = new TempDir();
        var store = AnnotationStore.Load(dir.Path);
        store.Annotations.Add(new Annotation { Id = "a1", Text = "everywhere", File = "a.cs" });
        store.Save();

        // nulls are dropped on the way out, so every note ever written does
        // not gain a "board": null
        Assert.DoesNotContain("board", File.ReadAllText(AnnotationStore.PathFor(dir.Path)));
    }
}

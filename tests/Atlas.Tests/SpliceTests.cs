using SkiaSharp;

namespace Atlas.Tests;

/// <summary>removed lines on the review map.
///
/// Review mode draws a temporary scan of the commit's tree. Each changed
/// file's text gets what the whole change removed put back where it came
/// out, so the cards contain it, and a map says where every line went.</summary>
[Collection("render")]
public class SpliceTests
{
    static RemovedBlock Block(int at, int oldLine, params string[] lines) => new(at, oldLine, [.. lines]);

    [Fact]
    public void RemovedLinesGoAboveTheLineNowInTheirPlace()
    {
        var (text, map) = Splice.Of(["a", "b", "c"], [Block(1, 1, "gone1", "gone2")]);

        Assert.Equal(["a", "gone1", "gone2", "b", "c"], text);
        Assert.Equal([1, 2], map.RemovedRows);
        // the file's lines, and where each one went; one past the end too
        Assert.Equal([0, 3, 4, 5], map.RowOf);
    }

    /// <summary>the gutter shows the numbers a reader expects: the new file's
    /// on its lines, and on a removed row the one it had in the old file.</summary>
    [Fact]
    public void RowsAreNumberedAsTheFilesWere()
    {
        var (_, map) = Splice.Of(["a", "b", "c"], [Block(1, 5, "old6", "old7")]);

        Assert.Equal([1, 6, 7, 2, 3], map.Numbers);
    }

    [Fact]
    public void ARemovalOffTheEndGoesAfterTheLastLine()
    {
        var (text, map) = Splice.Of(["a", "b"], [Block(2, 2, "tail")]);

        Assert.Equal(["a", "b", "tail"], text);
        Assert.Equal([2], map.RemovedRows);
    }

    [Fact]
    public void ARemovalAtTheTopGoesFirst()
    {
        var (text, map) = Splice.Of(["a"], [Block(0, 0, "head")]);

        Assert.Equal(["head", "a"], text);
        Assert.Equal([1, 2], map.RowOf);
    }

    static ChangeSet Set(params FileChange[] files)
    {
        var set = new ChangeSet { Label = "c", Files = [.. files] };
        set.Index();
        return set;
    }

    /// <summary>only a changed file with removals is touched; one the change
    /// deleted has no card to put its lines in and is left out.</summary>
    [Fact]
    public void OnlyFilesWithRemovalsThatStillExistAreSpliced()
    {
        var snapshot = new Dictionary<string, string[]>
        {
            ["a.cs"] = ["one", "two"],
            ["b.cs"] = ["untouched"],
        };
        var a = new FileChange("a.cs", 0, 1);
        a.RemovedText.Add(Block(1, 1, "gone"));
        var gone = new FileChange("gone.cs", 0, 1);
        gone.RemovedText.Add(Block(0, 0, "all of it"));

        var (text, maps) = Splice.All(snapshot, Set(a, gone));

        Assert.Equal(["one", "gone", "two"], text["a.cs"]);
        Assert.Equal(["untouched"], text["b.cs"]);
        Assert.Equal(["a.cs"], maps.Keys);
        Assert.Equal(["one", "two"], snapshot["a.cs"]);       // the snapshot itself is left alone
    }

    /// <summary>for the whole change, added lines move to their rows and the
    /// removal markers go: the removed lines are rows now, drawn red.</summary>
    [Fact]
    public void TheWholeChangeMovesToTheRowsAndDropsItsMarkers()
    {
        var (_, map) = Splice.Of(["a", "b", "c"], [Block(1, 1, "gone")]);
        var c = new FileChange("a.cs", 1, 1);
        c.AddedLines.Add(2);
        c.RemovedAt.Add(1);

        var whole = Splice.Remap(Set(c), new Dictionary<string, Splice> { ["a.cs"] = map }, whole: true);

        Assert.Equal([3], whole.ByPath["a.cs"].AddedLines);
        Assert.Empty(whole.ByPath["a.cs"].RemovedAt);
    }

    /// <summary>one commit keeps its markers, moved to rows: its removals are
    /// not the ones spliced in.</summary>
    [Fact]
    public void OneCommitKeepsItsMarkersMovedToRows()
    {
        var (_, map) = Splice.Of(["a", "b", "c"], [Block(1, 1, "gone")]);
        var c = new FileChange("a.cs", 0, 1);
        c.RemovedAt.Add(2);

        var one = Splice.Remap(Set(c), new Dictionary<string, Splice> { ["a.cs"] = map }, whole: false);

        Assert.Equal([3], one.ByPath["a.cs"].RemovedAt);
    }

    /// <summary>and on the map, close in, a removed row is red the way an
    /// added one is green.</summary>
    [Fact]
    public void ARemovedRowIsRedOnTheMap()
    {
        using var repo = SampleRepo.Build();
        const string P = "src/S.cs";
        var lines = Enumerable.Range(0, 40).Select(n => $"var keep{n} = {n};").ToArray();
        repo.File(P, string.Join("\n", lines));
        using var scene = new Scene(Scanner.Build(repo.Path));

        var change = new FileChange(P, 0, 3);
        change.RemovedText.Add(Block(20, 20, "var gone1 = 1;", "var gone2 = 2;", "var gone3 = 3;"));
        var snapshot = new Dictionary<string, string[]> { [P] = lines };
        foreach (var f in scene.Data.Files.Where(f => f.P != P))
            snapshot[f.P] = scene.ReadLines(f.P);
        var (text, maps) = Splice.All(snapshot, Set(change));
        scene.ShowSnapshot(Scanner.BuildFrom(repo.Path, text), p => text.GetValueOrDefault(p), maps);
        scene.Review = Splice.Remap(Set(change), maps, whole: true);

        var card = scene.Data.Files[scene.IndexOfPath(P)];
        Assert.Equal(43, card.N);                       // taller by the three removed lines
        // the card's left edge in view, where the change stripe is
        scene.CamX = card.X + 90;
        scene.CamY = card.Y + scene.Data.HeaderH + 21.5f * scene.Data.LineH;
        scene.CamS = 3f;
        scene.Tier = Scene.TierFor(scene.CamS);

        using var bmp = new SKBitmap(600, 300);
        using (var canvas = new SKCanvas(bmp)) scene.Draw(canvas, 600, 300);
        // the stripe, a few pixels in from the card's left edge, past the
        // one pixel outline review mode draws round a changed file
        int left = (int)(300 + (card.X - scene.CamX) * scene.CamS);
        int RedAcross(int y)
        {
            int n = 0;
            for (int x = left + 2; x < left + 8; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Red > 150 && c.Red > c.Green + 60) n++;
            }
            return n;
        }
        // a removed row, and a kept row six lines further down
        int removed = RedAcross(150), kept = RedAcross(150 + (int)(6 * scene.Data.LineH * scene.CamS));
        Assert.True(removed > 3, $"only {removed} red pixels in the stripe of a removed row");
        Assert.Equal(0, kept);
    }

    /// <summary>the change view opens on a change, and a removal is one -
    /// even when, for the whole change, it is only rows now.</summary>
    [Fact]
    public void TheViewCanOpenOnARemoval()
    {
        using var repo = SampleRepo.Build();
        const string P = "src/S.cs";
        var lines = Enumerable.Range(0, 200).Select(n => $"var keep{n} = {n};").ToArray();
        repo.File(P, string.Join("\n", lines));
        using var scene = new Scene(Scanner.Build(repo.Path));

        var change = new FileChange(P, 0, 1);
        change.RemovedText.Add(Block(150, 150, "var gone = 0;"));
        var (text, maps) = Splice.All(new Dictionary<string, string[]> { [P] = lines }, Set(change));
        scene.ShowSnapshot(Scanner.BuildFrom(repo.Path, text), p => text.GetValueOrDefault(p), maps);
        var set = Splice.Remap(Set(change), maps, whole: true);

        var board = ChangeBoard.Build(set, scene, "c");
        var spot = ChangeBoard.FirstChange(board, set, scene);

        Assert.NotNull(spot);
        var window = board.Items.Single();
        Assert.True(spot!.Value.Y > window.Y + scene.ItemHeight(window) / 2,
            "the view opened near the top rather than at the removal three quarters down");
    }

    /// <summary>no removal mark straight under a removed row that is shown:
    /// the text put back above it says the same thing, and the mark was an
    /// opaque red line under every edited line. A removal with no text shown
    /// beside it keeps its mark.</summary>
    [Theory]
    [InlineData(new[] { 10 }, new[] { 9 }, new int[0])]          // under its own text
    [InlineData(new[] { 10 }, new[] { 3 }, new[] { 10 })]        // nothing shown beside it
    [InlineData(new[] { 10, 20 }, new[] { 19 }, new[] { 10 })]
    [InlineData(new[] { 10 }, new int[0], new[] { 10 })]         // nothing spliced at all
    public void AMarkUnderShownTextIsNotDrawn(int[] removedAt, int[] removedRows, int[] drawn) =>
        Assert.Equal(drawn, Scene.RemovalMarks(removedAt, removedRows));

    /// <summary>and through a real review: stepping to a commit that edited a
    /// line draws no mark under the edited line's removed text.</summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SteppingToACommitThatEditedALineMarksNothingUnderIt()
    {
        using var git = new GitFixture();
        var scene = new Scene(Scanner.Build(git.Path));
        var store = BoardStore.Load(git.Path);
        var view = new SceneView(scene);
        view.AttachBoards(store, new BoardOverlay(store));
        view.AttachReview(new ReviewOverlay(), new CommitsPanel());
        view.BuildLayers();
        new Avalonia.Controls.Window { Width = 800, Height = 600, Content = view }.Show();
        using var review = GitReview.Open(git.Path)!;
        view.OpenTarget(review.MergedPrs().Single());
        bool Until(Func<bool> done)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!done() && DateTime.UtcNow < deadline) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
            return done();
        }
        Assert.True(Until(() => scene.Review is not null));

        // the second commit edits Program.cs's Main: one line out, one in
        view.HandleKey(Avalonia.Input.Key.OemCloseBrackets);
        view.HandleKey(Avalonia.Input.Key.OemCloseBrackets);
        Assert.True(Until(() => scene.Review!.ByPath.TryGetValue("app/Program.cs", out var c) && c.RemovedAt.Count > 0));

        var change = scene.Review!.ByPath["app/Program.cs"];
        var rows = scene.Splices["app/Program.cs"].RemovedRows;
        Assert.Empty(Scene.RemovalMarks(change.RemovedAt, rows));
    }
}

namespace Atlas.Tests;

/// <summary>the gathered change view. The map answers where a change landed;
/// this answers what it said, so the hunks it picks out and the way they are
/// packed are the whole feature.</summary>
public class ChangeBoardTests
{
    static FileChange Change(string path, int[] added, int[]? removed = null)
    {
        var c = new FileChange(path, added.Length, removed?.Length ?? 0);
        c.AddedLines.AddRange(added);
        c.RemovedAt.AddRange(removed ?? []);
        return c;
    }

    // --- hunks -----------------------------------------------------------

    [Fact]
    public void AFileWithNoChangesHasNoHunks() =>
        Assert.Empty(ChangeBoard.HunksOf(Change("a.cs", []), 100));

    [Fact]
    public void AnEmptyFileHasNoHunks() =>
        Assert.Empty(ChangeBoard.HunksOf(Change("a.cs", [0, 1]), 0));

    [Fact]
    public void OneEditBecomesOneHunkWithContextAroundIt()
    {
        var hunk = Assert.Single(ChangeBoard.HunksOf(Change("a.cs", [50]), 100, context: 3, merge: 10));

        Assert.Equal(47, hunk.From);
        Assert.Equal(53, hunk.To);
        Assert.Equal(7, hunk.Lines);
    }

    [Fact]
    public void NearbyEditsMergeIntoOneHunk()
    {
        // two windows five lines apart would just be one window with a gap
        var hunk = Assert.Single(ChangeBoard.HunksOf(Change("a.cs", [50, 55]), 100, context: 3, merge: 10));

        Assert.Equal(47, hunk.From);
        Assert.Equal(58, hunk.To);
    }

    [Fact]
    public void DistantEditsStayApart()
    {
        var hunks = ChangeBoard.HunksOf(Change("a.cs", [10, 500]), 1000, context: 3, merge: 10);

        Assert.Equal(2, hunks.Count);
        Assert.Equal(7, hunks[0].From);
        Assert.Equal(497, hunks[1].From);
    }

    [Fact]
    public void PaddingNeverProducesOverlappingHunks()
    {
        // 30 and 45 are further apart than the merge distance but their
        // context would overlap; two windows showing the same lines is wrong
        var hunks = ChangeBoard.HunksOf(Change("a.cs", [30, 45]), 100, context: 10, merge: 5);

        foreach (var (a, b) in hunks.Zip(hunks.Skip(1)))
            Assert.True(b.From > a.To, $"hunk {b.From}-{b.To} overlaps {a.From}-{a.To}");
    }

    [Fact]
    public void DeletionsCountAsSomethingToLookAt()
    {
        // a deletion adds no line, but where it was taken from is exactly what
        // a reviewer wants to see
        var hunk = Assert.Single(ChangeBoard.HunksOf(Change("a.cs", [], [40]), 100));

        Assert.InRange(40, hunk.From, hunk.To);
    }

    [Fact]
    public void AdditionsAndDeletionsAreConsideredTogether()
    {
        var hunks = ChangeBoard.HunksOf(Change("a.cs", [10], [12]), 100, context: 1, merge: 10);

        var hunk = Assert.Single(hunks);
        Assert.InRange(10, hunk.From, hunk.To);
        Assert.InRange(12, hunk.From, hunk.To);
    }

    [Fact]
    public void HunksNeverRunOffEitherEndOfTheFile()
    {
        var atStart = Assert.Single(ChangeBoard.HunksOf(Change("a.cs", [0]), 20));
        Assert.Equal(0, atStart.From);

        var atEnd = Assert.Single(ChangeBoard.HunksOf(Change("a.cs", [19]), 20));
        Assert.Equal(19, atEnd.To);
    }

    [Fact]
    public void HunksComeOutInFileOrder()
    {
        var hunks = ChangeBoard.HunksOf(Change("a.cs", [900, 20, 450]), 1000);

        Assert.Equal(hunks.OrderBy(h => h.From), hunks);
    }

    [Fact]
    public void UnsortedInputIsHandled()
    {
        // git hands them over in order today, but a hunk list that depends on
        // that is a trap
        var sorted = ChangeBoard.HunksOf(Change("a.cs", [10, 11, 12]), 100);
        var jumbled = ChangeBoard.HunksOf(Change("a.cs", [12, 10, 11]), 100);

        Assert.Equal(sorted, jumbled);
    }

    // --- the board -------------------------------------------------------

    static ChangeSet SetOf(params FileChange[] files)
    {
        var set = new ChangeSet { Label = "a commit", Files = files.ToList() };
        set.Index();
        return set;
    }

    [Fact]
    public void OneWindowPerFileHoweverManyHunksItHas()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        var board = ChangeBoard.Build(SetOf(Change(SampleRepo.LongFile, [10, 200])), scene, "a commit");

        var item = Assert.Single(board.Items);
        Assert.Equal("file", item.Kind);
        Assert.Equal(SampleRepo.LongFile, item.File);
    }

    /// <summary>a window used to be cut to the hunk plus three lines, which
    /// is what a diff shows and throws away the one thing this view has that
    /// a diff does not: the rest of the file the change landed in.</summary>
    [Fact]
    public void AWindowShowsTheWholeFile()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        var item = Assert.Single(ChangeBoard.Build(
            SetOf(Change(SampleRepo.LongFile, [100])), scene, "a commit").Items);

        Assert.Equal(0, item.Line);
        Assert.Equal(SampleRepo.LongFileLines - 1, item.EndLine);
    }

    /// <summary>and the view opens looking at the change rather than at line
    /// one, which with whole files is a long way from it.</summary>
    [Fact]
    public void TheViewKnowsWhereTheFirstChangeIs()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        var set = SetOf(Change(SampleRepo.LongFile, [200]));
        var board = ChangeBoard.Build(set, scene, "a commit");
        var item = Assert.Single(board.Items);

        var spot = ChangeBoard.FirstChange(board, set, scene);

        Assert.NotNull(spot);
        // well below the top of the window, and inside it
        Assert.True(spot!.Value.Y > item.Y + Scene.WinHeadH);
        Assert.True(spot.Value.Y < item.Y + scene.ItemHeight(item));
    }

    [Fact]
    public void AChangeThatTouchedNoLinesHasNowhereToLookFirst()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        var set = SetOf(Change(SampleRepo.LongFile, []));
        var board = ChangeBoard.Build(set, scene, "a commit");

        Assert.Null(ChangeBoard.FirstChange(board, set, scene));
    }

    [Fact]
    public void AChangeWithNowhereToLandIsLeftOut()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        // a deleted file, or one the scanner skips, has no card on the map
        var board = ChangeBoard.Build(SetOf(Change("gone/away.cs", [1, 2])), scene, "a commit");

        Assert.Empty(board.Items);
    }

    [Fact]
    public void FilesThatDoLandAreKeptEvenWhenOthersDoNot()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        var board = ChangeBoard.Build(SetOf(
            Change("gone/away.cs", [1]),
            Change(SampleRepo.LongFile, [50])), scene, "a commit");

        Assert.Single(board.Items);
        Assert.Equal(SampleRepo.LongFile, board.Items[0].File);
    }

    [Fact]
    public void TheBiggestChurnComesFirst()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        // Build trusts the ChangeSet's own order, which git sorts by churn
        var board = ChangeBoard.Build(SetOf(
            Change(SampleRepo.LongFile, [10, 11, 12]),
            Change("app/Program.cs", [1])), scene, "a commit");

        Assert.Equal(SampleRepo.LongFile, board.Items[0].File);
    }

    [Fact]
    public void WindowsDoNotOverlap()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        var board = ChangeBoard.Build(SetOf(
            Change(SampleRepo.LongFile, [10, 60, 110, 160, 210, 260]),
            Change("app/Program.cs", [1]),
            Change("app/ui/Panel.cs", [2])), scene, "a commit");

        var boxes = board.Items
            .Select(i => (i.X, i.Y, i.W, H: scene.ItemHeight(i)))
            .ToList();

        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                var (a, b) = (boxes[i], boxes[j]);
                bool apart = a.X + a.W <= b.X || b.X + b.W <= a.X ||
                             a.Y + a.H <= b.Y || b.Y + b.H <= a.Y;
                Assert.True(apart, $"window {i} overlaps window {j}");
            }
    }

    [Fact]
    public void TheBoardCarriesTheCommitsLabel()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        var board = ChangeBoard.Build(SetOf(Change(SampleRepo.LongFile, [10])), scene, "Use the panel.");

        Assert.Equal("Use the panel.", board.Name);
    }

    [Fact]
    public void ARewriteTheWorldCommitIsCappedRatherThanUnrolled()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        // a hunk every 20 lines for 300 lines, over and over
        var everywhere = Enumerable.Range(0, 15).Select(i => i * 20).ToArray();
        var board = ChangeBoard.Build(
            SetOf(Enumerable.Repeat(0, 20).Select(_ => Change(SampleRepo.LongFile, everywhere)).ToArray()),
            scene, "a commit");

        Assert.InRange(board.Items.Count, 1, 60);
    }

    [Fact]
    public void AnEmptyChangeSetBuildsAnEmptyBoard()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        Assert.Empty(ChangeBoard.Build(SetOf(), scene, "nothing").Items);
    }

    // --- against a real commit -------------------------------------------

    [Fact]
    public void ARealCommitGathersOntoABoard()
    {
        using var git = new GitFixture();
        using var review = GitReview.Open(git.Path)!;
        using var scene = new Scene(Scanner.Build(git.Path));

        var pr = review.MergedPrs()[0];
        var set = review.Whole(pr)!;

        var board = ChangeBoard.Build(set, scene, pr.Label);

        Assert.NotEmpty(board.Items);
        Assert.All(board.Items, i => Assert.True(scene.IndexOfPath(i.File!) >= 0));
        Assert.Equal(set.Files.Select(f => f.Path).Order(),
                     board.Items.Select(i => i.File!).Distinct().Order());
    }

    [Fact]
    public void EveryWindowOfARealCommitHasHeight()
    {
        using var git = new GitFixture();
        using var review = GitReview.Open(git.Path)!;
        using var scene = new Scene(Scanner.Build(git.Path));

        var board = ChangeBoard.Build(review.Whole(review.MergedPrs()[0])!, scene, "pr");

        Assert.All(board.Items, i => Assert.True(scene.ItemHeight(i) > 0, $"{i.File} has no height"));
    }

    [Fact]
    public void TheGatheredBoardBoundsTheCamera()
    {
        using var git = new GitFixture();
        using var review = GitReview.Open(git.Path)!;
        using var scene = new Scene(Scanner.Build(git.Path));

        scene.ActiveBoard = ChangeBoard.Build(review.Whole(review.MergedPrs()[0])!, scene, "pr");
        scene.CamS = 1f;

        var b = scene.ContentBounds();
        Assert.True(b.Width > 0 && b.Height > 0);

        scene.CamX = 400_000;
        scene.ClampCamera(1400, 900);
        Assert.True(scene.CamX < 400_000, "the gathered board pans away forever");

        scene.ActiveBoard = null;
    }
}

namespace Atlas.Tests;

/// <summary>review mode, against a git repository built for the purpose.
///
/// The old --gittest could only run against whatever repo it was handed, and
/// printed SKIP when that repo had no merge commits - which is the case for
/// Atlas itself, so in practice nothing here was ever checked. Building the
/// history means the pull request path actually runs.</summary>
public class GitReviewTests : IClassFixture<GitFixture>
{
    readonly GitFixture _git;

    public GitReviewTests(GitFixture git) => _git = git;

    GitReview Open() => GitReview.Open(_git.Path)!;

    [Fact]
    public void OpeningSomethingThatIsNotARepoReturnsNull()
    {
        using var dir = new TempDir();
        Assert.Null(GitReview.Open(dir.Path));
    }

    [Fact]
    public void OpeningARepoWorks()
    {
        using var git = Open();
        Assert.Equal("main", git.HeadName);
    }

    [Fact]
    public void TheBaseBranchIsTheUsualName()
    {
        using var git = Open();
        Assert.Equal("main", git.BaseBranch());
    }

    // --- pull requests ---------------------------------------------------

    [Fact]
    public void AMergeCommitIsReadAsAPullRequest()
    {
        using var git = Open();
        var pr = Assert.Single(git.MergedPrs());

        Assert.StartsWith("#7", pr.Label);
        Assert.Equal("merged", pr.Detail);
    }

    [Fact]
    public void ThePullRequestTitleComesFromTheBodyNotTheBranchName()
    {
        using var git = Open();
        // github puts the branch on the subject line and the real title below
        Assert.Contains("Add a panel to the app", git.MergedPrs()[0].Label);
    }

    [Fact]
    public void ThePullRequestsBaseAndHeadAreTheMergeCommitsTwoParents()
    {
        using var git = Open();
        var pr = git.MergedPrs()[0];

        Assert.Equal(_git.FeatureTipSha, pr.HeadSha);
        Assert.Equal(40, pr.BaseSha.Length);
        Assert.NotEqual(pr.BaseSha, pr.HeadSha);
    }

    [Fact]
    public void APullRequestListsItsCommitsOldestFirst()
    {
        using var git = Open();
        var commits = git.CommitsOf(git.MergedPrs()[0]);

        Assert.Equal(2, commits.Count);
        Assert.Equal("Add the panel.", commits[0].Subject);
        Assert.Equal("Use the panel.", commits[1].Subject);
        Assert.True(commits[0].When <= commits[1].When, "oldest first is the order a reviewer walks");
    }

    [Fact]
    public void EveryCommitCarriesAShortShaAndAnAuthor()
    {
        using var git = Open();
        foreach (var c in git.CommitsOf(git.MergedPrs()[0]))
        {
            Assert.Equal(7, c.Short.Length);
            Assert.StartsWith(c.Short, c.Sha);
            Assert.Equal("Test", c.Author);
        }
    }

    // --- branches --------------------------------------------------------

    [Fact]
    public void AnUnmergedBranchIsOfferedForReview()
    {
        using var git = Open();
        var wip = git.Branches().Single(t => t.Label == "wip");

        Assert.Equal(_git.WipTipSha, wip.HeadSha);
        Assert.Contains("1 commit ahead of main", wip.Detail);
    }

    /// <summary>a branch with work of its own is what you came to review, so
    /// it is not buried under branches that have none.</summary>
    [Fact]
    public void ABranchWithWorkComesFirst()
    {
        using var git = Open();
        Assert.Equal("wip", git.Branches()[0].Label);
    }

    [Fact]
    public void ABranchIsMeasuredFromItsMergeBase()
    {
        using var git = Open();
        var wip = git.Branches()[0];

        // wip branched off the merge commit, so that is its base
        Assert.Equal(_git.PrMergeSha, wip.BaseSha);
    }

    [Fact]
    public void BranchesAndPullRequestsAreSeparateLists()
    {
        using var git = Open();

        Assert.All(git.Branches(), t => Assert.NotEqual("merged", t.Detail));
        Assert.All(git.MergedPrs(), t => Assert.Equal("merged", t.Detail));
    }

    /// <summary>the base branch has no change set - that is what being the
    /// base means - and leaving it out made the panel look broken: you open
    /// the list of branches and the branch you are on is not in it.</summary>
    [Fact]
    public void EveryBranchIsListed()
    {
        using var git = Open();
        var labels = git.Branches().Select(t => t.Label).ToList();

        Assert.Contains("main", labels);
        Assert.Contains("wip", labels);
        Assert.Contains("feature", labels);
    }

    [Fact]
    public void TheBaseBranchLeadsTheOnesWithNoWorkOfTheirOwn()
    {
        using var git = Open();
        var rest = git.Branches().SkipWhile(t => t.Detail.Contains("ahead of")).ToList();

        Assert.Equal("main", rest[0].Label);
        Assert.Contains("the base branch", rest[0].Detail);
    }

    /// <summary>a branch with nothing ahead of the base cannot be shown as a
    /// difference from it, so it is shown as its own recent history - and the
    /// detail line has to say so rather than implying a change set.</summary>
    [Fact]
    public void AMergedBranchIsShownAsItsOwnHistory()
    {
        using var git = Open();
        var feature = git.Branches().Single(t => t.Label == "feature");

        Assert.Contains("nothing ahead of main", feature.Detail);
        Assert.Contains("last", feature.Detail);
        Assert.Equal(_git.FeatureTipSha, feature.HeadSha);
    }

    [Fact]
    public void AHistoryRangeIsShorterThanTheBranchWhenTheBranchIsShort()
    {
        using var git = Open();
        var main = git.Branches().Single(t => t.Label == "main");

        // the fixture has four commits, not twenty five, and the detail must
        // say what is really in the range rather than the span it asked for
        Assert.DoesNotContain($"last {GitReview.RecentSpan} ", main.Detail);
        Assert.NotEmpty(git.CommitsOf(main));
    }

    [Fact]
    public void EveryTargetCarriesFullShas()
    {
        using var git = Open();
        foreach (var t in git.Branches().Concat(git.MergedPrs()))
        {
            Assert.Equal(40, t.BaseSha.Length);
            Assert.Equal(40, t.HeadSha.Length);
            Assert.NotEqual(t.BaseSha, t.HeadSha);
        }
    }

    // --- diffs -----------------------------------------------------------

    [Fact]
    public void AWholePullRequestDiffsToTheFilesItTouched()
    {
        using var git = Open();
        var set = git.Whole(git.MergedPrs()[0])!;

        Assert.Equal(["app/Panel.cs", "app/Program.cs"], set.Files.Select(f => f.Path).Order());
        Assert.True(set.TotalAdded > 0);
    }

    [Fact]
    public void PathsUseForwardSlashesSoTheyMatchTheScan()
    {
        using var git = Open();
        var set = git.Whole(git.MergedPrs()[0])!;

        Assert.All(set.Files, f => Assert.False(f.Path.Contains((char)92), f.Path));
    }

    [Fact]
    public void FilesAreSortedByChurnSoTheCameraHeadsSomewhereUseful()
    {
        using var git = Open();
        var files = git.Whole(git.MergedPrs()[0])!.Files;

        var churn = files.Select(f => f.Added + f.Removed).ToList();
        Assert.Equal(churn.OrderByDescending(c => c), churn);
    }

    [Fact]
    public void ThePathIndexCoversEveryChangedFile()
    {
        using var git = Open();
        var set = git.Whole(git.MergedPrs()[0])!;

        Assert.Equal(set.Files.Count, set.ByPath.Count);
        foreach (var f in set.Files)
            Assert.Same(f, set.ByPath[f.Path]);
    }

    [Fact]
    public void EveryAddedLineHasALineNumberAndEveryRemovalAPosition()
    {
        using var git = Open();
        foreach (var f in git.Whole(git.MergedPrs()[0])!.Files)
        {
            Assert.Equal(f.Added, f.AddedLines.Count);
            Assert.Equal(f.Removed, f.RemovedAt.Count);
        }
    }

    [Fact]
    public void AddedLineNumbersAreZeroBasedAndInOrder()
    {
        using var git = Open();
        foreach (var f in git.Whole(git.MergedPrs()[0])!.Files)
        {
            Assert.All(f.AddedLines, l => Assert.True(l >= 0, $"line {l} is negative"));
            Assert.Equal(f.AddedLines.OrderBy(l => l), f.AddedLines);
        }
    }

    [Fact]
    public void ASingleCommitDiffsAgainstItsParent()
    {
        using var git = Open();
        var commits = git.CommitsOf(git.MergedPrs()[0]);

        var first = git.OfCommit(commits[0])!;
        Assert.Equal("app/Panel.cs", Assert.Single(first.Files).Path);

        var second = git.OfCommit(commits[1])!;
        Assert.Equal("app/Program.cs", Assert.Single(second.Files).Path);
    }

    [Fact]
    public void DiffingAnUnknownShaIsNullRatherThanAThrow()
    {
        using var git = Open();
        Assert.Null(git.Diff("0".PadLeft(40, '0'), "1".PadLeft(40, '1'), "nowhere"));
    }

    // --- snapshots -------------------------------------------------------

    [Fact]
    public void ASnapshotIsTheTreeAsItWasAtThatCommit()
    {
        using var git = Open();
        var tree = git.Snapshot(_git.FeatureTipSha, p => Scanner.Wanted(p))!;

        Assert.Equal(["app/Panel.cs", "app/Program.cs"], tree.Keys.Order());
        Assert.Contains("class Panel", string.Join("\n", tree["app/Panel.cs"]));
    }

    [Fact]
    public void ASnapshotIsFilteredTheSameWayAFolderWalkIs()
    {
        using var git = Open();
        // nothing is wanted, so nothing comes back - the filter is applied
        Assert.Empty(git.Snapshot(_git.FeatureTipSha, _ => false)!);
    }

    [Fact]
    public void ASnapshotBuildsAMapTheChangesCanLandOn()
    {
        using var git = Open();
        var pr = git.MergedPrs()[0];
        var tree = git.Snapshot(pr.HeadSha, p => Scanner.Wanted(p))!;
        var scan = Scanner.BuildFrom(_git.Path, tree);
        using var scene = new Scene(scan);

        var set = git.Whole(pr)!;
        int placed = set.Files.Count(f => scene.IndexOfPath(f.Path) >= 0);

        Assert.Equal(set.Files.Count, placed);
    }

    [Fact]
    public void TheWorkingTreeScanAlsoHoldsThePullRequestsFiles()
    {
        using var git = Open();
        using var scene = new Scene(Scanner.Build(_git.Path));
        var set = git.Whole(git.MergedPrs()[0])!;

        Assert.All(set.Files, f => Assert.True(scene.IndexOfPath(f.Path) >= 0, $"{f.Path} is not on the map"));
    }

    [Fact]
    public void RecentCommitsComeBackNewestFirst()
    {
        using var git = Open();
        var recent = git.RecentCommits(10);

        Assert.NotEmpty(recent);
        Assert.Equal(recent.OrderByDescending(c => c.When).Select(c => c.Sha), recent.Select(c => c.Sha));
    }
}

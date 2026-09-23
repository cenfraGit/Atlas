namespace Atlas.Tests;

/// <summary>open pull requests in the review panel.
///
/// The panel only ever listed merged ones, because it finds pull requests
/// by their merge commits and an open one has none. They come from GitHub
/// now, through gh; what can be tested without the network is reading what
/// gh says and turning one into something to review.</summary>
public class OpenPrTests
{
    const string Json = """
        [{"baseRefName":"develop","headRefName":"feature/tabs","headRefOid":"abc123","number":42,"title":"Add tabs"},
         {"baseRefName":"main","headRefName":"fix/crash","headRefOid":"def456","number":43,"title":"Fix the crash"}]
        """;

    [Fact]
    public void WhatGhSaysIsRead()
    {
        var prs = GitReview.ParseOpenPrs(Json);

        Assert.Equal(2, prs.Count);
        Assert.Equal(new OpenPr(42, "Add tabs", "feature/tabs", "develop", "abc123"), prs[0]);
    }

    [Fact]
    public void NonsenseFromGhIsNoPullRequestsRatherThanACrash()
    {
        Assert.Empty(GitReview.ParseOpenPrs("not json"));
        Assert.Empty(GitReview.ParseOpenPrs("{}"));
    }

    /// <summary>an open pull request is reviewed from where its branch left
    /// its base, the same as a branch - so its own commits and nothing else.</summary>
    [Fact]
    public void AnOpenPullRequestIsReviewedFromWhereItLeftItsBase()
    {
        using var git = new GitFixture();
        using var review = GitReview.Open(git.Path)!;

        var target = review.TargetFor(new OpenPr(7, "Start something", "wip", "main", git.WipTipSha))!;

        Assert.Equal(git.WipTipSha, target.HeadSha);
        Assert.Equal(git.PrMergeSha, target.BaseSha);            // where wip branched off main
        Assert.Equal("Start something.", Assert.Single(review.CommitsOf(target)).Subject);
        Assert.Contains("#7", target.Label);
    }

    /// <summary>one whose commits are not here yet cannot be reviewed until
    /// they are fetched, and says so by being null rather than by guessing.</summary>
    [Fact]
    public void OneWhoseCommitsAreNotHereNeedsFetching()
    {
        using var git = new GitFixture();
        using var review = GitReview.Open(git.Path)!;

        Assert.Null(review.TargetFor(new OpenPr(8, "Elsewhere", "far", "main", new string('a', 40))));
    }

    [Fact]
    public void AListRowKnowsItIsOpenBeforeItIsResolved()
    {
        var row = GitReview.RowFor(new OpenPr(42, "Add tabs", "feature/tabs", "develop", "abc123"));

        Assert.NotNull(row.Open);
        Assert.Equal("", row.BaseSha);
        Assert.Contains("open", row.Detail);
    }
}

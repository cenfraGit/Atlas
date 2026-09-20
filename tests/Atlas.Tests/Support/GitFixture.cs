using LibGit2Sharp;

namespace Atlas.Tests;

/// <summary>a real git repository with a merged pull request and an unmerged
/// branch. The old --gittest could only run against whatever repo it was
/// pointed at, and skipped entirely when that repo had no merge commits -
/// which is the case for Atlas itself. Building the history means review mode
/// is actually exercised.</summary>
public sealed class GitFixture : IDisposable
{
    readonly TempDir _dir;

    public string Path => _dir.Path;
    public string PrMergeSha { get; }
    public string FeatureTipSha { get; }
    public string WipTipSha { get; }

    static readonly Signature Who = new("Test", "test@example.com", new DateTimeOffset(2024, 1, 1, 9, 0, 0, TimeSpan.Zero));

    static Signature At(int minutes) => new(Who.Name, Who.Email, Who.When.AddMinutes(minutes));

    public GitFixture()
    {
        _dir = new TempDir("atlas_git");
        Repository.Init(_dir.Path);
        using var repo = new Repository(_dir.Path);

        // main: one file
        _dir.File("app/Program.cs", "namespace Demo;\n\nclass Program\n{\n    static void Main() { }\n}\n");
        var first = Commit(repo, "Initial commit.", 0);
        // init.defaultBranch is whatever the machine has it set to, so the
        // first branch is renamed rather than created
        if (repo.Head.FriendlyName != "main")
            repo.Branches.Rename(repo.Branches[repo.Head.FriendlyName], "main");

        // feature: two commits that add a file and edit the first
        var feature = repo.CreateBranch("feature", first);
        Checkout(repo, feature);
        _dir.File("app/Panel.cs", "namespace Demo;\n\nclass Panel\n{\n    public int W;\n}\n");
        Commit(repo, "Add the panel.", 10);
        _dir.File("app/Program.cs", "namespace Demo;\n\nclass Program\n{\n    static void Main() { new Panel(); }\n}\n");
        var featureTip = Commit(repo, "Use the panel.", 20);
        FeatureTipSha = featureTip.Sha;

        // merge it into main the way github writes it, so MergedPrs can read
        // the base and the head straight off the commit's two parents
        Checkout(repo, repo.Branches["main"]);
        var merge = repo.ObjectDatabase.CreateCommit(
            At(30), At(30),
            "Merge pull request #7 from demo/feature\n\nAdd a panel to the app\n",
            featureTip.Tree, [first, featureTip], prettifyMessage: false);
        repo.Refs.UpdateTarget(repo.Refs["refs/heads/main"], merge.Sha);
        repo.Refs.UpdateTarget(repo.Refs.Head, "refs/heads/main");
        repo.Reset(ResetMode.Hard, merge);
        PrMergeSha = merge.Sha;

        // an unmerged branch, so Branches() has something to report
        var wip = repo.CreateBranch("wip", merge);
        Checkout(repo, wip);
        _dir.File("app/Wip.cs", "namespace Demo;\n\nclass Wip { }\n");
        WipTipSha = Commit(repo, "Start something.", 40).Sha;

        Checkout(repo, repo.Branches["main"]);
    }

    static LibGit2Sharp.Commit Commit(Repository repo, string message, int minutes)
    {
        Commands.Stage(repo, "*");
        return repo.Commit(message, At(minutes), At(minutes),
            new CommitOptions { AllowEmptyCommit = true });
    }

    static void Checkout(Repository repo, Branch b) => Commands.Checkout(repo, b);

    public void Dispose() => _dir.Dispose();
}

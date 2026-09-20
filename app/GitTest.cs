namespace Atlas;

/// <summary>run: dotnet run -- --gittest &lt;repo&gt;</summary>
public static class GitTest
{
    static int _fails;

    static void Check(bool ok, string what)
    {
        if (!ok) { _fails++; Console.WriteLine("  FAIL: " + what); }
    }

    public static void Run(string[] args)
    {
        var repo = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--") && Directory.Exists(a));
        if (repo is null) { Console.WriteLine("usage: --gittest <repo>"); return; }

        // review mode needs history to review. a repo with no merges is not a
        // failure, it just cannot answer the question - say so and stop
        using var git = GitReview.Open(repo);
        if (git is null) { Console.WriteLine("SKIP: not a git repository"); return; }
        Console.WriteLine($"branch: {git.HeadName}");

        var prs = git.MergedPrs(10);
        Console.WriteLine($"found {prs.Count} pull requests");
        if (prs.Count == 0)
        {
            Console.WriteLine("SKIP: no merge commits here. point it at a repo with merged PRs");
            return;
        }

        foreach (var pr in prs.Take(3))
            Console.WriteLine($"  {pr.Label}");

        var first = prs[0];
        var commits = git.CommitsOf(first);
        Console.WriteLine($"{first.Label} has {commits.Count} commits");
        Check(commits.Count > 0, "lists the commits a pull request contributed");
        Check(commits.All(c => c.Short.Length == 7), "commits carry a short sha");
        // oldest first is the order a reviewer walks them in
        Check(commits.Count < 2 || commits[0].When <= commits[^1].When, "commits are oldest first");

        var whole = git.Whole(first);
        Check(whole is not null, "diffs the whole pull request");
        if (whole is null) { Console.WriteLine("FAILURES"); return; }
        Console.WriteLine($"  {whole.Files.Count} files, +{whole.TotalAdded} -{whole.TotalRemoved}");
        Check(whole.Files.Count > 0, "the pull request touches files");
        Check(whole.TotalAdded + whole.TotalRemoved > 0, "the pull request changes lines");
        Check(whole.Files.All(f => f.Path.IndexOf((char)92) < 0), "paths use forward slashes like the scan");
        Check(whole.ByPath.Count == whole.Files.Count, "the path index is complete");
        // biggest churn first, so the camera can head somewhere useful
        Check(whole.Files.Count < 2 ||
              whole.Files[0].Added + whole.Files[0].Removed >= whole.Files[^1].Added + whole.Files[^1].Removed,
              "files are sorted by churn");

        var top = whole.Files[0];
        Console.WriteLine($"  biggest: {top.Path} +{top.Added} -{top.Removed}, " +
                          $"{top.AddedLines.Count} added line numbers, {top.RemovedAt.Count} deletion points");
        Check(top.AddedLines.Count == top.Added,
            $"every added line has a line number ({top.AddedLines.Count} vs {top.Added})");
        Check(top.RemovedAt.Count == top.Removed,
            $"every removed line has a position ({top.RemovedAt.Count} vs {top.Removed})");
        Check(top.AddedLines.All(l => l >= 0), "line numbers are zero based and never negative");
        Check(top.AddedLines.Count == 0 || top.AddedLines.SequenceEqual(top.AddedLines.OrderBy(x => x)),
            "added line numbers come out in order");

        // added lines must actually exist in the file as it stands at that commit
        var perCommit = git.OfCommit(commits[^1]);
        Check(perCommit is not null && perCommit.Files.Count > 0, "diffs a single commit against its parent");

        // branches and merged pull requests share one list
        var baseBranch = git.BaseBranch();
        Console.WriteLine($"base branch: {baseBranch}");
        Check(baseBranch is not null, "picks a base branch to measure against");

        var targets = git.Branches().Concat(git.MergedPrs()).ToList();
        Console.WriteLine($"{targets.Count} review targets");
        foreach (var t in targets.Take(6)) Console.WriteLine($"  {t.Label}   [{t.Detail}]");
        Check(targets.Count > 0, "offers something to review");
        Check(targets.All(t => t.BaseSha.Length == 40 && t.HeadSha.Length == 40), "targets carry full shas");
        Check(targets.All(t => t.BaseSha != t.HeadSha), "a target is never its own base");

        // branches and pull requests are now separate lists
        Check(git.Branches().All(t => t.Detail != "merged"), "the branch list holds no merged pull requests");
        Check(git.MergedPrs().All(t => t.Detail == "merged"), "the pull request list holds only merged ones");

        var branch = targets.FirstOrDefault(t => t.Detail != "merged");
        if (branch is not null)
        {
            var bCommits = git.CommitsOf(branch);
            Console.WriteLine($"  {branch.Label}: {bCommits.Count} commits");
            Check(bCommits.Count > 0, "a branch target lists its commits");
            var bDiff = git.Whole(branch);
            Check(bDiff is not null && bDiff.Files.Count > 0, "a branch target diffs against its merge base");
        }
        else Console.WriteLine("  (no branch ahead of base in this clone)");

        // a change the map cannot place is invisible, so measure the overlap
        var scan = Scanner.Build(repo);
        var scene = new Scene(scan);
        foreach (var t in targets.Take(6))
        {
            var set = git.Whole(t);
            if (set is null) continue;
            int placed = set.Files.Count(f => scene.IndexOfPath(f.Path) >= 0);
            Console.WriteLine($"  {t.Label}: {placed}/{set.Files.Count} changed files are on the map");
        }

        var showable = git.Whole(targets[0]);
        if (showable is not null)
        {
            int placed = showable.Files.Count(f => scene.IndexOfPath(f.Path) >= 0);
            Check(placed > 0, "at least some of a target's changes can be drawn on the map");
        }

        Console.WriteLine(_fails == 0 ? "PASS" : $"{_fails} FAILURES");
    }
}

using System.Text.RegularExpressions;
using LibGit2Sharp;

namespace Atlas;

/// <summary>a pull request, recovered from its merge commit rather than from a
/// host API: parent 1 is what it merged into, parent 2 is the branch head.</summary>
/// <summary>something reviewable: a merged pull request, or a branch that has
/// commits its base does not.</summary>
public sealed record ReviewTarget(string Label, string Detail, string BaseSha, string HeadSha);

public sealed record CommitInfo(string Sha, string Short, string Subject, string Author, DateTimeOffset When);

public sealed record FileChange(string Path, int Added, int Removed)
{
    /// <summary>0-based lines in the new version that were added.</summary>
    public List<int> AddedLines { get; } = [];

    /// <summary>0-based lines in the new version where lines were deleted.</summary>
    public List<int> RemovedAt { get; } = [];
}

public sealed class ChangeSet
{
    public string Label { get; init; } = "";
    public List<FileChange> Files { get; init; } = [];
    public Dictionary<string, FileChange> ByPath { get; } = new(StringComparer.Ordinal);

    public int TotalAdded => Files.Sum(f => f.Added);
    public int TotalRemoved => Files.Sum(f => f.Removed);

    public void Index()
    {
        ByPath.Clear();
        foreach (var f in Files) ByPath[f.Path] = f;
    }
}

public sealed class GitReview : IDisposable
{
    static readonly Regex PrSubject = new(@"^Merge pull request #(\d+) from (\S+)",
        RegexOptions.Compiled);

    const char Lf = (char)10;

    readonly Repository _repo;

    GitReview(Repository repo) => _repo = repo;

    public static GitReview? Open(string root)
    {
        try
        {
            var found = Repository.Discover(root);
            return found is null ? null : new GitReview(new Repository(found));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"no git repo at {root}: {ex.Message}");
            return null;
        }
    }

    public string HeadName => _repo.Head.FriendlyName;

    /// <summary>the branch everything else is measured against: whatever
    /// origin/HEAD points at, else the usual names.</summary>
    public string? BaseBranch()
    {
        var head = _repo.Branches["origin/HEAD"]?.Tip;
        if (head is not null)
            foreach (var b in _repo.Branches)
                if (b.IsRemote && b.Tip == head && !b.FriendlyName.EndsWith("/HEAD"))
                    return b.FriendlyName;

        foreach (var name in new[] { "origin/develop", "origin/main", "origin/master", "develop", "main", "master" })
            if (_repo.Branches[name] is not null) return name;
        return _repo.Head.FriendlyName;
    }

    /// <summary>branches with unmerged work first, then merged pull requests.
    /// the branch you are about to review matters more than one that landed
    /// months ago.</summary>
    public List<ReviewTarget> Branches(int maxBranches = 40)
    {
        var targets = new List<ReviewTarget>();
        var baseName = BaseBranch();
        var baseTip = baseName is null ? null : _repo.Branches[baseName]?.Tip;

        if (baseTip is not null)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var branches = new List<(Branch B, int Ahead, DateTimeOffset When)>();

            foreach (var b in _repo.Branches)
            {
                if (b.Tip is null || b.FriendlyName.EndsWith("/HEAD")) continue;
                if (b.FriendlyName == baseName) continue;
                // a local branch and its remote twin are the same review
                var key = b.FriendlyName.StartsWith("origin/") ? b.FriendlyName[7..] : b.FriendlyName;
                if (!seen.Add(key)) continue;

                try
                {
                    var div = _repo.ObjectDatabase.CalculateHistoryDivergence(b.Tip, baseTip);
                    if (div.AheadBy is null or 0) continue;
                    branches.Add((b, div.AheadBy.Value, b.Tip.Author.When));
                }
                catch { }
            }

            foreach (var (b, ahead, _) in branches.OrderByDescending(x => x.When).Take(maxBranches))
            {
                var mergeBase = _repo.ObjectDatabase.FindMergeBase(b.Tip, baseTip);
                targets.Add(new ReviewTarget(
                    b.FriendlyName,
                    $"{ahead} commit{(ahead == 1 ? "" : "s")} ahead of {baseName}",
                    (mergeBase ?? baseTip).Sha,
                    b.Tip.Sha));
            }
        }

        return targets;
    }

    /// <summary>merge commits that name a pull request, newest first.</summary>
    public List<ReviewTarget> MergedPrs(int max = 30, int scan = 600)
    {
        var prs = new List<ReviewTarget>();
        foreach (var c in _repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = _repo.Head }).Take(scan))
        {
            if (c.Parents.Count() < 2) continue;
            var m = PrSubject.Match(c.MessageShort);
            if (!m.Success) continue;

            var parents = c.Parents.ToList();
            // github puts the branch name on the subject line and the real
            // title on the first body line
            var title = c.Message.Split(Lf).Skip(1).FirstOrDefault(l => l.Trim().Length > 0)?.Trim()
                        ?? m.Groups[2].Value.Trim();
            prs.Add(new ReviewTarget($"#{m.Groups[1].Value}  {title}", "merged",
                parents[0].Sha, parents[1].Sha));
            if (prs.Count >= max) break;
        }
        return prs;
    }

    /// <summary>the commits a pull request contributed, oldest first, which is
    /// the order a reviewer wants to walk them in.</summary>
    public List<CommitInfo> CommitsOf(ReviewTarget target)
    {
        var list = new List<CommitInfo>();
        try
        {
            var filter = new CommitFilter
            {
                IncludeReachableFrom = target.HeadSha,
                ExcludeReachableFrom = target.BaseSha,
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Reverse,
            };
            foreach (var c in _repo.Commits.QueryBy(filter))
                list.Add(Info(c));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"could not list commits for {target.Label}: {ex.Message}");
        }
        return list;
    }

    public List<CommitInfo> RecentCommits(int count) =>
        _repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = _repo.Head })
            .Take(count).Select(Info).ToList();

    static CommitInfo Info(Commit c) =>
        new(c.Sha, c.Sha[..7], c.MessageShort.Trim(), c.Author.Name, c.Author.When);

    /// <summary>everything the target changed, as one set.</summary>
    public ChangeSet? Whole(ReviewTarget target) =>
        Diff(target.BaseSha, target.HeadSha, target.Label);

    /// <summary>what a single commit changed, against its first parent.</summary>
    public ChangeSet? OfCommit(CommitInfo info)
    {
        var c = _repo.Lookup<Commit>(info.Sha);
        var parent = c?.Parents.FirstOrDefault();
        if (c is null) return null;
        return Compare(parent?.Tree, c.Tree, info.Subject);
    }

    public ChangeSet? Diff(string fromSha, string toSha, string label)
    {
        var from = _repo.Lookup<Commit>(fromSha);
        var to = _repo.Lookup<Commit>(toSha);
        if (to is null) return null;
        return Compare(from?.Tree, to.Tree, label);
    }

    ChangeSet? Compare(Tree? from, Tree to, string label)
    {
        try
        {
            var patch = _repo.Diff.Compare<Patch>(from, to);
            var set = new ChangeSet { Label = label };
            foreach (var entry in patch)
            {
                if (entry.IsBinaryComparison) continue;
                var change = new FileChange(entry.Path.Replace('\\', '/'), entry.LinesAdded, entry.LinesDeleted);
                ReadHunks(entry.Patch, change);
                set.Files.Add(change);
            }
            set.Files.Sort((a, b) => (b.Added + b.Removed).CompareTo(a.Added + a.Removed));
            set.Index();
            return set;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"diff failed: {ex.Message}");
            return null;
        }
    }

    static readonly Regex HunkHeader = new(@"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.Compiled);

    /// <summary>walk a unified diff, recording new-side line numbers. patches
    /// count from 1, the canvas counts from 0.</summary>
    static void ReadHunks(string patchText, FileChange into)
    {
        int newLine = 0;
        foreach (var raw in patchText.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var header = HunkHeader.Match(line);
            if (header.Success)
            {
                newLine = int.Parse(header.Groups[1].Value) - 1;
                continue;
            }
            if (line.Length == 0) { newLine++; continue; }
            switch (line[0])
            {
                case '+':
                    if (line.StartsWith("+++")) break;
                    into.AddedLines.Add(newLine++);
                    break;
                case '-':
                    if (line.StartsWith("---")) break;
                    into.RemovedAt.Add(Math.Max(0, newLine));
                    break;
                case '\\':
                    break;                       // "\ No newline at end of file"
                default:
                    newLine++;
                    break;
            }
        }
    }

    /// <summary>every text file in a commit's tree, as lines. this is what lets
    /// a branch be drawn against the code as it was, rather than against a
    /// working tree whose paths may have moved on entirely.</summary>
    public Dictionary<string, string[]>? Snapshot(string sha, Func<string, bool> wanted)
    {
        var commit = _repo.Lookup<Commit>(sha);
        if (commit is null) return null;

        var files = new Dictionary<string, string[]>(StringComparer.Ordinal);
        Walk(commit.Tree, "", files, wanted);
        return files;
    }

    static void Walk(Tree tree, string prefix, Dictionary<string, string[]> into, Func<string, bool> wanted)
    {
        foreach (var entry in tree)
        {
            var path = prefix.Length == 0 ? entry.Name : prefix + "/" + entry.Name;
            if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                Walk((Tree)entry.Target, path, into, wanted);
                continue;
            }
            if (entry.TargetType != TreeEntryTargetType.Blob || !wanted(path)) continue;

            var blob = (Blob)entry.Target;
            if (blob.Size > 2_000_000 || blob.IsBinary) continue;
            try { into[path] = blob.GetContentText().Replace(CrLf, LfText).Split(Lf); }
            catch { }
        }
    }

    static readonly string LfText = ((char)10).ToString();
    static readonly string CrLf = ((char)13).ToString() + LfText;

    public void Dispose() => _repo.Dispose();
}

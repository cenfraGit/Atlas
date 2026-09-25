using System.Text.RegularExpressions;
using LibGit2Sharp;

namespace Atlas;

/// <summary>a pull request, recovered from its merge commit rather than from a
/// host API: parent 1 is what it merged into, parent 2 is the branch head.</summary>
/// <summary>something reviewable: a merged pull request, or a branch that has
/// commits its base does not.</summary>
public sealed record ReviewTarget(string Label, string Detail, string BaseSha, string HeadSha)
{
    /// <summary>an open pull request, whose base is not known until it is
    /// opened: its commits may not have been fetched yet.</summary>
    public OpenPr? Open { get; init; }
}

/// <summary>an open pull request, as GitHub reports it.</summary>
public sealed record OpenPr(int Number, string Title, string Head, string Base, string HeadSha);

public sealed record CommitInfo(string Sha, string Short, string Subject, string Author, DateTimeOffset When);

public sealed record FileChange(string Path, int Added, int Removed)
{
    /// <summary>0-based lines in the new version that were added.</summary>
    public List<int> AddedLines { get; } = [];

    /// <summary>0-based lines in the new version where lines were deleted.</summary>
    public List<int> RemovedAt { get; } = [];

    /// <summary>the deleted lines themselves, a block per run of them. The
    /// map only has room for where they were; the change view shows them.</summary>
    public List<RemovedBlock> RemovedText { get; } = [];

    /// <summary>the change deleted the file. Asked of git rather than guessed
    /// from the file not being on the map: a file can be off the map because
    /// it is hidden - a dotfile folder, build output - and one of those with
    /// lines taken out was shown as a deleted file holding only those lines.</summary>
    public bool Deleted { get; init; }
}

/// <summary>a run of consecutive deleted lines.</summary>
/// <param name="At">0-based line in the new version that now sits where they
/// did. The file's length when they came off the end.</param>
/// <param name="OldLine">0-based line in the old version the run started at.</param>
public sealed record RemovedBlock(int At, int OldLine, List<string> Lines);

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

    /// <summary>libgit2's handle is not thread safe, and a review now reads a
    /// commit's tree off the UI thread while the keys can still step the
    /// commits. Every public way in takes this, so the two never overlap -
    /// one waits for the other instead.</summary>
    readonly object _gate = new();

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

    public string HeadName { get { lock (_gate) return _repo.Head.FriendlyName; } }

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

    /// <summary>how far back a branch with no work of its own is shown. It has
    /// no change set - that is what "no work of its own" means - so what it
    /// can offer is its own recent history.</summary>
    public const int RecentSpan = 25;

    /// <summary>every branch, with work of its own first.
    ///
    /// The base branch used to be left out, along with anything fully merged
    /// into it, on the grounds that neither has a change set. That is true
    /// and it made the panel look broken: you open the list of branches and
    /// the branch you are on is not in it. So every branch is listed, and a
    /// branch with nothing ahead of the base shows its own recent history
    /// instead - one rule, and the detail line says which case a row is.
    ///
    /// Ordered by work first, because the branch you are about to review
    /// matters more than one that landed months ago.</summary>
    public List<ReviewTarget> Branches(int maxBranches = 40)
    {
        lock (_gate)
        {
            var targets = new List<ReviewTarget>();
            var baseName = BaseBranch();
            var baseTip = baseName is null ? null : _repo.Branches[baseName]?.Tip;
            if (baseTip is null) return targets;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ahead = new List<(Branch B, int By, DateTimeOffset When)>();
            var level = new List<(Branch B, DateTimeOffset When)>();

            foreach (var b in _repo.Branches)
            {
                if (b.Tip is null || b.FriendlyName.EndsWith("/HEAD")) continue;
                // a local branch and its remote twin are the same review
                var key = b.FriendlyName.StartsWith("origin/") ? b.FriendlyName[7..] : b.FriendlyName;
                if (!seen.Add(key)) continue;

                if (b.FriendlyName == baseName) { level.Add((b, b.Tip.Author.When)); continue; }
                try
                {
                    var div = _repo.ObjectDatabase.CalculateHistoryDivergence(b.Tip, baseTip);
                    if (div.AheadBy is null or 0) level.Add((b, b.Tip.Author.When));
                    else ahead.Add((b, div.AheadBy.Value, b.Tip.Author.When));
                }
                catch { }
            }

            foreach (var (b, by, _) in ahead.OrderByDescending(x => x.When).Take(maxBranches))
            {
                var mergeBase = _repo.ObjectDatabase.FindMergeBase(b.Tip, baseTip);
                targets.Add(new ReviewTarget(
                    b.FriendlyName,
                    $"{by} commit{(by == 1 ? "" : "s")} ahead of {baseName}",
                    (mergeBase ?? baseTip).Sha,
                    b.Tip.Sha));
            }

            // the base branch heads the rest: it is the one everything else is
            // measured against, so it is the one worth finding first
            var rest = level
                .OrderByDescending(x => x.B.FriendlyName == baseName)
                .ThenByDescending(x => x.When);

            foreach (var (b, _) in rest.Take(Math.Max(0, maxBranches - targets.Count)))
                targets.Add(RecentOf(b, b.FriendlyName == baseName
                    ? "the base branch"
                    : $"nothing ahead of {baseName}"));

            return targets;
        }
    }

    /// <summary>a branch shown as its own last few commits rather than as a
    /// difference from somewhere else.</summary>
    ReviewTarget RecentOf(Branch b, string why)
    {
        var tip = b.Tip!;
        var walk = _repo.Commits.QueryBy(new CommitFilter
        {
            IncludeReachableFrom = tip,
            SortBy = CommitSortStrategies.Topological,
        }).Take(RecentSpan + 1).ToList();

        // the whole branch when it is shorter than the span. The root commit
        // itself is the far end of the range and so is not in the diff, which
        // is the same thing every other target here does
        var from = walk.Count > RecentSpan ? walk[RecentSpan] : walk[^1];
        int span = Math.Min(walk.Count - 1, RecentSpan);

        return new ReviewTarget(
            b.FriendlyName,
            $"{why}; last {span} commit{(span == 1 ? "" : "s")}",
            from.Sha, tip.Sha);
    }

    /// <summary>merge commits that name a pull request, newest first.</summary>
    public List<ReviewTarget> MergedPrs(int max = 30, int scan = 600)
    {
        lock (_gate)
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
    }

    /// <summary>open pull requests, asked of GitHub through the gh command
    /// line tool, or null when that cannot be done - no gh, not signed in,
    /// not a GitHub repo, no network.
    ///
    /// An open pull request has no merge commit, so nothing in the local
    /// history says it exists: its branch shows under branches, but nothing
    /// there knows it is a pull request, what it is called or what it is for.
    /// Only the host knows, and gh is the host's own client, already signed
    /// in with whatever the user uses.
    ///
    /// No libgit2 here, so it runs off the UI thread.</summary>
    public static List<OpenPr>? OpenPrs(string root, int max = 50)
    {
        var json = Run(root, "gh", $"pr list --state open --limit {max} --json number,title,headRefName,baseRefName,headRefOid", 20_000);
        return json is null ? null : ParseOpenPrs(json);
    }

    public static List<OpenPr> ParseOpenPrs(string json)
    {
        var list = new List<OpenPr>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var pr in doc.RootElement.EnumerateArray())
                list.Add(new OpenPr(
                    pr.GetProperty("number").GetInt32(),
                    pr.GetProperty("title").GetString() ?? "",
                    pr.GetProperty("headRefName").GetString() ?? "",
                    pr.GetProperty("baseRefName").GetString() ?? "",
                    pr.GetProperty("headRefOid").GetString() ?? ""));
        }
        catch { }
        return list;
    }

    /// <summary>fetch a pull request's commits, for one whose branch was
    /// never fetched. Through the git on the path, so it signs in the way
    /// every other fetch does; libgit2 would need the credentials handed to
    /// it. Nothing gets a ref: the commits land in the object store, which is
    /// all a review reads. Off the UI thread.</summary>
    public static bool FetchPr(string root, int number) =>
        Run(root, "git", $"fetch --quiet origin pull/{number}/head", 90_000) is not null;

    static string? Run(string root, string exe, string args, int timeoutMs)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // a console prompt for a password would wait for ever on a
            // process with no console
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return null; }
            return p.ExitCode == 0 ? output.Result : null;
        }
        catch { return null; }
    }

    /// <summary>an open pull request as something to review: from where its
    /// branch left its base to its head. Null when its head commit is not
    /// here, which means it needs fetching first.</summary>
    public ReviewTarget? TargetFor(OpenPr pr)
    {
        lock (_gate)
        {
            if (_repo.Lookup<Commit>(pr.HeadSha) is not { } head) return null;
            var baseTip = _repo.Branches["origin/" + pr.Base]?.Tip
                          ?? _repo.Branches[pr.Base]?.Tip
                          ?? (BaseBranch() is { } name ? _repo.Branches[name]?.Tip : null);
            if (baseTip is null) return null;
            var from = _repo.ObjectDatabase.FindMergeBase(head, baseTip) ?? baseTip;
            return new ReviewTarget($"#{pr.Number}  {pr.Title}", $"open, {pr.Head} into {pr.Base}", from.Sha, head.Sha)
            {
                Open = pr,
            };
        }
    }

    /// <summary>the list row for one, before it is known whether its
    /// commits are here.</summary>
    public static ReviewTarget RowFor(OpenPr pr) =>
        new($"#{pr.Number}  {pr.Title}", $"open, into {pr.Base}", "", pr.HeadSha) { Open = pr };

    /// <summary>the commits a pull request contributed, oldest first, which is
    /// the order a reviewer wants to walk them in.</summary>
    public List<CommitInfo> CommitsOf(ReviewTarget target)
    {
        lock (_gate)
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
        lock (_gate)
        {
            var c = _repo.Lookup<Commit>(info.Sha);
            var parent = c?.Parents.FirstOrDefault();
            if (c is null) return null;
            return Compare(parent?.Tree, c.Tree, info.Subject);
        }
    }

    public ChangeSet? Diff(string fromSha, string toSha, string label)
    {
        lock (_gate)
        {
            var from = _repo.Lookup<Commit>(fromSha);
            var to = _repo.Lookup<Commit>(toSha);
            if (to is null) return null;
            return Compare(from?.Tree, to.Tree, label);
        }
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
                var change = new FileChange(entry.Path.Replace('\\', '/'), entry.LinesAdded, entry.LinesDeleted)
                {
                    Deleted = entry.Status == ChangeKind.Deleted,
                };
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

    static readonly Regex HunkHeader = new(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.Compiled);

    /// <summary>walk a unified diff, recording new-side line numbers, and the
    /// text of what was removed with its old-side line. patches count from 1,
    /// the canvas counts from 0.
    ///
    /// Public so a test can hand it a patch without building a repository.</summary>
    public static void ReadHunks(string patchText, FileChange into)
    {
        int newLine = 0, oldLine = 0;
        RemovedBlock? open = null;
        foreach (var raw in patchText.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var header = HunkHeader.Match(line);
            if (header.Success)
            {
                oldLine = int.Parse(header.Groups[1].Value) - 1;
                newLine = int.Parse(header.Groups[2].Value) - 1;
                open = null;
                continue;
            }
            // a block is a run of removed lines with nothing between them
            if (line.Length == 0 || line[0] != '-' || line.StartsWith("---")) open = null;
            if (line.Length == 0) { newLine++; oldLine++; continue; }
            switch (line[0])
            {
                case '+':
                    if (line.StartsWith("+++")) break;
                    into.AddedLines.Add(newLine++);
                    break;
                case '-':
                    if (line.StartsWith("---")) break;
                    into.RemovedAt.Add(Math.Max(0, newLine));
                    if (open is null)
                    {
                        open = new RemovedBlock(Math.Max(0, newLine), oldLine, []);
                        into.RemovedText.Add(open);
                    }
                    open.Lines.Add(line[1..]);
                    oldLine++;
                    break;
                case '\\':
                    break;                       // "\ No newline at end of file"
                default:
                    newLine++;
                    oldLine++;
                    break;
            }
        }
    }

    /// <summary>every text file in a commit's tree, as lines. this is what lets
    /// a branch be drawn against the code as it was, rather than against a
    /// working tree whose paths may have moved on entirely.</summary>
    public Dictionary<string, string[]>? Snapshot(string sha, Func<string, bool> wanted)
    {
        lock (_gate)
        {
            var commit = _repo.Lookup<Commit>(sha);
            if (commit is null) return null;

            var files = new Dictionary<string, string[]>(StringComparer.Ordinal);
            Walk(commit.Tree, "", files, wanted);
            return files;
        }
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

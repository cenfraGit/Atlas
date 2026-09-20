using LibGit2Sharp;

namespace Atlas.Tests;

/// <summary>the repo's own account of what is not its source.
///
/// A hardcoded list of noisy directories gets node_modules right and knows
/// nothing about anything else; .gitignore is the file designed to answer
/// this, so it is what gets asked. The patterns themselves are libgit2's
/// problem - what is tested here is that they are asked at all, asked about
/// directories before their contents, and never allowed to hide something
/// the repo is already tracking.</summary>
public class GitIgnoreTests
{
    /// <summary>a repo with an ignore file and nothing committed. Patterns
    /// work from `init`; nothing here needs history.</summary>
    static TempDir Repo(string ignoreFile, params (string Path, string Text)[] files)
    {
        var dir = new TempDir("atlas_ignore");
        Repository.Init(dir.Path);
        dir.File(".gitignore", ignoreFile);
        foreach (var (path, text) in files) dir.File(path, text);
        return dir;
    }

    static List<string> Paths(TempDir dir, bool showHidden = false)
    {
        using var ignore = GitIgnore.For(dir.Path);
        Assert.NotNull(ignore);
        return Scanner.Build(dir.Path, new ScanOptions(showHidden) { Ignored = ignore!.Ignored })
            .Files.Select(f => f.P).ToList();
    }

    // --- the predicate ----------------------------------------------------

    [Fact]
    public void AFolderThatIsNotARepoHasNothingToAsk()
    {
        using var dir = new TempDir();
        Assert.Null(GitIgnore.For(dir.Path));
    }

    [Fact]
    public void APatternIsHonoured()
    {
        using var dir = Repo("*.log\n");
        using var ignore = GitIgnore.For(dir.Path)!;

        Assert.True(ignore.Ignored("debug.log"));
        Assert.False(ignore.Ignored("debug.txt"));
    }

    [Fact]
    public void ADirectoryRuleIsAskedWithATrailingSlash()
    {
        using var dir = Repo("build/\n");
        using var ignore = GitIgnore.For(dir.Path)!;

        // `build/` means the directory, not a file called build
        Assert.True(ignore.Ignored("build/"));
    }

    [Fact]
    public void ANegationIsHonoured()
    {
        using var dir = Repo("*.log\n!keep.log\n");
        using var ignore = GitIgnore.For(dir.Path)!;

        Assert.True(ignore.Ignored("debug.log"));
        Assert.False(ignore.Ignored("keep.log"));
    }

    [Fact]
    public void AnEmptyPathIsNotIgnored()
    {
        using var dir = Repo("*\n");
        using var ignore = GitIgnore.For(dir.Path)!;

        Assert.False(ignore.Ignored(""));
    }

    [Fact]
    public void TheAnswerIsCachedButStillCorrect()
    {
        using var dir = Repo("*.log\n");
        using var ignore = GitIgnore.For(dir.Path)!;

        Assert.True(ignore.Ignored("a.log"));
        Assert.True(ignore.Ignored("a.log"));
        Assert.False(ignore.Ignored("a.cs"));
    }

    // --- tracked files ----------------------------------------------------

    [Fact]
    public void ATrackedFileIsNeverHiddenByAPattern()
    {
        using var dir = Repo("*.json\n");
        dir.File("committed.json", "{}");

        using (var repo = new Repository(dir.Path))
        {
            Commands.Stage(repo, "committed.json", new StageOptions { IncludeIgnored = true });
        }

        using var ignore = GitIgnore.For(dir.Path)!;

        // git ignores only what it is not already following. This is what
        // lets a repo commit its .atlas folder while gitignoring the pattern
        Assert.False(ignore.Ignored("committed.json"));
    }

    [Fact]
    public void AnUntrackedFileMatchingThePatternIsStillHidden()
    {
        using var dir = Repo("*.json\n");
        dir.File("committed.json", "{}");
        dir.File("scratch.json", "{}");

        using (var repo = new Repository(dir.Path))
        {
            Commands.Stage(repo, "committed.json", new StageOptions { IncludeIgnored = true });
        }

        using var ignore = GitIgnore.For(dir.Path)!;

        Assert.False(ignore.Ignored("committed.json"));
        Assert.True(ignore.Ignored("scratch.json"));
    }

    // --- the scan ---------------------------------------------------------

    [Fact]
    public void IgnoredFilesAreOffTheMap()
    {
        using var dir = Repo("*.log\n",
            ("app/Program.cs", "class P { }"),
            ("debug.log", "noise\n"));

        Assert.Equal(["app/Program.cs"], Paths(dir).Where(p => p != ".gitignore"));
    }

    [Fact]
    public void AnIgnoredDirectoryIsPrunedWholesale()
    {
        using var dir = Repo("/generated/\n",
            ("app/Program.cs", "class P { }"),
            ("generated/a.cs", "class A { }"),
            ("generated/deep/b.cs", "class B { }"));

        var paths = Paths(dir);

        Assert.DoesNotContain("generated/a.cs", paths);
        Assert.DoesNotContain("generated/deep/b.cs", paths);
        Assert.Contains("app/Program.cs", paths);
    }

    [Fact]
    public void ANestedIgnoreFileApplies()
    {
        using var dir = Repo("\n", ("app/Program.cs", "class P { }"));
        dir.File("app/.gitignore", "secret.cs\n");
        dir.File("app/secret.cs", "class S { }");

        var paths = Paths(dir);

        Assert.Contains("app/Program.cs", paths);
        Assert.DoesNotContain("app/secret.cs", paths);
    }

    [Fact]
    public void TheToggleShowsIgnoredFilesToo()
    {
        using var dir = Repo("*.log\n",
            ("app/Program.cs", "class P { }"),
            ("debug.log", "noise\n"));

        // "show me everything" has to mean everything
        Assert.Contains("debug.log", Paths(dir, showHidden: true));
    }

    [Fact]
    public void GitAndAtlasStayHiddenEvenSo()
    {
        using var dir = Repo("\n", ("app/Program.cs", "class P { }"));
        dir.File(".atlas/boards/b.json", "{}");

        var shown = Paths(dir, showHidden: true);

        Assert.DoesNotContain(".atlas/boards/b.json", shown);
        Assert.DoesNotContain(shown, p => p.StartsWith(".git/", StringComparison.Ordinal));
    }

    [Fact]
    public void WithNoRepoTheScanIsUnchanged()
    {
        using var dir = new TempDir();
        dir.File("app/Program.cs", "class P { }");
        dir.File("debug.log", "noise\n");

        // no repo, no .gitignore, nothing to ask: a folder is a normal way to
        // use Atlas and must not need git
        var paths = Scanner.Build(dir.Path).Files.Select(f => f.P).ToList();

        Assert.Contains("app/Program.cs", paths);
        Assert.Contains("debug.log", paths);
    }

    [Fact]
    public void WantedAgreesWithTheWalk()
    {
        using var dir = Repo("*.log\n/generated/\n",
            ("app/Program.cs", "class P { }"),
            ("debug.log", "noise\n"),
            ("generated/a.cs", "class A { }"));

        using var ignore = GitIgnore.For(dir.Path)!;
        var opts = new ScanOptions { Ignored = ignore.Ignored };

        foreach (var f in Scanner.Build(dir.Path, opts).Files)
            Assert.True(Scanner.Wanted(f.P, opts), $"the walk took {f.P} but Wanted() rejects it");

        Assert.False(Scanner.Wanted("debug.log", opts));
        Assert.False(Scanner.Wanted("generated/a.cs", opts));
        Assert.True(Scanner.Wanted("app/Program.cs", opts));
    }

    [Fact]
    public void ScanningASubdirectoryOfARepoStillReadsTheRootsRules()
    {
        using var dir = Repo("*.log\n");
        dir.File("app/Program.cs", "class P { }");
        dir.File("app/debug.log", "noise\n");

        var sub = Path.Combine(dir.Path, "app");
        using var ignore = GitIgnore.For(sub);
        Assert.NotNull(ignore);

        var paths = Scanner.Build(sub, new ScanOptions { Ignored = ignore!.Ignored })
            .Files.Select(f => f.P).ToList();

        // git's paths are relative to the work tree, not to what Atlas opened
        Assert.Equal(["Program.cs"], paths);
    }
}

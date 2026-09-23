namespace Atlas.Tests;

/// <summary>what a change deleted, kept as text.
///
/// The hunk reader walked every removed line of a patch and kept only where
/// it had been, so the change view could draw a red line where code went
/// and never the code. It keeps the lines now, a block per run, with the
/// line in the old file each run started at.</summary>
public class RemovedTextTests
{
    static FileChange Read(params string[] patch)
    {
        var change = new FileChange("a.cs", 0, 0);
        GitReview.ReadHunks(string.Join("\n", patch), change);
        return change;
    }

    [Fact]
    public void ARunOfRemovedLinesIsOneBlockWithItsText()
    {
        var c = Read(
            "@@ -10,5 +10,2 @@",
            " keep one",
            "-gone one",
            "-gone two",
            "-gone three",
            " keep two");

        var block = Assert.Single(c.RemovedText);
        Assert.Equal(["gone one", "gone two", "gone three"], block.Lines);
        Assert.Equal(10, block.At);         // the line that now sits where they were
        Assert.Equal(10, block.OldLine);    // 0-based line 10 is "gone one" in the old file
        Assert.Equal([10, 10, 10], c.RemovedAt);
    }

    /// <summary>a changed line is a removal and an addition in the same
    /// place, and the old text sits right above the new, as a diff shows it.</summary>
    [Fact]
    public void AModifiedLineKeepsTheOldTextWhereTheNewOneIs()
    {
        var c = Read(
            "@@ -4,3 +4,3 @@",
            " {",
            "-    static void Main() { }",
            "+    static void Main() { new Panel(); }",
            " }");

        var block = Assert.Single(c.RemovedText);
        Assert.Equal(["    static void Main() { }"], block.Lines);
        Assert.Equal(4, block.At);
        Assert.Equal([4], c.AddedLines);
    }

    [Fact]
    public void TwoRunsInOneHunkAreTwoBlocks()
    {
        var c = Read(
            "@@ -1,6 +1,4 @@",
            " a",
            "-b",
            " c",
            "-d",
            " e");

        Assert.Equal(2, c.RemovedText.Count);
        Assert.Equal((1, 1, "b"), (c.RemovedText[0].At, c.RemovedText[0].OldLine, c.RemovedText[0].Lines[0]));
        Assert.Equal((2, 3, "d"), (c.RemovedText[1].At, c.RemovedText[1].OldLine, c.RemovedText[1].Lines[0]));
    }

    /// <summary>each hunk says where it starts in both files; the old side
    /// is read from the header, not counted from the first hunk.</summary>
    [Fact]
    public void EveryHunkStartsFromItsOwnHeader()
    {
        var c = Read(
            "@@ -3,2 +3,1 @@",
            " x",
            "-y",
            "@@ -50,2 +49,1 @@",
            " p",
            "-q");

        Assert.Equal([3, 50], c.RemovedText.Select(b => b.OldLine));
        Assert.Equal([3, 49], c.RemovedText.Select(b => b.At));
    }

    /// <summary>taken off the end, there is no line below to name, so the
    /// place is the new file's length.</summary>
    [Fact]
    public void ARemovalAtTheEndIsAtTheFilesLength()
    {
        var c = Read(
            "@@ -1,3 +1,1 @@",
            " only",
            "-tail one",
            "-tail two",
            "\\ No newline at end of file");

        var block = Assert.Single(c.RemovedText);
        Assert.Equal(1, block.At);
        Assert.Equal(["tail one", "tail two"], block.Lines);
    }

    [Fact]
    public void ABlankRemovedLineIsKept()
    {
        var c = Read(
            "@@ -1,3 +1,2 @@",
            " a",
            "-",
            " b");

        Assert.Equal([""], Assert.Single(c.RemovedText).Lines);
    }

    /// <summary>and the same from a patch libgit2 wrote, not one typed here.</summary>
    [Fact]
    public void ARealCommitCarriesItsRemovedText()
    {
        using var git = new GitFixture();
        using var review = GitReview.Open(git.Path)!;
        string parent;
        using (var repo = new LibGit2Sharp.Repository(git.Path))
            parent = ((LibGit2Sharp.Commit)repo.Lookup(git.FeatureTipSha)).Parents.First().Sha;

        var set = review.Diff(parent, git.FeatureTipSha, "use the panel")!;

        var block = Assert.Single(set.ByPath["app/Program.cs"].RemovedText);
        Assert.Equal(["    static void Main() { }"], block.Lines);
        Assert.Equal(4, block.At);
        Assert.Equal(4, block.OldLine);
    }
}

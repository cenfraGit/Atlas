namespace Atlas.Tests;

/// <summary>the folder named on the command line is the one opened.
///
/// A tab-completed folder ends in a backslash, and quoted, <c>\"</c> reaches
/// the app as an escaped quote - <c>C:\repo"</c>, which is not a folder. It
/// was skipped without a word and Atlas opened whatever it scanned last.</summary>
public class LaunchTests
{
    [Fact]
    public void APlainPathIsTaken()
    {
        using var dir = new TempDir();
        Assert.Equal(dir.Path.TrimEnd('\\', '/'), App.RepoFrom([dir.Path]));
    }

    [Fact]
    public void AnEscapedQuoteFromATrailingBackslashIsForgiven()
    {
        using var dir = new TempDir();
        Assert.Equal(dir.Path.TrimEnd('\\', '/'), App.RepoFrom([dir.Path.TrimEnd('\\') + "\""]));
    }

    [Fact]
    public void ATrailingSlashIsDropped()
    {
        using var dir = new TempDir();
        Assert.Equal(dir.Path.TrimEnd('\\', '/'), App.RepoFrom([dir.Path.TrimEnd('\\') + "\\"]));
    }

    [Fact]
    public void FlagsAndThingsThatAreNotFoldersAreSkipped()
    {
        using var dir = new TempDir();
        Assert.Equal(dir.Path.TrimEnd('\\', '/'),
            App.RepoFrom(["--bench", "--goto", "1,2,3", Path.Combine(dir.Path, "nope"), dir.Path]));
        Assert.Null(App.RepoFrom(["--bench", Path.Combine(dir.Path, "nope")]));
    }

    [Fact]
    public void ADriveRootKeepsItsSlash()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory)!;
        Assert.Equal(root, App.RepoFrom([root]));
    }
}

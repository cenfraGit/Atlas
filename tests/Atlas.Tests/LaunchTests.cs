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

    /// <summary>what bash makes of an unquoted Windows path: every backslash
    /// dropped.</summary>
    static string Bashed(string path) => path[..2] + path[2..].Replace("\\", "").Replace("/", "");

    /// <summary>typed unquoted into Git Bash, a path loses its backslashes,
    /// and Atlas opened the last scan instead - which looked like it worked
    /// for as long as the last scan was the folder meant.</summary>
    [Fact]
    public void APathBashDroppedTheBackslashesFromIsPutBackTogether()
    {
        using var dir = new TempDir();
        var repo = Directory.CreateDirectory(Path.Combine(dir.Path, "Repos", "MyApp")).FullName;

        Assert.Equal(repo, App.RepoFrom([Bashed(repo)]));
    }

    /// <summary>and where two folders would both fit, it does not guess.</summary>
    [Fact]
    public void AnAmbiguousOneIsNotGuessed()
    {
        using var dir = new TempDir();
        var one = Directory.CreateDirectory(Path.Combine(dir.Path, "ab", "c")).FullName;
        Directory.CreateDirectory(Path.Combine(dir.Path, "a", "bc"));

        Assert.Null(App.Unmangled(Bashed(one)));
    }

    [Fact]
    public void ARealPathIsNeverUnmangled()
    {
        Assert.Null(App.Unmangled(@"C:\Windows"));
        Assert.Null(App.Unmangled("relative"));
    }
}

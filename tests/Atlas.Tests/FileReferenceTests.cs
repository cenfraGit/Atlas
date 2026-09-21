namespace Atlas.Tests;

/// <summary>a path is not an identity.
///
/// Every stored reference to a file keeps a content fingerprint beside the
/// path, so renaming or moving the file finds it again instead of orphaning
/// whatever pointed at it. The rule was written down and then only annotations
/// kept it: board windows stored null, and bookmarks had nowhere to put one
/// and resolved on the exact path alone.</summary>
public class FileReferenceTests
{
    const string Original = "app/ui/Panel.cs";
    const string Renamed = "app/ui/Sidebar.cs";

    /// <summary>rename a file on disk and rescan, so neither the path nor the
    /// name is what it was.</summary>
    static Scene Rescanned(TempDir repo)
    {
        File.Move(Path.Combine(repo.Path, "app", "ui", "Panel.cs"),
                  Path.Combine(repo.Path, "app", "ui", "Sidebar.cs"));
        return new Scene(Scanner.Build(repo.Path));
    }

    [Fact]
    public void AFileWindowIsMadeWithAFingerprint()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        var key = scene.KeyFor(Original);

        Assert.False(string.IsNullOrEmpty(key));
        Assert.Equal(key, FileKeys.Of(File.ReadAllLines(Path.Combine(repo.Path, "app", "ui", "Panel.cs"))));
    }

    [Fact]
    public void AWindowOnARenamedFileIsFoundByItsFingerprint()
    {
        using var repo = SampleRepo.Build();
        string key;
        using (var scene = new Scene(Scanner.Build(repo.Path))) key = scene.KeyFor(Original)!;

        using var after = Rescanned(repo);

        Assert.Equal(after.IndexOfPath(Renamed), after.ResolveFile(Original, key));
    }

    /// <summary>a board written before windows kept fingerprints has none, so
    /// opening it fills them in - otherwise the rename that happens next week
    /// orphans a board made today.</summary>
    [Fact]
    public void OpeningABoardFillsInMissingFingerprints()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));
        var board = new Board { Id = "b", Name = "old" };
        board.Items.Add(new BoardItem { Id = "w", Kind = "file", File = Original });
        board.Items.Add(new BoardItem { Id = "n", Kind = "note", Text = "not a file" });

        Assert.True(scene.EnsureKeys(board));

        Assert.Equal(scene.KeyFor(Original), board.Items[0].Key);
        Assert.Null(board.Items[1].Key);
        Assert.False(scene.EnsureKeys(board));      // nothing left to fill
    }

    [Fact]
    public void ABookmarkKeepsAFingerprintToo()
    {
        using var repo = SampleRepo.Build();
        using var scene = new Scene(Scanner.Build(repo.Path));

        int i = scene.IndexOfPath(Original);
        var f = scene.Data.Files[i];
        scene.CamS = 4f;
        scene.CamX = f.X + f.W / 2;
        scene.CamY = f.Y + f.H / 2;
        scene.Tier = Scene.TierFor(scene.CamS);

        var mark = BookmarkTargets.Capture(scene, "a place", 800, 600);

        Assert.Equal(Original, mark.File);
        Assert.False(string.IsNullOrEmpty(mark.Key));
    }

    /// <summary>and that is what makes a tour survive a refactor: every stop
    /// is a bookmark, and a stop that cannot find its file is a stop that
    /// drops you on the map with no explanation.</summary>
    [Fact]
    public void ABookmarkOnARenamedFileStillLands()
    {
        using var repo = SampleRepo.Build();
        Bookmark mark;
        using (var scene = new Scene(Scanner.Build(repo.Path)))
        {
            int i = scene.IndexOfPath(Original);
            var f = scene.Data.Files[i];
            scene.CamS = 4f;
            scene.CamX = f.X + f.W / 2;
            scene.CamY = f.Y + f.H / 2;
            scene.Tier = Scene.TierFor(scene.CamS);
            mark = BookmarkTargets.Capture(scene, "a place", 800, 600);
        }

        using var after = Rescanned(repo);
        var target = BookmarkTargets.Resolve(after, mark, 800, 600);

        Assert.False(target.Orphaned);
        var moved = after.Data.Files[after.IndexOfPath(Renamed)];
        Assert.InRange(target.Y, moved.Y, moved.Y + moved.H);
    }
}

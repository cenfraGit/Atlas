using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Atlas.Tests;

/// <summary>opening a pull request or branch for review reads it off the
/// UI thread.
///
/// Reading the commits, the diff and every file of the tree at that commit
/// is seconds on a big repo, and it was done on the UI thread, so the window
/// froze for all of it. libgit2's handle is not thread safe, which is why it
/// was left there; GitReview now serialises it instead.</summary>
public class ReviewLoadTests
{
    static (SceneView View, Scene Scene) Open(GitFixture git)
    {
        var scene = new Scene(Scanner.Build(git.Path));
        var store = BoardStore.Load(git.Path);
        var view = new SceneView(scene);
        view.AttachBoards(store, new BoardOverlay(store));
        view.AttachReview(new ReviewOverlay(), new CommitsPanel());
        view.BuildLayers();
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        return (view, scene);
    }

    static bool Until(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
        return done();
    }

    [AvaloniaFact]
    public void OpeningATargetReturnsAtOnceAndLandsLater()
    {
        using var git = new GitFixture();
        var (view, scene) = Open(git);
        using var review = GitReview.Open(git.Path)!;
        var pr = review.MergedPrs().Single();

        view.OpenTarget(pr);
        // nothing read yet: the call only started it
        Assert.False(scene.OnSnapshot);

        Assert.True(Until(() => scene.OnSnapshot && scene.Review is not null), "the review never arrived");
        Assert.Contains(scene.Review!.Files, f => f.Path == "app/Panel.cs");
    }

    /// <summary>picking a second target before the first has arrived shows
    /// the second: the first one's result is thrown away when it lands.</summary>
    [AvaloniaFact]
    public void ALaterPickWins()
    {
        using var git = new GitFixture();
        var (view, scene) = Open(git);
        using var review = GitReview.Open(git.Path)!;
        var pr = review.MergedPrs().Single();
        var branch = review.Branches().First(b => b.HeadSha == git.WipTipSha);

        view.OpenTarget(pr);
        view.OpenTarget(branch);

        Assert.True(Until(() => scene.Review is not null));
        // and give the first one every chance to land on top
        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
        Assert.Equal(branch.Label, scene.Review!.Label);
    }
}

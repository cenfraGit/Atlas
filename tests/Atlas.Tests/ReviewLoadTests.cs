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
        var deadline = DateTime.UtcNow.AddSeconds(30);
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

    /// <summary>R reads the tree again with the removed lines taken out, or
    /// put back - off the UI thread too, with the old view up meanwhile.</summary>
    [AvaloniaFact]
    public void TheRemovedLinesToggleLandsLater()
    {
        using var git = new GitFixture();
        var (view, scene) = Open(git);
        using var review = GitReview.Open(git.Path)!;
        view.OpenTarget(review.MergedPrs().Single());
        Assert.True(Until(() => scene.OnSnapshot && scene.Review is not null));
        Assert.NotEmpty(scene.Splices);                 // shown by default

        view.HandleKey(Avalonia.Input.Key.R);
        Assert.NotEmpty(scene.Splices);                 // still the old view
        Assert.True(Until(() => scene.Splices.Count == 0), "the removed lines never went");

        view.HandleKey(Avalonia.Input.Key.R);
        Assert.True(Until(() => scene.Splices.Count > 0), "the removed lines never came back");
    }

    /// <summary>R while the target is still being read is refused, rather
    /// than racing the read and landing the other setting.</summary>
    [AvaloniaFact]
    public void TheToggleWaitsForTheOpenToFinish()
    {
        using var git = new GitFixture();
        var (view, scene) = Open(git);
        using var review = GitReview.Open(git.Path)!;
        var pr = review.MergedPrs().Single();
        view.OpenTarget(pr);
        view.OpenTarget(pr);                            // _target is set by the first to land
        Assert.True(Until(() => scene.Review is not null));
        view.OpenTarget(pr);                            // and now one is under way again

        view.HandleKey(Avalonia.Input.Key.R);

        Assert.True(Until(() => scene.OnSnapshot && scene.Review is not null));
        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
        Assert.NotEmpty(scene.Splices);
    }

    /// <summary>the review panel opens at once and fills in when the list
    /// has been read.</summary>
    [AvaloniaFact]
    public void TheBranchListOpensAtOnceAndFillsIn()
    {
        using var git = new GitFixture();
        var scene = new Scene(Scanner.Build(git.Path));
        var store = BoardStore.Load(git.Path);
        var view = new SceneView(scene);
        var panel = new ReviewOverlay { Transitions = null };
        view.AttachBoards(store, new BoardOverlay(store));
        view.AttachReview(panel, new CommitsPanel());
        view.BuildLayers();
        var grid = new Grid();
        grid.Children.Add(view);
        grid.Children.Add(panel);
        var window = new Window { Width = 800, Height = 600, Content = grid };
        window.Show();
        var list = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(panel).OfType<ListBox>().Single();

        view.HandleKey(Avalonia.Input.Key.G);

        Assert.True(Reveal.Showing(panel));
        Assert.True(Until(() => list.ItemCount > 0), "the branches never arrived");
    }

    /// <summary>stepping to a commit reads its diff off the UI thread, and
    /// stepping back to the whole change is instant: it was read when the
    /// target opened and is kept, where it used to be diffed again.</summary>
    [AvaloniaFact]
    public void SteppingReadsInTheBackgroundAndTheWholeChangeIsKept()
    {
        using var git = new GitFixture();
        var (view, scene) = Open(git);
        using var review = GitReview.Open(git.Path)!;
        var pr = review.MergedPrs().Single();
        var commits = review.CommitsOf(pr);
        view.OpenTarget(pr);
        Assert.True(Until(() => scene.Review is not null));
        var whole = Paths(scene.Review!);

        view.HandleKey(Avalonia.Input.Key.Down);
        var first = Paths(review.OfCommit(commits[0])!);
        Assert.True(Until(() => Paths(scene.Review!) == first), "the commit never showed");

        view.HandleKey(Avalonia.Input.Key.Up);
        Assert.Equal(whole, Paths(scene.Review!));      // no waiting: it was kept
    }

    /// <summary>held down, the key steps faster than git answers; whatever
    /// lands late is dropped, and the last step is what shows.</summary>
    [AvaloniaFact]
    public void FastSteppingEndsOnTheLastStep()
    {
        using var git = new GitFixture();
        var (view, scene) = Open(git);
        using var review = GitReview.Open(git.Path)!;
        var pr = review.MergedPrs().Single();
        var commits = review.CommitsOf(pr);
        view.OpenTarget(pr);
        Assert.True(Until(() => scene.Review is not null));

        view.HandleKey(Avalonia.Input.Key.Down);    // 0
        view.HandleKey(Avalonia.Input.Key.Down);    // 1
        view.HandleKey(Avalonia.Input.Key.Up);      // 0

        var want = Paths(review.OfCommit(commits[0])!);
        Assert.True(Until(() => Paths(scene.Review!) == want));
        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
        Assert.Equal(want, Paths(scene.Review!));
    }

    static string Paths(ChangeSet set) => string.Join(",", set.Files.Select(f => f.Path).Order());

    /// <summary>Escape leaves the gathered change view - it is a way of
    /// looking at the review, not a board you are on - and the next Escape
    /// leaves the review.</summary>
    [AvaloniaFact]
    public void EscapeLeavesTheChangeViewThenTheReview()
    {
        using var git = new GitFixture();
        var (view, scene) = Open(git);
        using var review = GitReview.Open(git.Path)!;
        view.OpenTarget(review.MergedPrs().Single());
        Assert.True(Until(() => scene.Review is not null));

        view.HandleKey(Avalonia.Input.Key.C);
        Assert.True(scene.BoardReadOnly);

        Assert.True(view.Escape());
        Assert.False(scene.BoardReadOnly);
        Assert.Null(scene.ActiveBoard);
        Assert.NotNull(scene.Review);

        Assert.True(view.Escape());
        Assert.Null(scene.Review);
    }

    /// <summary>the brackets no longer step the commits; the arrows do.</summary>
    [AvaloniaFact]
    public void TheBracketsNoLongerStepCommits()
    {
        using var git = new GitFixture();
        var (view, scene) = Open(git);
        using var review = GitReview.Open(git.Path)!;
        view.OpenTarget(review.MergedPrs().Single());
        Assert.True(Until(() => scene.Review is not null));
        var whole = Paths(scene.Review!);

        view.HandleKey(Avalonia.Input.Key.OemCloseBrackets);
        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
        Assert.Equal(whole, Paths(scene.Review!));

        view.HandleKey(Avalonia.Input.Key.Down);
        Assert.True(Until(() => Paths(scene.Review!) != whole), "the arrow did not step");
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

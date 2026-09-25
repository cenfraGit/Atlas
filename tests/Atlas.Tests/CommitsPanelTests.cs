using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Atlas.Tests;

/// <summary>the commit list down the right while reviewing.</summary>
public class CommitsPanelTests
{
    /// <summary>every commit has a row on screen. The list virtualised, and
    /// handed its rows while the panel slid in it drew only the first - the
    /// "all commits" row - with none of the commits under it. The tour and
    /// boards panels had the same bug.</summary>
    [AvaloniaFact]
    public void EveryCommitIsDrawn()
    {
        using var git = new GitFixture();
        using var review = GitReview.Open(git.Path)!;
        var pr = review.MergedPrs().Single();
        var commits = review.CommitsOf(pr);
        var panel = new CommitsPanel();
        var window = new Window { Width = 1000, Height = 500, Content = new Grid { Children = { panel } } };
        window.Show();

        panel.Show(pr.Label, commits);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using (window.CaptureRenderedFrame()) { }

        var list = panel.GetVisualDescendants().OfType<ListBox>().Single();
        for (int i = 0; i <= commits.Count; i++)
            Assert.NotNull(list.ContainerFromIndex(i));
    }
}

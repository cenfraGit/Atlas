using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Atlas.Tests;

/// <summary>side panels: every one drags wider from its inner edge, and
/// whatever else is on screen moves over to stay clear of it.
///
/// The corner toggles, the bars and the dialogs sat wherever they were put,
/// and a panel sliding in covered them - the "map" button under the
/// workspace, the toggles under the commits.</summary>
public class SidePanelRoomTests
{
    static void Settle(Window w)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    static void Drag(Window w, Point from, Point to)
    {
        w.MouseMove(from);
        w.MouseDown(from, MouseButton.Left);
        w.MouseMove(to, RawInputModifiers.LeftMouseButton);
        w.MouseUp(to, MouseButton.Left);
    }

    /// <summary>a panel on the right widens by dragging its left edge left.</summary>
    [AvaloniaFact]
    public void ARightPanelWidensFromItsLeftEdge()
    {
        var tour = new TourPanel { Transitions = null };
        var window = new Window { Width = 1200, Height = 700, Content = new Grid { Children = { tour } } };
        window.Show();
        Reveal.Show(tour);
        Settle(window);
        double before = tour.Bounds.Width;

        var edge = new Point(tour.Bounds.Left + 2, 300);
        Drag(window, edge, new Point(edge.X - 120, 300));
        Settle(window);

        Assert.Equal(before + 120, tour.Bounds.Width, 1);
        Assert.Equal(1200, tour.Bounds.Right, 1);           // still against the right
    }

    [AvaloniaFact]
    public void TheCommitsPanelWidensToo()
    {
        var commits = new CommitsPanel { Transitions = null };
        var window = new Window { Width = 1200, Height = 700, Content = new Grid { Children = { commits } } };
        window.Show();
        Reveal.Show(commits);
        Settle(window);
        double before = commits.Bounds.Width;

        var edge = new Point(commits.Bounds.Left + 2, 300);
        Drag(window, edge, new Point(edge.X - 80, 300));
        Settle(window);

        Assert.Equal(before + 80, commits.Bounds.Width, 1);
    }

    /// <summary>the toggles move over by exactly a right panel's width,
    /// follow it as it is dragged, and come back when it closes; the bar
    /// along the top moves for a panel on the left.</summary>
    [AvaloniaFact]
    public void EverythingElseMovesClearOfTheSidePanels()
    {
        using var repo = SampleRepo.Build();
        var store = BoardStore.Load(repo.Path);
        var boards = new BoardOverlay(store) { Transitions = null };
        var tour = new TourPanel { Transitions = null };
        var commits = new CommitsPanel { Transitions = null };
        var islands = new ModeIslands();
        var hints = new HintBar();
        var window = new Window
        {
            Width = 1400, Height = 800,
            Content = new Grid { Children = { boards, tour, commits, islands, hints } },
        };
        App.MakeRoom([boards], [tour, commits], [islands, hints]);
        window.Show();
        var islandsOwn = islands.Margin;
        var hintsOwn = hints.Margin;

        Reveal.Show(tour);
        Settle(window);
        Assert.Equal(islandsOwn.Right + tour.Width, islands.Margin.Right, 1);
        Assert.True(islands.Bounds.Right <= tour.Bounds.Left + 0.5, "the toggles are under the tour panel");

        tour.Width += 100;                                  // as the grip does
        Settle(window);
        Assert.Equal(islandsOwn.Right + tour.Width, islands.Margin.Right, 1);

        Reveal.Show(boards);
        Settle(window);
        Assert.Equal(hintsOwn.Left + boards.Width, hints.Margin.Left, 1);
        Assert.Equal(hintsOwn.Right + tour.Width, hints.Margin.Right, 1);

        Reveal.Hide(tour);
        Reveal.Hide(boards);
        Assert.Equal(islandsOwn, islands.Margin);
        Assert.Equal(hintsOwn, hints.Margin);
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Atlas.Tests;

/// <summary>opening something from the bookmarks panel.
///
/// A single bookmark used to be a one-way trip: it flew you there and that
/// was the end of it, so the arrow keys meant nothing and reaching the next
/// place you had saved meant opening the panel again. A bookmark is now the
/// starting point of an unnamed tour of all of them, so the arrows work the
/// same whether you opened a tour or a bookmark.</summary>
public class BookmarkWalkTests
{
    static (BookmarkOverlay Panel, BookmarkStore Store, Scene Scene, TempDir Repo) Panel(
        int marks = 3, bool withTour = false)
    {
        var repo = SampleRepo.Build();
        var scene = new Scene(Scanner.Build(repo.Path));
        var store = BookmarkStore.Load(repo.Path);

        for (int i = 0; i < marks; i++)
            store.Bookmarks.Add(new Bookmark { Id = "m" + i, Name = "place " + i, X = i, Y = i, S = 1 });
        if (withTour)
            store.Tours.Add(new Tour { Id = "t", Name = "the tour", Stops = ["m0", "m2"] });

        var panel = new BookmarkOverlay(store, scene);
        var window = new Window { Width = 800, Height = 600, Content = panel };
        window.Show();
        return (panel, store, scene, repo);
    }

    static (Tour? Tour, int At) Open(BookmarkOverlay panel, int row)
    {
        (Tour? Tour, int At) got = (null, -1);
        panel.Play += (t, at) => got = (t, at);

        panel.Open();
        for (int i = 0; i < row; i++) panel.HandleKey(Key.Down);
        panel.HandleKey(Key.Enter);
        return got;
    }

    [AvaloniaFact]
    public void OpeningABookmarkStartsAWalkOfAllOfThem()
    {
        var (panel, _, scene, repo) = Panel();
        using (repo)
        using (scene)
        {
            var (tour, at) = Open(panel, row: 0);

            Assert.NotNull(tour);
            Assert.Equal(["m0", "m1", "m2"], tour!.Stops);
            Assert.Equal(0, at);
        }
    }

    /// <summary>and it starts on the one you picked, not at the beginning.</summary>
    [AvaloniaFact]
    public void ItStartsWhereYouOpenedIt()
    {
        var (panel, _, scene, repo) = Panel();
        using (repo)
        using (scene)
        {
            var (tour, at) = Open(panel, row: 2);

            Assert.NotNull(tour);
            Assert.Equal(2, at);
            Assert.Equal("m2", tour!.Stops[at]);
        }
    }

    /// <summary>the walk is never written to disk: it exists for as long as
    /// you are walking it, and a tour nobody named is not a tour.</summary>
    [AvaloniaFact]
    public void TheWalkIsNotStored()
    {
        var (panel, store, scene, repo) = Panel();
        using (repo)
        using (scene)
        {
            Open(panel, row: 0);

            Assert.Empty(store.Tours);
            Assert.Empty(BookmarkStore.Load(repo.Path).Tours);
        }
    }

    [AvaloniaFact]
    public void ARealTourStillOpensAtItsBeginning()
    {
        var (panel, _, scene, repo) = Panel(withTour: true);
        using (repo)
        using (scene)
        {
            // tours are listed first
            var (tour, at) = Open(panel, row: 0);

            Assert.Equal("the tour", tour!.Name);
            Assert.Equal(0, at);
            Assert.Equal(["m0", "m2"], tour.Stops);
        }
    }

    /// <summary>a tour's stops are its own; opening a bookmark below one
    /// must not hand back the tour's list.</summary>
    [AvaloniaFact]
    public void ABookmarkBelowATourStillWalksTheBookmarks()
    {
        var (panel, _, scene, repo) = Panel(withTour: true);
        using (repo)
        using (scene)
        {
            // row 0 is the tour, so row 2 is the second bookmark
            var (tour, at) = Open(panel, row: 2);

            Assert.Equal(["m0", "m1", "m2"], tour!.Stops);
            Assert.Equal(1, at);
        }
    }
}

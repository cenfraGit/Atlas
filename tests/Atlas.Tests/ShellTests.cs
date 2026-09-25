using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Atlas.Tests;

/// <summary>starting with no folder, and opening one - or another - from
/// inside Atlas.
///
/// A folder had to be passed on the command line, and a bare launch reopened
/// whatever had been scanned last from a cache in Atlas's own folder: which
/// is how a path argument that was being ignored went unnoticed, and why
/// driving the app changed what it opened next.</summary>
public class ShellTests
{
    static (Shell Shell, Window Window, string Recent, TempDir Data) Make()
    {
        var data = new TempDir("shell");
        var recent = Path.Combine(data.Path, "recent.json");
        var window = new Window { Width = 1000, Height = 700 };
        var shell = new Shell(window, recent);
        window.Show();
        return (shell, window, recent, data);
    }

    static bool Until(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!done() && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
        return done();
    }

    static List<string> Buttons(Window w) =>
        w.GetVisualDescendants().OfType<Button>()
            .Select(b => b.Content is TextBlock t ? t.Text ?? "" : b.Content?.ToString() ?? "").ToList();

    [AvaloniaFact]
    public void TheWelcomeListsRecentFoldersThatAreStillThere()
    {
        var (shell, window, recent, data) = Make();
        using var _ = data;
        using var repo = SampleRepo.Build();
        var gone = Path.Combine(data.Path, "gone");
        File.WriteAllText(recent, JsonSerializer.Serialize(new[] { repo.Path, gone }));

        shell.ShowWelcome();
        window.UpdateLayout();

        var buttons = Buttons(window);
        Assert.Contains("open folder...", buttons);
        Assert.Contains(repo.Path, buttons);
        Assert.DoesNotContain(gone, buttons);
        Assert.Null(shell.Current);
    }

    [AvaloniaFact]
    public void OpeningAFolderShowsItAndRemembersIt()
    {
        var (shell, window, recent, data) = Make();
        using var _ = data;
        using var repo = SampleRepo.Build();
        shell.ShowWelcome();

        shell.Open(repo.Path);

        Assert.True(Until(() => shell.Current is not null), "the folder never opened");
        Assert.Contains(shell.Current!, window.GetVisualDescendants());
        Assert.Equal(repo.Path, shell.Recent().First(), ignoreCase: true);
    }

    /// <summary>another folder replaces the first - newest first in the
    /// recent list - and the window's keys reach the new one.</summary>
    [AvaloniaFact]
    public void AnotherFolderReplacesTheFirst()
    {
        var (shell, window, _, data) = Make();
        using var __ = data;
        using var a = SampleRepo.Build("a");
        using var b = SampleRepo.Build("b");
        shell.ShowWelcome();
        shell.Open(a.Path);
        Assert.True(Until(() => shell.Current is not null));
        var first = shell.Current;

        shell.Open(b.Path);
        Assert.True(Until(() => shell.Current is not null && shell.Current != first));

        Assert.DoesNotContain(first!, window.GetVisualDescendants());
        Assert.Equal([b.Path, a.Path], shell.Recent().Take(2), StringComparer.OrdinalIgnoreCase);

        var workspace = window.GetVisualDescendants().OfType<BoardOverlay>().Single();
        workspace.Transitions = null;
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Assert.True(Reveal.Showing(workspace));
    }

    [AvaloniaFact]
    public void AFolderThatCannotBeOpenedSaysSo()
    {
        var (shell, window, _, data) = Make();
        using var __ = data;
        shell.ShowWelcome();

        // inside the test's own folder: an early version built a view onto a
        // missing folder, and the board watcher created it, on the real drive
        var missing = Path.Combine(data.Path, "missing");
        shell.Open(missing);

        Assert.True(Until(() => window.GetVisualDescendants().OfType<TextBlock>()
            .Any(t => t.Text?.StartsWith("could not open") == true)), "no word of the failure");
        Assert.Null(shell.Current);
        Assert.False(Directory.Exists(missing), "opening it created it");
    }
}

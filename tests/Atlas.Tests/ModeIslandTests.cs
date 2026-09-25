using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Atlas.Tests;

/// <summary>the toggles at the top right: wheel-zoom first, and under it
/// edit and snap together - only on a board that can be edited.</summary>
public class ModeIslandTests
{
    [AvaloniaFact]
    public void EditAndSnapShowOnABoardAndFollowTheView()
    {
        using var repo = SampleRepo.Build();
        var store = BoardStore.Load(repo.Path);
        store.Create("one");
        var scene = new Scene(Scanner.Build(repo.Path));
        var view = new SceneView(scene);
        var panel = new BoardOverlay(store) { Transitions = null };
        view.AttachBoards(store, panel);
        view.BuildLayers();

        // wired as the app wires them
        var islands = new ModeIslands();
        islands.EditChanged += view.SetEditing;
        islands.ZoomChanged += view.SetWheelZoom;
        islands.SnapChanged += view.SetSnap;
        view.MouseModeChanged += () => islands.Reflect(view.Editing, view.WheelZoom, view.SnapToGrid, view.OnEditableBoard);
        islands.Reflect(view.Editing, view.WheelZoom, view.SnapToGrid, view.OnEditableBoard);

        var window = new Window { Width = 1000, Height = 600, Content = new Grid { Children = { view, panel, islands } } };
        window.Show();
        Assert.False(islands.BoardTogglesShown);        // the map: nothing to edit

        panel.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var list = panel.GetVisualDescendants().OfType<ListBox>().Single();
        list.SelectedIndex = panel.Rows.ToList().FindIndex(r => r.Board is not null);
        panel.HandleKey(Key.Enter);
        Assert.True(islands.BoardTogglesShown);

        // G from the keyboard shows on the island; the island sets the view
        view.SetEditing(true);
        view.HandleKey(Key.G);
        Assert.True(view.SnapToGrid);
        window.UpdateLayout();
        var snap = islands.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "snap");
        var at = snap.TranslatePoint(new Point(snap.Bounds.Width / 2, snap.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Assert.False(view.SnapToGrid);

        // and back to Home hides them again
        panel.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        list.SelectedIndex = 0;
        panel.HandleKey(Key.Enter);
        Assert.False(islands.BoardTogglesShown);
    }
}

using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Atlas;

/// <summary>what the window shows: a welcome with the folders opened lately,
/// or a folder open in full - the map, its boards and every panel.
///
/// Atlas used to need a folder on the command line, and without one reopened
/// whatever it had scanned last, from a cache in its own folder - which is
/// how a path argument that was being ignored went unnoticed. Now a bare
/// launch asks, and any open folder can be swapped for another from the
/// workspace without restarting.</summary>
public sealed class Shell
{
    readonly Window _window;
    readonly string _recentPath;
    Action? _unhook;
    int _opening;

    /// <summary>the folder open now, or null on the welcome.</summary>
    public SceneView? Current { get; private set; }

    public Shell(Window window, string recentPath)
    {
        _window = window;
        _recentPath = recentPath;
        // once, for whichever view is current: wired per view they piled up
        App.WireKeys(window, () => Current);
    }

    /// <summary>Atlas's own data folder, found by walking up from the build
    /// to the repo that holds it. Machine specific and gitignored.</summary>
    public static string DataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "data")) &&
               !Directory.EnumerateFiles(dir.FullName, "Atlas.sln*").Any())
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? ".", "data");
    }

    // ---- recent folders ----

    /// <summary>folders opened lately, newest first, that are still there.</summary>
    public List<string> Recent()
    {
        try
        {
            if (File.Exists(_recentPath) &&
                JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_recentPath)) is { } list)
                return list.Where(Directory.Exists).ToList();
        }
        catch (Exception) { }
        return [];
    }

    void Remember(string folder)
    {
        var list = Recent();
        list.RemoveAll(p => string.Equals(p, folder, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, folder);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_recentPath)!);
            File.WriteAllText(_recentPath, JsonSerializer.Serialize(list.Take(10).ToList(),
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Console.WriteLine($"could not write {_recentPath}: {ex.Message}"); }
    }

    // ---- the welcome ----

    TextBlock? _status;

    /// <summary>no folder open: a panel shaped like the workspace, with a way
    /// to pick one and the ones opened lately.</summary>
    public void ShowWelcome(string? message = null)
    {
        Close();
        _status = Line(message ?? "no folder open", Ui.Dim);
        var list = new StackPanel();
        foreach (var folder in Recent())
        {
            var row = Flat(folder);
            row.Click += (_, _) => Open(folder);
            list.Children.Add(row);
        }
        var open = Flat("open folder...");
        open.Foreground = Ui.Fore;
        open.Click += (_, _) => PickFolder();

        var panel = new Border
        {
            Background = Ui.PanelBg, BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(12), Width = 380, HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel
            {
                Children =
                {
                    Line("WORKSPACE", Ui.Accent),
                    _status,
                    open,
                    Line(list.Children.Count > 0 ? "recent" : "nothing opened yet", Ui.Dim, top: 14),
                    list,
                    Line("or pass a folder on the command line: Atlas.exe C:\\path\\to\\repo", Ui.Dim, top: 14),
                },
            },
        };
        _window.Content = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x04, 0x07, 0x0f)), Children = { panel } };
    }

    static TextBlock Line(string text, IBrush brush, double top = 0) => new()
    {
        Text = text, FontFamily = Ui.Mono, FontSize = 12, Foreground = brush,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 8),
    };

    static Button Flat(string text) => new()
    {
        Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
        FontFamily = Ui.Mono, FontSize = 12, Foreground = Ui.Dim,
        Background = Brushes.Transparent, BorderThickness = new Thickness(0),
        Padding = new Thickness(0, 3), HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Left,
        Cursor = new Cursor(StandardCursorType.Hand),
    };

    /// <summary>the system's folder picker, then open what was picked.</summary>
    public async void PickFolder()
    {
        var picked = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "open a folder in Atlas",
            AllowMultiple = false,
        });
        if (picked.FirstOrDefault()?.TryGetLocalPath() is { } folder) Open(folder);
    }

    // ---- a folder, open ----

    /// <summary>scan a folder off the UI thread and show it. A later pick
    /// while one is still scanning wins.</summary>
    public void Open(string folder, bool bench = false, string? startCam = null, bool stress = false)
    {
        int turn = ++_opening;
        // before anything is built: a view onto a folder that is not there
        // watches it, and the board watcher creates .atlas/boards in it
        if (!Directory.Exists(folder)) { ShowWelcome($"could not open {folder}: it is not a folder"); return; }
        if (_status is not null) _status.Text = $"opening {folder}...";
        Task.Run(() =>
        {
            using var ignore = GitIgnore.For(folder);
            return Scanner.Build(folder, new ScanOptions { Ignored = ignore is null ? null : ignore.Ignored });
        }).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (turn != _opening) return;
            if (t.IsFaulted)
            {
                ShowWelcome($"could not open {folder}: {t.Exception?.GetBaseException().Message}");
                return;
            }
            var scan = t.Result;
            Console.WriteLine($"scanned {scan.Files.Count} files, {scan.Folders.Count} folders in {folder}");
            Remember(scan.Root);
            Show(scan, bench, startCam, stress);
        }));
    }

    /// <summary>let go of the folder that is open: its watchers, its git
    /// handle, its place in the room-making. The scene itself is left to the
    /// collector - the render thread may still be inside a frame of it.</summary>
    void Close()
    {
        _unhook?.Invoke();
        _unhook = null;
        Current?.Close();
        Current = null;
        _status = null;
    }

    void Show(Scan scan, bool bench, string? startCam, bool stress)
    {
        Close();
        var scene = new Scene(scan);
        if (stress) scene.Stress();
        var view = new SceneView(scene, bench, startCam);
        var search = new SearchOverlay(scene);
        search.Chosen += view.OnFileChosen;
        view.OpenSearch = search.Open;
        view.SearchOpen = () => Reveal.Showing(search);
        view.CloseSearch = search.Close;

        var prompt = new PromptOverlay();
        view.AttachPrompt(prompt);
        var tour = new TourPanel();
        view.AttachTour(tour);

        var reviews = new ReviewOverlay();
        var commits = new CommitsPanel();
        view.AttachReview(reviews, commits);

        var noteStore = AnnotationStore.Load(scene.Data.Root);
        scene.Notes = noteStore;
        var notes = new AnnotationOverlay(noteStore, scene);
        notes.Chosen += view.FlyToAnnotation;
        notes.EditRequested += view.EditAnnotation;
        view.AttachNotes(noteStore, notes);

        var boardStore = BoardStore.Load(scene.Data.Root);
        var boards = new BoardOverlay(boardStore);
        boards.OpenFolderRequested += PickFolder;
        view.AttachBoards(boardStore, boards);
        view.WatchBoards();
        view.WatchRepo();

        var hints = new HintBar();
        view.AttachHints(hints);

        var boardBar = new BoardBar();
        view.AttachBoardBar(boardBar);

        var penBar = new PenBar(SceneView.Colours);
        view.AttachPenBar(penBar);

        var eraserBar = new EraserBar();
        view.AttachEraserBar(eraserBar);

        var grep = new GrepOverlay();
        view.AttachGrep(grep);

        var editor = new InlineEditor();
        view.AttachEditor(editor);

        var islands = new ModeIslands();
        islands.EditChanged += view.SetEditing;
        islands.ZoomChanged += view.SetWheelZoom;
        islands.SnapChanged += view.SetSnap;
        view.MouseModeChanged += () => islands.Reflect(view.Editing, view.WheelZoom, view.SnapToGrid, view.OnEditableBoard);
        islands.Reflect(view.Editing, view.WheelZoom, view.SnapToGrid, view.OnEditableBoard);

        var root = new Grid();
        root.Children.Add(view);
        root.Children.Add(search);
        root.Children.Add(tour);
        root.Children.Add(boards);
        root.Children.Add(notes);
        root.Children.Add(boardBar);
        root.Children.Add(penBar);
        root.Children.Add(eraserBar);
        root.Children.Add(hints);
        // the islands after both side panels, so they sit over them
        root.Children.Add(commits);
        root.Children.Add(islands);
        root.Children.Add(reviews);
        root.Children.Add(grep);
        root.Children.Add(prompt);
        // over everything: it is inside an item, and an item is on the
        // canvas under all of these
        root.Children.Add(editor);

        view.BuildLayers();
        _unhook = App.MakeRoom([boards], [tour, commits],
            [search, notes, boardBar, penBar, eraserBar, hints, islands, reviews, grep, prompt]);

        _window.Content = root;
        _window.Title = $"Atlas - {Path.GetFileName(scan.Root)}";
        Current = view;
        view.Focus();
    }
}

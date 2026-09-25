using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

namespace Atlas;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // tests live in tests/Atlas.Tests and run with `dotnet test`
        if (args.Contains("--samples")) { Samples.Run(args); return; }
        if (args.Length > 0 && args[0] is "board" or "boards") { Environment.ExitCode = BoardCli.Run(args); return; }

        // before anything can throw. A WinExe has no console, so without this
        // an unhandled exception is a window that vanishes with no message.
        // Only the handlers that do not touch Dispatcher.UIThread - see
        // Crash.InstallEarly for what happens when one does
        Crash.InstallEarly();
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}

public sealed class App : Application
{
    public override void Initialize()
    {
        // controls have no template without a theme; the canvas draws itself,
        // but the search box is a real TextBox and needs one
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = Environment.GetCommandLineArgs();
            var scene = new Scene(LoadScan(args));
            if (args.Contains("--stress")) scene.Stress();
            var goTo = Array.IndexOf(args, "--goto");
            var startCam = goTo >= 0 && goTo + 1 < args.Length ? args[goTo + 1] : null;
            var view = new SceneView(scene, args.Contains("--bench"), startCam);
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
            view.AttachBoards(boardStore, boards);
            view.WatchBoards();
            view.WatchRepo();

            var hints = new HintBar();
            view.AttachHints(hints);

            var boardBar = new BoardBar();
            view.AttachBoardBar(boardBar);

            var back = new BackButton();
            view.AttachBack(back);

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
            view.MouseModeChanged += () => islands.Reflect(view.Editing, view.WheelZoom);
            islands.Reflect(view.Editing, view.WheelZoom);

            var root = new Grid();
            root.Children.Add(view);
            root.Children.Add(search);
            root.Children.Add(tour);
            root.Children.Add(boards);
            root.Children.Add(notes);
            root.Children.Add(boardBar);
            root.Children.Add(back);
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

            var window = new Window
            {
                Title = "Atlas",
                Width = 1400,
                Height = 900,
                Background = Brushes.Black,
                Content = root,
            };

            WireKeys(window, view);

            // now the framework is up, so the dispatcher is the real one,
            // and there is somewhere to say it out loud
            Crash.InstallOnUiThread(msg => Dispatcher.UIThread.Post(() =>
                view.Toast($"something went wrong - {msg}")));

            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>the folder a command line names, cleaned up, or null.
    ///
    /// A folder tab-completed in a terminal ends in a backslash, and quoted
    /// that makes <c>\"</c> - which Windows reads as an escaped quote, so the
    /// argument arrives as <c>C:\repo"</c>. That is not a folder, and the path
    /// was ignored without a word: Atlas opened whatever it had last scanned.
    /// The quote is trimmed, the path made full, the trailing slash dropped.</summary>
    public static string? RepoFrom(IEnumerable<string> args)
    {
        foreach (var a in args)
        {
            if (a.StartsWith("--")) continue;
            var p = a.Trim().Trim('"');
            if (p.Length == 0) continue;
            if (!Directory.Exists(p) && Unmangled(p) is { } restored)
            {
                Console.WriteLine($"read {a} as {restored} - bash drops unquoted backslashes");
                p = restored;
            }
            try { p = Path.GetFullPath(p); } catch { continue; }
            if (!Directory.Exists(p)) continue;
            var root = Path.GetPathRoot(p);
            return p.Length > (root?.Length ?? 0) ? p.TrimEnd('\\', '/') : p;
        }
        return null;
    }

    static bool LooksLikePath(string a) =>
        a.Contains('\\') || a.Contains('/') || a.StartsWith('.') || a.Length > 2 && a[1] == ':';

    /// <summary>a Windows path typed unquoted into bash, put back together.
    ///
    /// Bash treats a backslash as an escape and drops it, so
    /// <c>C:\Users\me\Repo</c> arrives as <c>C:UsersmeRepo</c>. That used to
    /// fall through to the last scan, which looked like it worked for as long
    /// as the last scan happened to be the folder meant. The separators can
    /// be found again by walking down from the drive, taking each folder
    /// whose name the rest of the text starts with - and only a single match
    /// is accepted, so this never guesses between two.</summary>
    public static string? Unmangled(string arg)
    {
        if (arg.Length < 3 || !char.IsLetter(arg[0]) || arg[1] != ':' || arg[2] is '\\' or '/') return null;
        var found = new List<string>();
        Walk(arg[..2] + "\\", arg[2..], found);
        return found.Count == 1 ? found[0] : null;

        static void Walk(string dir, string rest, List<string> found)
        {
            if (found.Count > 1) return;
            if (rest.Length == 0) { found.Add(dir.TrimEnd('\\')); return; }
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { return; }
            foreach (var sub in subs)
            {
                var name = Path.GetFileName(sub);
                if (rest.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    Walk(sub + "\\", rest[name.Length..], found);
            }
        }
    }

    /// <summary>the keys the window takes before any control can: Escape,
    /// peeled off the layer stack, and Tab, the workspace. Public so a test
    /// can wire a window the way the app does.</summary>
    public static void WireKeys(Window window, SceneView view)
    {
        // tunnel, so the window sees Escape on the way *down* to whatever
        // holds focus. A dialog that owns the key can only handle it while
        // it owns focus, and that is exactly how dialogs got stranded
        window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (view.Escape()) e.Handled = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        // tab opens and closes the workspace from anywhere: taken here,
        // on the way down, before a focused list or button can read it as
        // "move to the next control". Not while typing, where a tab may
        // be text, and not with a modifier, which is someone else's key
        window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Tab || e.KeyModifiers != KeyModifiers.None) return;
            if (window.FocusManager?.GetFocusedElement() is TextBox) return;
            view.ToggleWorkspace();
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    static Scan LoadScan(string[] args)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "data")) &&
               !Directory.EnumerateFiles(dir.FullName, "Atlas.sln*").Any())
            dir = dir.Parent;
        var cache = Path.Combine(dir?.FullName ?? ".", "data", "scan.json");

        // first non-flag argument is a repo to scan; otherwise reuse the last scan
        var repo = RepoFrom(args.Skip(1));
        if (repo is null && args.Skip(1).FirstOrDefault(a => !a.StartsWith("--") && LooksLikePath(a)) is { } asked)
            Console.WriteLine($"not a folder: {asked} - opening the last scan instead");
        if (repo is not null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var ignore = GitIgnore.For(repo);
            var fresh = Scanner.Build(repo, new ScanOptions
            {
                Ignored = ignore is null ? null : ignore.Ignored,
            });
            Scanner.Save(fresh, cache);
            Console.WriteLine($"scanned {fresh.Files.Count} files, {fresh.Folders.Count} folders " +
                              $"in {sw.ElapsedMilliseconds}ms -> {cache}");
            if (fresh.Skipped > 0)
                Console.WriteLine($"{fresh.Skipped} skipped as binary or too large. " +
                                  "'.' shows build output and dotfiles too.");
            return fresh;
        }
        // the cache records the folder it scanned, which is the one thing in it
        // that belongs to this machine. with no cache, or one written on
        // another machine, fall back to Atlas itself rather than failing
        if (File.Exists(cache))
        {
            using var fs = File.OpenRead(cache);
            var cached = JsonSerializer.Deserialize<Scan>(fs)!;

            // a cache written by an older Atlas can deserialise into something
            // shaped right and empty - folders were called districts once -
            // and a map with files but nowhere to put them draws nothing
            if (cached.Files.Count > 0 && cached.Folders.Count == 0)
                Console.WriteLine("the cached scan is from an older Atlas; rescanning.");
            else if (Directory.Exists(cached.Root))
                return cached;
            else
                Console.WriteLine($"the cached scan points at {cached.Root}, which is not here.");
        }

        var self = dir?.FullName ?? ".";
        Console.WriteLine($"no scan yet: reading {self}. pass a folder to open a different repo.");
        var own = Scanner.Build(self);
        Scanner.Save(own, cache);
        return own;
    }
}

public sealed class SceneView : Control
{
    readonly Scene _scene;
    readonly double[] _ring = new double[90];
    int _ringAt;
    double _lastDrawMs;

    bool _drag;
    Point _last;
    Point _dragOrigin;
    int _axis;          // 0 undecided, 1 horizontal, 2 vertical
    Point _pressAt;
    double _dragDist;

    /// <summary>whether a press on an item has travelled far enough to be a
    /// drag. Until it has, nothing moves: a click wobbles by a pixel or two,
    /// and that used to nudge the item and rewrite the board's file.</summary>
    bool _grabbed;
    const double GrabSlop = 4;

    Flight? _flight;
    double _flightT0;
    readonly Glide _glide = new();
    double _lastFrame = -1;

    // the tail end of a pan, for throwing the canvas when it is let go
    float _panVx, _panVy;
    double _panAt = -1;

    /// <summary>keep a running estimate of how fast the hand is moving.
    ///
    /// Averaged with the previous estimate rather than taken from the last
    /// frame alone: one frame is a few milliseconds and its velocity is
    /// mostly noise, and a pause at the end of a drag has to be able to bring
    /// the number back down or every gesture ends in a throw.</summary>
    void TrackPan(float dx, float dy)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = _panAt < 0 ? 0 : now - _panAt;
        _panAt = now;

        if (dt <= 0 || dt > 0.12) { _panVx = _panVy = 0; return; }

        const float Blend = 0.55f;
        _panVx = _panVx * (1 - Blend) + (float)(dx / dt) * Blend;
        _panVy = _panVy * (1 - Blend) + (float)(dy / dt) * Blend;
    }

    /// <summary>a drag let go with speed on it carries on. A drag that has
    /// already stopped moving does not, which is what the staleness check is
    /// for: resting the hand before letting go means you meant to stop.</summary>
    void ThrowPan()
    {
        bool stale = _panAt < 0 || _clock.Elapsed.TotalSeconds - _panAt > 0.09;
        if (!stale) _glide.Flick(_scene, _panVx, _panVy);

        _panVx = _panVy = 0;
        _panAt = -1;
        if (_glide.Running)
        {
            // never throw the canvas somewhere it is not allowed to be
            var (x, y, s) = (_scene.CamX, _scene.CamY, _scene.CamS);
            _scene.CamX = _glide.X; _scene.CamY = _glide.Y;
            _scene.ClampCamera((float)Bounds.Width, (float)Bounds.Height);
            _glide.To(_scene.CamX, _scene.CamY, _glide.S);
            _scene.CamX = x; _scene.CamY = y; _scene.CamS = s;
            InvalidateVisual();
        }
    }
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    // bench: (zoom, world-units of pan per frame, frame count)
    readonly (float Zoom, float Dx, int Frames)[] _phases =
        [(0.5f, 25f, 400), (0.9f, 20f, 400), (4.5f, 6f, 300)];
    int _phase = -1, _phaseFrame, _warmFrames;
    bool _benchDone;
    List<double> _samples = [];
    string _benchText = "";

    readonly bool _autoBench;

    string? _startCam;
    static readonly IBrush _bg = new SolidColorBrush(Color.FromRgb(0x04, 0x07, 0x0f));

    public SceneView(Scene scene, bool autoBench = false, string? startCam = null)
    {
        _scene = scene;
        _autoBench = autoBench;
        scene.RequestRedraw = () => Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);
        _startCam = startCam;
        Focusable = true;
        ClipToBounds = true;
        DoubleTapped += OnDoubleTapped;
        ApplyCursor();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_startCam is not null)
        {
            var v = _startCam.Split(',');
            _scene.CamX = float.Parse(v[0]); _scene.CamY = float.Parse(v[1]); _scene.CamS = float.Parse(v[2]);
            _startCam = null;
            InvalidateVisual();
            return;
        }
        if (_scene.CamS <= 0.051f) _scene.Fit((float)Bounds.Width, (float)Bounds.Height);
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
    }

    public override void Render(DrawingContext context)
    {
        var w = (float)Bounds.Width;
        var h = (float)Bounds.Height;
        if (w < 2 || h < 2) return;
        context.FillRectangle(_bg, new Rect(0, 0, w, h));

        double now = _clock.Elapsed.TotalSeconds;
        // a long gap means the canvas was idle, not that one frame took a
        // second; easing across it would fling the camera
        float dt = _lastFrame < 0 ? 0 : (float)Math.Min(now - _lastFrame, 0.05);
        _lastFrame = now;

        if (_flight is not null)
        {
            if (!_flight.Sample(_clock.Elapsed.TotalMilliseconds - _flightT0,
                                out var fx, out var fy, out var fs))
                _flight = null;
                _glide.Stop();
            _scene.CamX = fx; _scene.CamY = fy; _scene.CamS = fs;
        }
        else
        {
            _glide.Step(_scene, dt);
        }

        if (_autoBench && _phase < 0 && !_benchDone && ++_warmFrames > 30) { _phase = 0; _phaseFrame = 0; }
        if (_phase >= 0) StepBench(w);

        if (EditingItem() is not null) QueuePlaceEditor();

        // where the tour's stops are, while its panel is open - not while
        // one is playing, when the frames would fly past on every step
        _scene.StopsShown = Reveal.Showing(_stops) && _tour is null && !_scene.BoardReadOnly;
        _scene.StopPicked = _stops?.Selected ?? -1;
        StepSpotlight(now);
        StepRubberband(now);
        context.Custom(new SceneOp(new Rect(0, 0, w, h), _scene, w, h));
        if (_scene.Samples.Count > 0)
        {
            lock (_scene.Samples)
            {
                foreach (var v in _scene.Samples) _ring[_ringAt++ % _ring.Length] = v;
                if (_phase >= 0) _samples.AddRange(_scene.Samples);
                _scene.Samples.Clear();
            }
        }

        DrawHud(context);

        // only the benchmark free-runs; otherwise input and pending work drive redraws
        // a toast has to expire off-frame, so keep drawing while one is up
        if (_phase >= 0 || _flight is not null || _glide.Running || ToastShowing || SpotFading || _bandFadeAt >= 0 ||
            (_autoBench && !_benchDone))
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);
        else
            _lastFrame = -1;   // next frame starts a new gesture, not a huge dt
    }

    /// <summary>how tall the hint bar along the bottom is, near enough.</summary>
    const double HintBarH = 30;

    void DrawHud(DrawingContext ctx)
    {
        var sorted = _ring.Where(v => v > 0).OrderBy(v => v).ToArray();
        var med = sorted.Length > 0 ? sorted[sorted.Length / 2] : 0;
        var tierName = new[] { "folders", "cards", "bars", "text" }[_scene.Tier];
        var line1 = $"{med:F2} ms draw  |  zoom {_scene.CamS:F3}x  |  tier {tierName}";
        var line2 = $"{_scene.Data.Files.Count} files  {_scene.Data.Folders.Count} folders  " +
                    $"{_scene.VisibleCards} visible  {_scene.ChunksBuilt} built" +
                    (_scene.BuiltThisFrame > 0 ? $"  +{_scene.BuiltThisFrame}" : "");
        // bottom right, just above the hint bar, and under every panel since
        // it is drawn by the canvas. It sat in the top left, where the "map"
        // button and the boards panel both want to be
        var lines = new List<(string Text, Color Colour)>
        {
            (line1, Color.FromRgb(0xff, 0xd1, 0x66)),
            (line2, Color.FromRgb(0x35, 0x70, 0x8f)),
        };
        if (_benchText.Length > 0) lines.Add((_benchText, Color.FromRgb(0x5f, 0xd3, 0xf3)));
        double y = Bounds.Height - HintBarH - 8 - lines.Count * 16;
        foreach (var (text, colour) in lines)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Ui.Mono), 12, new SolidColorBrush(colour));
            ctx.DrawText(ft, new Point(Bounds.Width - 12 - ft.Width, y));
            y += 16;
        }
        DrawCaption(ctx);
    }

    void UpdateHoverLine(Point p)
    {
        if (!Editing || _scene.Tier < 3) { if (_scene.HoverLine is not null) { _scene.HoverLine = null; InvalidateVisual(); } return; }
        var (wx, wy) = WorldAt(p);
        var hit = _scene.LineAt(wx, wy);
        if (Equals(hit, _scene.HoverLine)) return;
        _scene.HoverLine = hit;
        InvalidateVisual();
    }

    /// <summary>the whole declaration around a line, for double click.</summary>
    (int From, int To)? SymbolRangeAt(int fileIndex, int line)
    {
        var f = _scene.Data.Files[fileIndex];
        var full = Path.Combine(_scene.Data.Root, f.P.Replace('/', Path.DirectorySeparatorChar));
        var sym = Symbols.Innermost(Symbols.ForFile(full), line);
        return sym is { } s ? (s.StartLine, s.EndLine) : null;
    }

    void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_scene.ActiveBoard is not null)
        {
            // edit mode is what says whether this board is being changed or
            // read. A double click that opens an editor outside it is a way
            // to alter a board you only meant to look at
            if (!Editing) return;

            var (bx, by) = WorldAt(e.GetPosition(this));
            // anything with words in it, which now includes the shapes: a
            // rectangle you double click is a rectangle you are naming
            // and an arrow, whose words are its label
            if ((_scene.ArrowAt(bx, by) ?? _scene.ItemAt(bx, by)) is { } words &&
                (HasText(words) || words.Kind == "file" && by < words.Y + Scene.WinHeadH)) BeginEdit(words);
            return;
        }
        if (_scene.Tier < 3 || !Editing) return;
        var (wx, wy) = WorldAt(e.GetPosition(this));
        if (_scene.LineAt(wx, wy) is not { } hit) return;

        // a whole method is a more useful unit than one line
        var range = SymbolRangeAt(hit.File, hit.Line);
        if (range is { } r) Select(hit.File, r.From, r.To);
        else Select(hit.File, hit.Line, hit.Line);
    }

    /// <summary>what the menu acts on: the picked lines, else the line clicked.</summary>
    (int File, int From, int To)? TargetFor(Point p)
    {
        var (wx, wy) = WorldAt(p);
        if (_scene.Tier >= 3 && _scene.LineAt(wx, wy) is { } hit)
        {
            if (_scene.Selection is { } sel && sel.File == hit.File &&
                hit.Line >= sel.From && hit.Line <= sel.To)
                return sel;
            Select(hit.File, hit.Line, hit.Line);
            return _scene.Selection;
        }
        int file = _scene.FileAt(wx, wy);
        if (file < 0) return null;
        return (file, -1, -1);          // the whole file, no particular lines
    }

    void ShowContextMenu(Point p)
    {
        if (_scene.ActiveBoard is not null) { ShowBoardMenu(p); return; }

        if (Editing && _scene.PickedFiles.Count > 0)
        {
            var picked = _scene.PickedFiles.ToList();
            var known = _boardStore?.Boards ?? [];
            var to = known.Select(b => ContextActions.Item(b.Name, () => AddFilesToBoard(b, picked))).ToList();
            to.Add(ContextActions.Item("New board...", () => NewBoardFromFiles(picked)));
            OpenMenu(new List<MenuItem>
            {
                ContextActions.Item($"── {picked.Count} file{(picked.Count == 1 ? "" : "s")} ──", () => { }, enabled: false),
                ContextActions.Submenu("Add to board", to),
                ContextActions.Item("Clear selection", () => { _scene.PickedFiles.Clear(); InvalidateVisual(); }),
            });
            return;
        }
        if (TargetFor(p) is not { } target) return;

        var f = _scene.Data.Files[target.File];
        var name = f.P[(f.P.LastIndexOf('/') + 1)..];
        bool hasLines = target.From >= 0;
        var what = hasLines
            ? target.From == target.To
                ? $"{name}:{target.From + 1}"
                : $"{name}:{target.From + 1}-{target.To + 1}"
            : name;

        var items = new List<MenuItem>
        {
            ContextActions.Item($"── {what} ──", () => { }, enabled: false),
        };

        // the whole file goes on the board. a note about a range is written
        // there afterwards, where it has the surrounding context to make sense
        var whole = (target.File, -1, -1);
        var boards = _boardStore?.Boards ?? [];
        var addTo = boards.Select(b =>
            ContextActions.Item(b.Name, () => AddToBoard(b, whole))).ToList();
        addTo.Add(ContextActions.Item("New board...", () => NewBoardFrom(whole)));
        items.Add(ContextActions.Submenu("Add file to board", addTo));

        // notes under these lines are shown, but authored on a board
        foreach (var (a, anchor) in _scene.AnchorsFor(f.P))
        {
            if (!hasLines || anchor.Line < target.From || anchor.Line > target.To) continue;
            var shown = a.Text.Length > 40 ? a.Text[..39] + "…" : a.Text;
            items.Add(ContextActions.Item("note: " + shown, () => { }, enabled: false));
        }

        OpenMenu(items);
    }

    /// <summary>one menu at a time: clicking repeatedly used to stack them.</summary>
    /// <summary>a prompt must never outlive the thing it was asking about.</summary>
    ScanOptions _scanOptions = ScanOptions.Default;

    /// <summary>show or hide build output, dependencies and dotfiles. A rescan
    /// of this repo, so the map is laid out afresh around what is now on it -
    /// node_modules is not a few extra cards, it is most of the map.</summary>
    void ToggleHidden()
    {
        if (_scene.OnSnapshot) { Toast("leave the commit first"); return; }

        _scanOptions = _scanOptions with { ShowHidden = !_scanOptions.ShowHidden };
        Toast(_scanOptions.ShowHidden ? "rescanning, everything..." : "rescanning...");
        InvalidateVisual();

        var root = _scene.Data.Root;
        var opts = _scanOptions;
        Task.Run(() =>
        {
            // the repo handle is not thread safe, so it belongs to this scan
            using var ignore = GitIgnore.For(root);
            var fresh = Scanner.Build(root, opts with { Ignored = ignore is null ? null : ignore.Ignored });
            Dispatcher.UIThread.Post(() =>
            {
                _scene.ShowScan(fresh);
                FitAll();
                Toast(Describe(fresh));
                InvalidateVisual();
            });
        });
    }

    FileSystemWatcher? _repoWatch;
    readonly HashSet<string> _touched = [];
    int _rescanAsk;
    bool _rescanning;

    /// <summary>rescan when files change on disk. The scan was taken once, so
    /// a file that grew had a stale card height on the map and a stale line
    /// count everywhere that had not read it, and a new file never appeared.
    ///
    /// Only paths the scanner would include count - a build writing into
    /// bin/ is not a reason to rescan - and a burst of changes is one rescan,
    /// a second after the last of them.</summary>
    public void WatchRepo()
    {
        var root = _scene.Data.Root;
        try
        {
            _repoWatch = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            // a folder is "changed" whenever anything in it is, which is the
            // file's event over again - and a folder path in the list means
            // every cached text is dropped. Its renames still count
            _repoWatch.Changed += (_, e) => { if (!Directory.Exists(e.FullPath)) Touched(root, e.FullPath); };
            _repoWatch.Created += (_, e) => Touched(root, e.FullPath);
            _repoWatch.Deleted += (_, e) => Touched(root, e.FullPath);
            _repoWatch.Renamed += (_, e) => { Touched(root, e.OldFullPath); Touched(root, e.FullPath); };
            // the buffer overflowed: something changed, and nobody knows what
            _repoWatch.Error += (_, _) => Touched(root, null);
            _repoWatch.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"not watching {root}: {ex.Message}");
        }
    }

    /// <summary>on a pool thread: note the path and ask for a rescan soon.</summary>
    void Touched(string root, string? full)
    {
        string rel = "*";
        if (full is not null)
        {
            rel = Path.GetRelativePath(root, full).Replace('\\', '/');
            if (!Scanner.Wanted(rel, _scanOptions)) return;
        }
        lock (_touched) _touched.Add(rel);
        ScheduleRescan(1000);
    }

    void ScheduleRescan(int ms)
    {
        int ask = Interlocked.Increment(ref _rescanAsk);
        Task.Delay(ms).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (ask == _rescanAsk) Rescan();
        }));
    }

    /// <summary>scan again, off the UI thread, and put it on screen without
    /// moving the camera. Waits out a drag, a scan already running, and a
    /// commit being reviewed - that is a tree from history, not the working
    /// copy - by asking again a little later.</summary>
    public void Rescan()
    {
        if (_drag || _rescanning || _scene.OnSnapshot) { ScheduleRescan(2000); return; }
        string[] touched;
        lock (_touched) { touched = [.. _touched]; _touched.Clear(); }
        if (touched.Length == 0) return;

        _rescanning = true;
        var root = _scene.Data.Root;
        var opts = _scanOptions;
        Task.Run(() =>
        {
            Scan fresh;
            try
            {
                using var ignore = GitIgnore.For(root);
                fresh = Scanner.Build(root, opts with { Ignored = ignore is null ? null : ignore.Ignored });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"rescan failed: {ex.Message}");
                Dispatcher.UIThread.Post(() => _rescanning = false);
                return;
            }
            Dispatcher.UIThread.Post(() => TakeRescan(fresh, touched));
        });
    }

    void TakeRescan(Scan fresh, string[] touched)
    {
        _rescanning = false;
        if (_scene.OnSnapshot || _drag)
        {
            lock (_touched) foreach (var p in touched) _touched.Add(p);
            ScheduleRescan(2000);
            return;
        }

        // a path the watcher saw that is in neither scan was gitignored, which
        // the watcher cannot ask about from its thread - nothing to show
        var paths = fresh.Files.Select(f => f.P).Concat(_scene.Data.Files.Select(f => f.P)).ToList();
        bool whole = touched.Contains("*");
        bool relevant = whole || touched.Any(t => paths.Any(p => p == t || p.StartsWith(t + "/", StringComparison.Ordinal)));
        if (!relevant) return;

        foreach (var t in touched) Symbols.Forget(Path.Combine(fresh.Root, t.Replace('/', Path.DirectorySeparatorChar)));
        // a folder that moved takes files with it, and the paths the watcher
        // named are the folder's: drop every cached text rather than guess
        bool folders = touched.Any(t => !paths.Contains(t));
        _scene.ShowScan(fresh, whole || folders ? null : touched);

        // the open board's windows follow their code, as they do on opening it
        if (_scene.ActiveBoard is { } b && !_scene.BoardReadOnly) FollowCode(b);
        InvalidateVisual();
    }

    /// <summary>the spotlight, on or off, at the pointer.</summary>
    public void ToggleSpotlight()
    {
        _spotOn = !_spotOn;
        _spotAt = _clock.Elapsed.TotalSeconds;
        // it stays drawn while it fades out; StepSpotlight lets it go
        if (_spotOn) _scene.Spotlight = new SkiaSharp.SKPoint((float)_pointer.X, (float)_pointer.Y);
        Toast(_spotOn ? "spotlight - space or esc to turn off, alt+wheel to size" : "spotlight off");
        InvalidateVisual();
    }

    bool _spotOn;
    double _spotAt = -1;
    const double SpotFade = 0.18;

    /// <summary>whether the spotlight is on - it can still be fading out
    /// after this says no.</summary>
    public bool SpotlightOn => _spotOn;

    bool SpotFading => _spotAt >= 0 && _clock.Elapsed.TotalSeconds - _spotAt < SpotFade;

    /// <summary>ease the spotlight in or out, once a frame.</summary>
    void StepSpotlight(double now)
    {
        if (_scene.Spotlight is null) return;
        double t = Math.Clamp((now - _spotAt) / SpotFade, 0, 1);
        float eased = (float)(1 - Math.Pow(1 - t, 3));
        _scene.SpotlightAmount = _spotOn ? eased : 1 - eased;
        if (!_spotOn && t >= 1) _scene.Spotlight = null;
    }

    static string Describe(Scan scan)
    {
        var what = scan.ShowingHidden ? "everything" : "source";
        return scan.Skipped == 0
            ? $"{scan.Files.Count} files, {what}"
            : $"{scan.Files.Count} files, {what}; {scan.Skipped} skipped as binary or too large";
    }

    public readonly Layers Layers = new();

    /// <summary>build the Escape order once every overlay is attached.
    /// Innermost first: a prompt sits over a panel, a panel over the canvas,
    /// and an armed tool is the last thing standing.</summary>
    public void BuildLayers()
    {
        // innermost first: a menu opens over a panel, a prompt over the
        // canvas, and an editor is inside the item itself
        Layers.Add("editing", () => _editor is { Editing: true }, () => _editor!.Cancel());
        Layers.Add("menu", () => _menuOpen, CloseMenu);
        // a stop being dragged in the tour panel goes back where it was
        Layers.Add("stop drag", () => _stops is { Dragging: true }, () => _stops!.CancelDrag());
        Layers.Add("prompt", () => Reveal.Showing(_prompt), () => { _prompt!.Close(); Focus(); });
        Layers.Add("search", () => SearchOpen?.Invoke() ?? false, () => { CloseSearch?.Invoke(); Focus(); });
        Layers.Add("grep", () => Reveal.Showing(_grep), () => { _grep!.Close(); Focus(); });
        // a board or group being dragged in the boards panel goes back first,
        // and only the next Escape closes the panel
        Layers.Add("board drag", () => _boards is { Dragging: true }, () => _boards!.CancelDrag());
        Layers.Add("boards", () => Reveal.Showing(_boards), () => _boards!.Close());
        Layers.Add("tour panel", () => Reveal.Showing(_stops), () => { _stops!.Close(); Focus(); });
        Layers.Add("annotations", () => Reveal.Showing(_notes), () => _notes!.Close());
        Layers.Add("reviews", () => Reveal.Showing(_reviews), () => _reviews!.Close());
        Layers.Add("tour", () => _tour is not null, EndTour);
        Layers.Add("tool", () => _armBrush || _armEraser || _armArrow || _armShape is not null, DisarmTools);
        // before the selection, and it clears that too. A search leaves a
        // selection behind on the line it landed on, so with the selection
        // first you had to press Escape twice to be rid of one search
        Layers.Add("matches", () => _scene.Find is not null, ClearFind);
        Layers.Add("selection", HasSelection, ClearSelection);
        // outermost: presenting is the last thing Escape should interrupt
        Layers.Add("spotlight", () => _spotOn, ToggleSpotlight);
        // deliberately no "board" layer. Escape closes what is open - a
        // dialog, a menu, an armed tool, a selection - and leaving the board
        // is not closing anything; it is going somewhere. That is alt+left
        // and the "<" button. Escape did leave a board once, and putting a
        // dialog on top of one then meant Escape both dismissed the dialog
        // and threw you off the board
        Layers.Add("review", () => _scene.Review is not null && _scene.ActiveBoard is null, LeaveReview);
    }

    public Func<bool>? SearchOpen;
    public Action? CloseSearch;

    bool HasSelection() =>
        _scene.Picked.Count > 0 || _scene.PickedFiles.Count > 0 || _scene.Selection is not null;

    void ClearSelection()
    {
        _scene.Picked.Clear();
        _scene.PickedFiles.Clear();
        _scene.Selection = null;
        InvalidateVisual();
    }

    void DisarmTools()
    {
        _armBrush = _armEraser = _armArrow = false;
        _armShape = null;
        _scene.ShowAnchors = false;
        _scene.StrokeDraft = null;
        _scene.ArrowDraft = null;
        _scene.ShapeDraft = null;
        RefreshBoardBar();
        ApplyCursor();
        InvalidateVisual();
    }

    /// <summary>close one thing. The window calls this before the key reaches
    /// whatever has focus, so nothing can hold Escape hostage.</summary>
    public bool Escape()
    {
        bool closed = Layers.Dismiss() is not null;
        if (closed) InvalidateVisual();
        return closed;
    }

    void DismissPrompt() => _prompt?.Close();

    void OpenMenu(List<MenuItem> items)
    {
        DismissPrompt();
        _menu?.Close();
        _menu = new ContextMenu { ItemsSource = items, Placement = PlacementMode.Pointer };
        // the menu is Avalonia's and closes itself when it is clicked away
        // from, so the flag has to be told rather than inferred
        _menu.Closed += (_, _) => { _menuOpen = false; Focus(); };
        _menuOpen = true;
        _menu.Open(this);
    }

    /// <summary>true while the secondary-click menu is up.
    ///
    /// Avalonia owns that menu and handles Escape inside it, which is fine
    /// until focus is somewhere it does not expect - the same way every
    /// dialog used to strand itself before Layers existed. It is in the stack
    /// now, innermost of all, so the window can always shut it.</summary>
    bool _menuOpen;

    void CloseMenu()
    {
        _menu?.Close();
        _menuOpen = false;
        Focus();
    }

    void ShowBoardMenu(Point p)
    {
        var (wx, wy) = WorldAt(p);
        // the menu acts on whatever a click would select, arrows included
        var item = _scene.PickAt(wx, wy);
        var board = _scene.ActiveBoard!;
        // a right click on a line of code in a window offers notes
        if (_scene.LineInWindowAt(WorldAt(p).X, WorldAt(p).Y) is { } spot)
        {
            var file = _scene.Data.Files[spot.File];
            var name = file.P[(file.P.LastIndexOf('/') + 1)..];
            var noteItems = new List<MenuItem>
            {
                ContextActions.Item($"── {name}:{spot.Line + 1} ──", () => { }, enabled: false),
                ContextActions.Item(
                    _scene.Selection is { } s && s.File == spot.File && s.To > s.From
                        ? $"Annotate lines {s.From + 1}-{s.To + 1}..."
                        : "Annotate this line...",
                    () => AnnotateSelection(Target(spot), local: false)),
            };

            // the choice belongs where the note is made. A board of its own is
            // usually explaining something, and half of what you write while
            // explaining is about the explanation rather than about the code
            if (_scene.ActiveBoard is not null && !_scene.BoardReadOnly)
                noteItems.Add(ContextActions.Item("Annotate for this board only...",
                    () => AnnotateSelection(Target(spot), local: true)));
            // named by what they do. spelling the note back at you made the
            // menu grow with the note and read like a paragraph
            var here = new List<Annotation>();
            foreach (var (a, anchor) in _scene.AnchorsFor(file.P))
                if (spot.Line >= anchor.Line && spot.Line < anchor.Line + Math.Max(1, a.Span))
                    here.Add(a);
            for (int n = 0; n < here.Count; n++)
            {
                var a = here[n];
                var tag = here.Count > 1 ? $" {n + 1}" : "";
                noteItems.Add(ContextActions.Item($"Edit annotation{tag}...", () => EditAnnotation(a)));
                if (_scene.ActiveBoard is not null && !_scene.BoardReadOnly)
                    noteItems.Add(ContextActions.Item(
                        a.Global ? $"Keep annotation{tag} on this board" : $"Show annotation{tag} everywhere",
                        () => SetAnnotationScope([a], local: a.Global)));
                noteItems.Add(ContextActions.Item($"Delete annotation{tag}", () => DeleteAnnotation(a)));
            }
            noteItems.Add(ContextActions.Item("Change line range...", () => EditRange(spot.Item)));
            noteItems.Add(ContextActions.Item("Bring to front", () =>
            {
                _scene.Picked.Clear();
                _scene.Picked.Add(spot.Item.Id);
                BringToFront();
            }));
            noteItems.Add(ContextActions.Item("Send to back", () =>
            {
                _scene.Picked.Clear();
                _scene.Picked.Add(spot.Item.Id);
                SendToBack();
            }));
            noteItems.Add(ContextActions.Item("Remove this window", () =>
            {
                Remember();
                _scene.Remove(board, [spot.Item]);
                _boardStore?.Save(board);
                _boards?.Rebuild();
                InvalidateVisual();
            }));
            noteItems.Add(ContextActions.Item("Back to the map", LeaveBoard));
            OpenMenu(noteItems);
            return;
        }

        var picked = board.Items.Where(i => _scene.Picked.Contains(i.Id)).ToList();
        if (item is not null && !_scene.Picked.Contains(item.Id))
        {
            _scene.Picked.Clear();
            _scene.Picked.Add(item.Id);
            picked = [item];
        }

        var header = picked.Count > 1 ? $"{picked.Count} items" : item?.Kind ?? board.Name;
        var items = new List<MenuItem>
        {
            ContextActions.Item($"── {header} ──", () => { }, enabled: false),
        };

        if (picked.Count == 1 && HasText(picked[0]))
            items.Add(ContextActions.Item("Edit text", () => BeginEdit(picked[0])));
        if (picked.Count == 1 && picked[0].Kind == "file")
        {
            items.Add(ContextActions.Item("Edit title", () => BeginEdit(picked[0])));
            items.Add(ContextActions.Item("Change line range...", () => EditRange(picked[0])));
        }
        if (picked.Count == 2)
            items.Add(ContextActions.Item("Connect these", () => Connect(picked[0], picked[1])));

        // only what applies to everything picked
        if (picked.Count > 0 &&
            picked.All(i => i.Kind is "note" or "arrow" or "text" || Scene.IsShape(i.Kind) || Strokes.Is(i)))
            items.Add(ContextActions.Submenu(
                picked.All(i => Scene.IsShape(i.Kind)) ? "Border colour" : "Colour", Palette(picked)));

        // a fill is a shape's own business; a note or an arrow has no inside
        if (picked.Count > 0 && picked.All(i => Scene.IsShape(i.Kind)))
            items.Add(ContextActions.Submenu("Fill", Fills(picked)));

        if (picked.Count > 0 && picked.All(HasLineWidth))
            items.Add(ContextActions.Submenu("Line width", LineWidths(picked)));

        if (picked.Count > 0 && picked.All(HasText))
            items.Add(ContextActions.Submenu("Text size", TextSizes(picked)));
        if (picked.Count > 0)
        {
            items.Add(ContextActions.Item("Bring to front", BringToFront));
            items.Add(ContextActions.Item("Send to back", SendToBack));
            items.Add(ContextActions.Item("Copy  ctrl+C", () => CopyPicked(picked)));
            items.Add(ContextActions.Item(picked.Count > 1 ? "Remove these" : "Remove", DeletePicked));
        }

        items.Add(ContextActions.Item("Add board note...", AddNote));
        items.Add(ContextActions.Item("Add rectangle  1", () => AddShape("shape")));
        items.Add(ContextActions.Item("Add ellipse  2", () => AddShape("ellipse")));
        items.Add(ContextActions.Item("Add diamond  3", () => AddShape("diamond")));
        items.Add(ContextActions.Item("Add label...  4", AddLabel));
        items.Add(ContextActions.Item("Add image...", AddImageFromDisk));
        items.Add(ContextActions.Item("Paste image (ctrl+V)", PasteImage));
        if (_clipboard.Count > 0)
            items.Add(ContextActions.Item($"Paste {_clipboard.Count}  ctrl+V", () => Paste(p)));
        items.Add(ContextActions.Item("Back to the map", LeaveBoard));
        OpenMenu(items);
    }

    string[]? LinesFor(int fileIndex)
    {
        var f = _scene.Data.Files[fileIndex];
        _scene.EnsureAnchored(f.P);
        return _scene.LinesOf(f.P);
    }

    bool ReadOnlyHere()
    {
        if (!_scene.OnSnapshot) return false;
        Toast("annotations belong to the working tree, not to a commit you are reviewing");
        return true;
    }

    /// <summary>the picked range if it is on this file, else the one line.</summary>
    (int File, int From, int To) Target((BoardItem Item, int File, int Line) spot) =>
        _scene.Selection is { } sel && sel.File == spot.File
            ? sel
            : (spot.File, spot.Line, spot.Line);

    void AnnotateSelection((int File, int From, int To) target, bool local = false)
    {
        if (_noteStore is null || _prompt is null || ReadOnlyHere()) return;
        var f = _scene.Data.Files[target.File];
        var lines = LinesFor(target.File);
        if (lines is null) return;
        int line = Math.Clamp(target.From < 0 ? 0 : target.From, 0, lines.Length - 1);

        var board = local ? _scene.ActiveBoard?.Id : null;
        var where = board is null ? "annotate" : "annotate, this board only";

        _prompt.Ask($"{where}  {f.P[(f.P.LastIndexOf('/') + 1)..]}:{line + 1}", "", text =>
        {
            var full = Path.Combine(_scene.Data.Root, f.P.Replace('/', Path.DirectorySeparatorChar));
            int to = target.To < 0 ? line : Math.Clamp(target.To, line, lines.Length - 1);
            var a = Anchors.Create(f.P, full, lines, line, to, text);
            a.Board = board;
            _noteStore.Annotations.Add(a);
            _noteStore.Save();
            _scene.Reanchor(f.P, full, lines);
            Saved($"annotation on {a.Symbol ?? f.P}");
            Focus();
        });
    }

    /// <summary>move annotations between showing everywhere and showing on
    /// this board only. Several at once, because deciding that a run of notes
    /// belongs to the board you are building is one decision.</summary>
    void SetAnnotationScope(IEnumerable<Annotation> notes, bool local)
    {
        if (_noteStore is null || ReadOnlyHere()) return;

        var board = _scene.ActiveBoard;
        if (local && board is null) { Toast("open a board to keep a note on it"); return; }

        int changed = 0;
        foreach (var a in notes)
        {
            var want = local ? board!.Id : null;
            if (a.Board == want) continue;
            a.Board = want;
            changed++;
        }
        if (changed == 0) return;

        _noteStore.Save();
        _notes?.Refresh();
        Saved(local
            ? $"{changed} note{(changed == 1 ? "" : "s")} kept on {board!.Name}"
            : $"{changed} note{(changed == 1 ? "" : "s")} shown everywhere");
        InvalidateVisual();
    }

    /// <summary>the selection becomes the window's line range, so a board shows
    /// exactly the method you picked rather than the whole file.</summary>
    BoardItem WindowFor((int File, int From, int To) target)
    {
        var f = _scene.Data.Files[target.File];
        return new BoardItem
        {
            Id = BoardStore.NewId(), Kind = "file", File = f.P, Key = _scene.KeyFor(f.P),
            Line = target.From < 0 ? 0 : target.From,
            EndLine = target.To, W = 620,
        };
    }

    void AddFilesToBoard(Board board, List<int> files)
    {
        if (_boardStore is null) return;
        float y = board.Items.Count == 0 ? 0 : board.Items.Max(i => i.Y + _scene.LastHeight(i)) + 40;
        foreach (var i in files)
        {
            var f = _scene.Data.Files[i];
            board.Items.Add(new BoardItem
            {
                Id = BoardStore.NewId(), Kind = "file", File = f.P, Key = _scene.KeyFor(f.P),
                Line = 0, EndLine = -1, W = 620, X = 0, Y = y,
            });
            y += Math.Min(f.N, 400) * _scene.Data.LineH * (620f / f.W) + 26 + 40;
        }
        _boardStore.Save(board);
        _lastBoard = board;
        _boards?.Rebuild();
        _scene.PickedFiles.Clear();
        Saved($"{files.Count} file{(files.Count == 1 ? "" : "s")} added to  {board.Name}");
    }

    void NewBoardFromFiles(List<int> files)
    {
        if (_boardStore is null || _prompt is null) return;
        _prompt.Ask("name the new board", "board", name =>
        {
            var board = _boardStore.Create(name);
            AddFilesToBoard(board, files);
            _boards?.Rebuild();
            Focus();
        });
    }

    void AddToBoard(Board board, (int File, int From, int To) target)
    {
        if (_boardStore is null) return;
        Remember();
        var item = WindowFor(target);
        PlaceBelowExisting(board, item);
        board.Items.Add(item);
        _boardStore.Save(board);
        _lastBoard = board;
        _boards?.Rebuild();
        Saved($"{Path.GetFileName(item.File)} added to  {board.Name}");
    }

    void NewBoardFrom((int File, int From, int To) target)
    {
        if (_boardStore is null || _prompt is null) return;
        var f = _scene.Data.Files[target.File];
        var suggested = Path.GetFileNameWithoutExtension(f.P);
        _prompt.Ask("name the new board", suggested, name =>
        {
            var board = _boardStore.Create(name);
            AddToBoard(board, target);
            _boards?.Rebuild();
            Focus();
        });
    }

    public void EditAnnotation(Annotation a)
    {
        if (_noteStore is null || _prompt is null || ReadOnlyHere()) return;
        _prompt.Ask("edit the annotation", a.Text, text =>
        {
            a.Text = text;
            _noteStore.Save();
            _scene.Reanchor(a.File);
            _notes?.Refresh();
            Saved("annotation updated");
            Focus();
        });
    }

    void DeleteAnnotation(Annotation a)
    {
        if (_noteStore is null || ReadOnlyHere()) return;
        _noteStore.Annotations.Remove(a);
        _noteStore.Save();
        _scene.Reanchor(a.File);
        Saved("annotation deleted");
    }

    /// <summary>everything writes straight to .atlas; say so, because
    /// invisible saving reads as no saving at all.</summary>
    void Saved(string what) => Toast($"saved: {what}");

    string _toast = "";
    double _toastUntil;
    const double ToastSeconds = 2.6;

    /// <summary>say something for a moment.
    ///
    /// This used to write straight into _caption, which is what names the
    /// board you are on - so every passing message permanently replaced it,
    /// and a run of them read as the bottom of the screen going haywire. A
    /// toast is transient and a caption is not, so they are two things.</summary>
    public void Toast(string message)
    {
        _toast = message;
        _toastUntil = _clock.Elapsed.TotalSeconds + ToastSeconds;
        InvalidateVisual();
    }

    bool ToastShowing => _clock.Elapsed.TotalSeconds < _toastUntil;

    /// <summary>snap the picked items as a group, keeping their spacing.</summary>
    void SnapPicked()
    {
        if (_scene.ActiveBoard is not { } board) return;
        var picked = board.Items.Where(i => _scene.Picked.Contains(i.Id)).ToList();
        if (picked.Count == 0) return;

        float ax = picked[0].X, ay = picked[0].Y;
        float step = _scene.SnapStep(GridStep);
        float dx = MathF.Round(ax / step) * step - ax;
        float dy = MathF.Round(ay / step) * step - ay;
        if (dx == 0 && dy == 0) return;

        foreach (var it in picked)
        {
            if (Strokes.Is(it)) { Strokes.Move(it, dx, dy); continue; }
            it.X += dx;
            it.Y += dy;
            if (it.Kind == "arrow") { it.X2 += dx; it.Y2 += dy; }
        }
    }

    /// <summary>when the last rubberband was let go, while it fades, or -1.
    /// Worked out from the clock each frame, as the spotlight is. It was a
    /// timer per release that nothing stopped, so a band begun while the last
    /// was still fading was faded to nothing and cleared by the old timer -
    /// invisible until let go, when its own fade began at full.</summary>
    double _bandFadeAt = -1;
    const double BandFade = 0.14;

    void FadeRubberband()
    {
        _bandFadeAt = _clock.Elapsed.TotalSeconds;
        InvalidateVisual();
    }

    void StepRubberband(double now)
    {
        if (_bandFadeAt < 0) return;
        double t = (now - _bandFadeAt) / BandFade;
        _scene.RubberbandFade = (float)Math.Max(0, 1 - t);
        if (t < 1) return;
        _scene.Rubberband = null;
        _bandFadeAt = -1;
    }

    /// <summary>the plain arrow means you are looking; the move cursor means
    /// you can grab things here, which is what edit mode is.</summary>
    /// <summary>outside edit mode the canvas is something you take hold of, so
    /// it gets the open hand, and the closed one while you are holding it.
    /// </summary>
    void ApplyCursor()
    {
        bool panning = !Editing;

        Cursor = _armBrush || _armEraser || _armShape is not null || _armArrow
                ? new Cursor(StandardCursorType.Cross)
            : panning ? (_drag ? Cursors.Closed : Cursors.Open)
            : new Cursor(HandleCursor ?? StandardCursorType.DragMove);
    }

    /// <summary>the resize cursor for the handle under the pointer, or null
    /// when it is not over one. Worked out on hover and left alone during a
    /// drag, so a resize keeps the cursor it started with.</summary>
    public StandardCursorType? HandleCursor { get; private set; }

    /// <summary>which resize cursor, if any, the pointer earns where it is.
    ///
    /// The handles moved the pointer's meaning without saying so: the cursor
    /// stayed the move cross over a corner, so there was no telling a grab
    /// that would resize from one that would drag. Asked in the order a press
    /// resolves in - corner, then wall - so the cursor says what a press does.</summary>
    void HoverHandles(Point p)
    {
        StandardCursorType? want = null;
        bool armed = _armBrush || _armEraser || _armShape is not null || _armArrow;
        if (Editing && !armed && !_scene.BoardReadOnly)
        {
            var (wx, wy) = WorldAt(p);
            if (_scene.GripAt(wx, wy) is { } grip)
                want = grip.Corner switch
                {
                    Scene.GripLeft | Scene.GripTop => StandardCursorType.TopLeftCorner,
                    Scene.GripTop => StandardCursorType.TopRightCorner,
                    Scene.GripLeft => StandardCursorType.BottomLeftCorner,
                    _ => StandardCursorType.BottomRightCorner,
                };
            else if (_scene.EdgeAt(wx, wy) is { } wall)
                want = wall.Edge is Scene.Top or Scene.Bottom
                    ? StandardCursorType.SizeNorthSouth
                    : StandardCursorType.SizeWestEast;
        }
        if (want == HandleCursor) return;
        HandleCursor = want;
        ApplyCursor();
    }

    void Select(int fileIndex, int from, int to)
    {
        _scene.Selection = (fileIndex, Math.Min(from, to), Math.Max(from, to));
        InvalidateVisual();
    }

    /// <summary>the tour narration, centred near the bottom.</summary>
    void DrawCaption(DrawingContext ctx)
    {
        // a toast covers the caption while it lasts, then the caption is back
        var text = ToastShowing ? _toast : _caption;
        if (text.Length == 0) return;

        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Ui.Mono), 17, new SolidColorBrush(Color.FromRgb(0xff, 0xd1, 0x66)));
        double x = (Bounds.Width - ft.Width) / 2;
        double y = Bounds.Height - 78;
        var pad = 14.0;
        ctx.FillRectangle(Ui.PanelBg, new Rect(x - pad, y - 8, ft.Width + pad * 2, ft.Height + 16));
        ctx.DrawText(ft, new Point(x, y));
    }

    void StepBench(float w)
    {
        var p = _phases[_phase];
        if (_phaseFrame == 0)
        {
            _scene.CamS = p.Zoom;
            _scene.CamX = _scene.Data.World.W * 0.1f;
            _scene.CamY = _scene.Data.World.H * 0.5f;
            _samples = [];
            _scene.Recording = false;
        }
        else
        {
            _scene.CamX += p.Dx / p.Zoom;
        }
        _phaseFrame++;

        // first 20 frames are warm-up, same as the web probe
        if (_phaseFrame == 20) { _samples.Clear(); _scene.Recording = true; }
        if (_phaseFrame < p.Frames + 20 || _samples.Count < p.Frames / 2) return;
        _scene.Recording = false;

        _samples.Sort();
        int n = _samples.Count;
        var res = $"zoom {p.Zoom}: median {_samples[n / 2]:F2}ms  p95 {_samples[(int)(n * .95)]:F2}ms  " +
                  $"max {_samples[n - 1]:F2}ms  chunks {_scene.ChunksBuilt}";
        Console.WriteLine("[bench] " + res);
        _benchText = _benchText.Length > 0 ? _benchText + "\n" + res : res;

        _phase++;
        _phaseFrame = 0;
        if (_phase >= _phases.Length)
        {
            _phase = -1;
            _benchDone = true;
            if (_autoBench)
            {
                Console.WriteLine($"[bench] files={_scene.Data.Files.Count} chunks={_scene.ChunksBuilt}");
                Dispatcher.UIThread.Post(() => Environment.Exit(0), DispatcherPriority.Background);
            }
        }
    }

    /// <summary>frame the whole board. The glide goes first: a wheel notch
    /// or a flick still easing the camera would pull it straight back off the
    /// framing, which is how F after a fling kept drifting where it had been
    /// going instead of fitting.</summary>
    void FitBoard()
    {
        _glide.Stop();
        _flight = null;
        _scene.FitBoard((float)Bounds.Width, (float)Bounds.Height);
    }

    void FlyTo(float x, float y, float s)
    {
        // a deliberate move wins over momentum. The flight holds the glide
        // off while it runs, but a flight too short to animate sets the
        // camera directly, and the glide then dragged it away again
        _glide.Stop();
        _flight = Flight.To((float)Bounds.Width, _scene.CamX, _scene.CamY, _scene.CamS, x, y, s);
        _flightT0 = _clock.Elapsed.TotalMilliseconds;
        if (_flight is null) { _scene.CamX = x; _scene.CamY = y; _scene.CamS = s; }
        Focus();   // a panel just handed control back to the canvas
        InvalidateVisual();
    }

    /// <summary>frame a file: card about 70% of the viewport wide, anchored near
    /// its top so you land on the start of the file rather than its middle.</summary>
    public Action? OpenSearch;

    GitReview? _git;
    ReviewOverlay? _reviews;
    ReviewTarget? _target;
    List<CommitInfo> _prCommits = [];
    int _commitAt = -1;          // -1 means the whole pull request

    AnnotationStore? _noteStore;
    AnnotationOverlay? _notes;

    BoardStore? _boardStore;
    BoardOverlay? _boards;
    Board? _lastBoard;

    // the map camera, kept while a board is open so returning is seamless
    (float X, float Y, float S)? _mapCam;
    BoardItem? _dragItem;
    BoardItem? _resizing;
    int _resizeCorner;

    /// <summary>which wall is being dragged, or -1 when it is a corner.</summary>
    int _resizeEdge = -1;
    bool _band;
    readonly List<string> _bandBase = [];
    readonly List<int> _bandBaseFiles = [];
    readonly Dictionary<string, (float X, float Y)> _unsnapped = [];
    Point _bandStart;
    public bool SnapToGrid;
    const float GridStep = 40f;

    /// <summary>editing and wheel-zoom are unrelated switches, not modes of
    /// one another. not editing: either button pans. editing: the primary
    /// button selects and the secondary one pans.</summary>
    public bool Editing { get; private set; }
    public bool WheelZoom { get; private set; } = true;
    public event Action? MouseModeChanged;

    public void SetEditing(bool on)
    {
        // the gathered change view belongs to a commit, not to the repo
        if (on && _scene.BoardReadOnly) { Toast("this view is read only"); return; }
        if (Editing == on) return;
        Editing = on;
        _dragItem = null;
        _armArrow = false;
        _armBrush = false;
        _armEraser = false;
        _armShape = null;
        _scene.Picked.Clear();
        _scene.PickedFiles.Clear();
        _scene.Selection = null;
        _scene.Grid = on ? GridStep : 0;
        RefreshBoardBar();
        RefreshHints();
        ApplyCursor();
        MouseModeChanged?.Invoke();
        InvalidateVisual();
    }

    public void SetWheelZoom(bool on)
    {
        if (WheelZoom == on) return;
        WheelZoom = on;
        MouseModeChanged?.Invoke();
        // the mode island says this too, but a side panel covers it - and
        // in review mode, where the commits panel is always open, the only
        // indication that the key had done anything was invisible
        Toast(on ? "wheel zooms" : "wheel scrolls");
    }
    int _clickCount = 1;
    bool _secondary;
    bool _armArrow;
    bool _armBrush, _armEraser, _erasing;
    SkiaSharp.SKPoint _shapeFrom;

    /// <summary>what the brush draws with. Null is the default ink.</summary>
    public string? PenColor;

    /// <summary>thickness in board units. A ladder rather than a slider: the
    /// useful widths are few and far apart, and stepping through them is
    /// faster than aiming at one.</summary>
    static readonly float[] Weights = [1f, 2f, 3f, 5f, 8f, 13f, 20f];
    int _weightAt = 2;
    float PenWeight => Weights[_weightAt];

    void StepWeight(int by)
    {
        if (_scene.ActiveBoard is null || _scene.BoardReadOnly) return;
        _weightAt = Math.Clamp(_weightAt + by, 0, Weights.Length - 1);

        // a selection makes it mean "make those this thick" as well - and
        // that is every line on the board, not just the inked ones: a shape's
        // border and an arrow's shaft are the same idea as a pen width
        SetLineWidth(PickedLines(), PenWeight);
        RefreshBoardBar();
        Toast($"line {PenWeight:0.#}");
    }

    void SetPenColour(string hex)
    {
        PenColor = hex;
        // a swatch with a shape picked means its border, the same way it
        // means a stroke's colour: one control, one meaning - "this colour"
        ApplyToPicked(i => Strokes.Is(i) || Scene.IsShape(i.Kind), it => it.Color = hex);
        RefreshBoardBar();
    }

    void ApplyToPickedStrokes(Action<BoardItem> change) => ApplyToPicked(Strokes.Is, change);

    /// <summary>everything picked that is drawn with a line. A note has a
    /// border all the way round it now, which makes it one of these.</summary>
    static bool HasLineWidth(BoardItem it) =>
        Strokes.Is(it) || Scene.IsShape(it.Kind) || it.Kind is "arrow" or "note";

    static bool HasText(BoardItem it) => Scene.HasText(it.Kind) || it.Kind == "arrow";

    List<BoardItem> PickedLines() =>
        _scene.ActiveBoard is not { } b
            ? []
            : b.Items.Where(i => HasLineWidth(i) && _scene.Picked.Contains(i.Id)).ToList();

    /// <summary>a stroke has to be reframed afterwards: its box is its ink's
    /// bounds and a fatter pen spills past them.</summary>
    void SetLineWidth(List<BoardItem> picked, float width)
    {
        if (picked.Count == 0) return;
        Remember();
        foreach (var it in picked)
        {
            it.Weight = width;
            if (Strokes.Is(it)) Strokes.Reframe(it);
        }
        if (_scene.ActiveBoard is { } b) _boardStore?.Save(b);
        Saved($"line {width:0.#}");
        InvalidateVisual();
    }

    /// <summary>apply to everything picked that the tool means something for,
    /// and save. Nothing picked means the change is only to the tool.</summary>
    void ApplyToPicked(Func<BoardItem, bool> applies, Action<BoardItem> change)
    {
        if (_scene.ActiveBoard is not { } board) return;
        var picked = board.Items.Where(i => applies(i) && _scene.Picked.Contains(i.Id)).ToList();
        if (picked.Count == 0) return;

        Remember();
        foreach (var it in picked) change(it);
        _boardDirty = true;
        InvalidateVisual();
    }
    InlineEditor? _editor;

    public void AttachEditor(InlineEditor editor) => _editor = editor;

    /// <summary>type into an item where it sits. Anything with words: a note,
    /// a label, or a shape - a shape with no text yet is being given some,
    /// which is how a box becomes a box that says "parse".</summary>
    void BeginEdit(BoardItem it)
    {
        if (_editor is null || _scene.ActiveBoard is not { } board) return;
        if (_scene.BoardReadOnly) { Toast("this view is read only"); return; }
        // the one place that opens an editor, so the one place that has to
        // know a board being read is not a board being written
        if (!Editing) { Toast("press E to edit this board"); return; }
        // a window's words are its title, in its header
        if (!HasText(it) && it.Kind != "file") return;

        // stale from whatever was edited last; the draw loop refills it
        _scene.EditingHeight = 0;
        _scene.EditingItem = it.Id;
        var (x, y, w, h, size) = EditorRect(it);
        _editor.Begin(it.Id, it.Text ?? "", x, y, w, h, size, text =>
        {
            _scene.EditingItem = null;
            if (text != (it.Text ?? ""))
            {
                Remember();
                it.Text = text;
                _boardStore?.Save(board);
                Saved(Named(it.Kind));
            }
            // an empty label is nothing at all - no panel, no border, no
            // words - so it would be an invisible thing to trip over later
            if (it.Kind == "text" && string.IsNullOrWhiteSpace(text))
            {
                _scene.Remove(board, [it]);
                _boardStore?.Save(board);
            }
            Focus();
            InvalidateVisual();
        });
        InvalidateVisual();
    }

    bool _placeQueued;

    /// <summary>ask for the editor to be moved, after this frame.
    ///
    /// Placing writes layout properties, and writing one from inside the
    /// render pass throws "Visual was invalidated during the render pass" -
    /// every frame, for as long as you pan with an editor open. The camera
    /// moves during Render (the glide is stepped there), so the placement
    /// cannot simply be moved to wherever the camera changes; it is posted
    /// instead. The box lands a frame behind the canvas while panning, which
    /// nobody can see, and never throws.</summary>
    void QueuePlaceEditor()
    {
        if (_placeQueued) return;
        _placeQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _placeQueued = false;
            if (EditingItem() is { } it) PlaceEditor(it);
        }, DispatcherPriority.Background);
    }

    /// <summary>put the editor over the item it is editing, in screen space.
    /// The canvas can pan and zoom underneath an open editor, and a box that
    /// stayed put would be editing one thing while pointing at another.</summary>
    void PlaceEditor(BoardItem it)
    {
        if (_editor is null) return;
        var (x, y, w, h, size) = EditorRect(it);
        _editor.Place(x, y, w, h, size);
    }

    /// <summary>where an item is on screen, and how big its type is there.
    ///
    /// Deliberately arithmetic only. Scene.ItemHeight would be the obvious
    /// way to get the height and it is the one thing that must not happen
    /// here: for a note it wraps the text, wrapping measures it with
    /// SkiaSharp, and this runs on the UI thread while the render thread is
    /// inside Scene.Draw doing the same with the same typeface. Two threads
    /// in Skia's text path is not an exception, it is the process
    /// disappearing. The draw loop leaves the height in EditingHeight.</summary>
    (double X, double Y, double W, double H, double Size) EditorRect(BoardItem it)
    {
        float s = _scene.CamS;
        float h = _scene.EditingHeight > 0 ? _scene.EditingHeight
            : it.H > 0 ? it.H
            : 64;
        if (it.Kind == "file")
            return (
                (it.X - _scene.CamX) * s + Bounds.Width / 2,
                (it.Y - _scene.CamY) * s + Bounds.Height / 2,
                it.W * s,
                Scene.WinHeadH * s,
                14 * s);
        if (it.Kind == "arrow")
        {
            // an arrow's box is a phantom; its label sits on the middle of
            // the shaft, so the editor does too. ArrowEnds is arithmetic -
            // safe here, unlike anything that measures
            var (a, b) = _scene.ArrowEnds(it);
            float lh = _scene.EditingHeight > 0 ? _scene.EditingHeight : Scene.LineStep(Scene.SizeOf(it));
            const float Wide = 220;
            return (
                ((a.X + b.X) / 2 - Wide / 2 - _scene.CamX) * s + Bounds.Width / 2,
                ((a.Y + b.Y) / 2 - lh / 2 - _scene.CamY) * s + Bounds.Height / 2,
                Wide * s,
                lh * s,
                Scene.SizeOf(it) * s);
        }
        return (
            (it.X - _scene.CamX) * s + Bounds.Width / 2,
            (it.Y - _scene.CamY) * s + Bounds.Height / 2,
            it.W * s,
            h * s,
            Scene.SizeOf(it) * s);
    }

    BoardItem? EditingItem() =>
        _scene.EditingItem is { } id && _scene.ActiveBoard is { } b
            ? b.Items.FirstOrDefault(i => i.Id == id)
            : null;

    BoardItem? _arrowEnd;
    int _arrowEndWhich;
    ContextMenu? _menu;
    bool _boardDirty;

    // file under the cursor on the map, and the boards that reference it

    PromptOverlay? _prompt;
    string _caption = "";

    public void AttachPrompt(PromptOverlay prompt) => _prompt = prompt;

    TourPanel? _stops;

    /// <summary>the board whose tour is playing, and the stop it is on.</summary>
    Board? _tour;
    int _stop = -1;

    CommitsPanel? _commitsPanel;
    HintBar? _hints;
    BoardBar? _boardBar;

    public void AttachBoardBar(BoardBar bar)
    {
        _boardBar = bar;
        bar.Add += AddOfKind;
        bar.ToggleSnap += () =>
        {
            SnapToGrid = !SnapToGrid;
            _boardBar?.Reflect(_scene.ActiveBoard is not null && Editing, SnapToGrid);
        };
    }

    BackButton? _back;

    public void AttachBack(BackButton back)
    {
        _back = back;
        back.Clicked += () => { LeaveBoard(); Focus(); };
    }

    PenBar? _penBar;
    EraserBar? _eraserBar;

    public void AttachEraserBar(EraserBar bar)
    {
        _eraserBar = bar;
        bar.SizeStepped += by => { StepEraser(by); Focus(); };
        bar.ModePicked += split => { SetEraseMode(split); Focus(); };
    }

    /// <summary>[ and ] mean "smaller" and "bigger" for whatever is in hand.</summary>
    void StepTool(int by)
    {
        if (_armEraser) StepEraser(by);
        else StepWeight(by);
    }

    public void AttachPenBar(PenBar bar)
    {
        _penBar = bar;
        bar.ColourPicked += hex => { SetPenColour(hex); Focus(); };
        bar.WeightStepped += by => { StepWeight(by); Focus(); };
    }

    void RefreshBoardBar()
    {
        bool editing = _scene.ActiveBoard is not null && Editing;
        _boardBar?.Reflect(editing, SnapToGrid,
            _armShape ?? (_armBrush ? "brush" : _armEraser ? "eraser" : _armArrow ? "arrow" : null));
        _back?.Reflect(_scene.ActiveBoard is not null, _scene.ActiveBoard?.Name);
        // each tool shows its own settings, and only while it is armed
        _penBar?.Reflect(editing && _armBrush, PenWeight, PenColor);
        _eraserBar?.Reflect(editing && _armEraser, EraserRadius, _splitErase);
    }

    void AddOfKind(string kind)
    {
        switch (kind)
        {
            case "note": AddNote(); break;
            case "shape": AddShape("shape"); break;
            case "ellipse": AddShape("ellipse"); break;
            case "diamond": AddShape("diamond"); break;
            case "text": AddLabel(); break;
            case "arrow": AddArrow(); break;
            case "brush": ArmBrush(); break;
            case "eraser": ArmEraser(); break;
            case "file": OpenSearch?.Invoke(); break;
        }
    }

    /// <summary>arms the brush: the next drag draws freehand. Staying armed
    /// after a stroke is the point - you draw several in a row, and reaching
    /// for the key between each one is the thing that makes a drawing tool
    /// unusable.</summary>
    void ArmBrush()
    {
        if (_scene.ActiveBoard is null || _scene.BoardReadOnly) return;
        if (!Editing) SetEditing(true);
        _armEraser = false;
        _armBrush = !_armBrush;
        _armArrow = false;
        _scene.Picked.Clear();
        RefreshBoardBar();
        Toast(_armBrush ? "brush on - drag to draw, B to stop" : "brush off");
    }

    /// <summary>X cycles: off, whole elements, splitting strokes, off.
    ///
    /// A separate key for the mode was a key nobody would find, and its only
    /// feedback was a message. Walking the modes with the same key you turned
    /// it on with means the tool teaches itself, and the panel says which one
    /// you are in.</summary>
    void ArmEraser()
    {
        if (_scene.ActiveBoard is null || _scene.BoardReadOnly) return;
        if (!Editing) SetEditing(true);
        _armBrush = false;
        _armArrow = false;

        if (!_armEraser) { _armEraser = true; _splitErase = false; }
        else if (!_splitErase) _splitErase = true;
        else { _armEraser = false; _splitErase = false; }

        _scene.Picked.Clear();
        RefreshBoardBar();
        ApplyCursor();
        Toast(_armEraser
            ? _splitErase ? "eraser splits strokes" : "eraser takes whole elements"
            : "eraser off");
    }

    void SetEraseMode(bool split)
    {
        if (!_armEraser) ArmEraser();
        _splitErase = split;
        RefreshBoardBar();
        Toast(split ? "eraser splits strokes" : "eraser takes whole elements");
    }

    /// <summary>how wide the rub is, in screen pixels.</summary>
    static readonly float[] EraserSizes = [6f, 10f, 14f, 22f, 34f, 52f];
    int _eraserAt = 2;
    float EraserRadius => EraserSizes[_eraserAt];

    void StepEraser(int by)
    {
        _eraserAt = Math.Clamp(_eraserAt + by, 0, EraserSizes.Length - 1);
        RefreshBoardBar();
        Toast($"eraser {EraserRadius:0}");
    }

    /// <summary>true when the eraser takes a bite out of a stroke rather than
    /// a whole element.</summary>
    bool _splitErase;

    /// <summary>rub something out.
    ///
    /// Whole elements is the blunt one: anything the eraser passes over goes,
    /// note or shape or window or stroke. Splitting is for ink only - taking
    /// a bite out of a note is not a thing a note can survive - so in that
    /// mode nothing but strokes is touched.</summary>
    void EraseAt(float wx, float wy)
    {
        if (_scene.ActiveBoard is not { } board) return;
        float radius = EraserRadius / _scene.CamS;

        var hit = _splitErase
            ? _scene.StrokesNear(wx, wy, radius)
            : _scene.ItemsNear(wx, wy, radius);
        if (hit.Count == 0) return;

        // one history entry for the whole rub, not one per element
        if (!_erasing) { Remember(); _erasing = true; }

        foreach (var it in hit)
        {
            int at = board.Items.IndexOf(it);
            _scene.Remove(board, [it]);

            if (!_splitErase) continue;

            // put the surviving pieces back where the stroke was, so erasing
            // through the middle of something does not bring it to the front
            var left = Strokes.Erase(it, wx, wy, radius);
            for (int i = 0; i < left.Count; i++)
                board.Items.Insert(Math.Min(at + i, board.Items.Count), left[i]);
        }
        _boardDirty = true;
        InvalidateVisual();
    }


    /// <summary>an arrow needs two picked items: it joins them and follows
    /// them when either is moved.</summary>
    /// <summary>arms the arrow tool: the next drag draws one.</summary>
    void AddArrow()
    {
        if (_scene.ActiveBoard is null || _scene.BoardReadOnly) return;
        if (!Editing) SetEditing(true);
        _armBrush = _armEraser = false;
        _armArrow = true;
        _scene.ShowAnchors = true;
        _scene.Picked.Clear();
        RefreshBoardBar();
        ApplyCursor();
        Toast("drag to draw - onto a side of a box, it ties there");
    }

    /// <summary>tie two picked items together. The same connector a drag from
    /// one to the other makes, for when they are already both picked.</summary>
    void Connect(BoardItem from, BoardItem to)
    {
        if (_scene.ActiveBoard is not { } board || _scene.BoardReadOnly) return;
        if (ReferenceEquals(from, to)) return;

        Remember();
        board.Items.Add(new BoardItem
        {
            Id = BoardStore.NewId(), Kind = "arrow",
            From = from.Id, To = to.Id, Color = PenColor,
            // a fallback for if either is ever cut
            X = from.X + from.W / 2, Y = from.Y,
            X2 = to.X + to.W / 2, Y2 = to.Y,
        });
        _boardStore?.Save(board);
        Saved("connector");
    }

    public void AttachHints(HintBar bar)
    {
        _hints = bar;
        RefreshHints();
    }

    /// <summary>the buttons available right now. each runs the same code its
    /// key runs, so an action has only one implementation.</summary>
    public void RefreshHints()
    {
        if (_hints is null) return;
        var items = new List<(string, string, Action)>();

        if (_scene.BoardReadOnly)
        {
            items.Add(("previous commit", "[", () => { StepCommit(-1); RebuildChangeBoard(); }));
            items.Add(("next commit", "]", () => { StepCommit(1); RebuildChangeBoard(); }));
            items.Add(("commits", "H", ToggleCommits));
            items.Add(("fit", "F", () => { FitBoard(); InvalidateVisual(); }));
            items.Add((_showRemoved ? "hide removed" : "show removed", "R", ToggleRemoved));
            items.Add(("back to the map", "C", LeaveBoard));
        }
        else if (_scene.ActiveBoard is not null)
        {
            items.Add(("undo", "ctrl+Z", Undo));
            items.Add(("redo", "ctrl+Y", Redo));
            items.Add(("workspace", "tab", ToggleWorkspace));
            items.Add(("tour stop", "M", CaptureStop));
            items.Add(("tour", "shift+M", ToggleTourPanel));
            items.Add(("play", "P", () => PlayTour(0)));
            items.Add(("fit", "F", () => { FitBoard(); InvalidateVisual(); }));
            items.Add(("back to map", "alt+←", LeaveBoard));
        }
        else if (_scene.Review is not null)
        {
            items.Add(("previous commit", "[", () => StepCommit(-1)));
            items.Add(("next commit", "]", () => StepCommit(1)));
            items.Add(("commits", "H", ToggleCommits));
            items.Add((_showRemoved ? "hide removed" : "show removed", "R", ToggleRemoved));
            items.Add(("changed code", "C", ToggleChangeBoard));
            items.Add(("leave review", "esc", LeaveReview));
        }
        else
        {
            items.Add(("search", "/", () => OpenSearch?.Invoke()));
            items.Add(("pull requests", "P", () => OpenReviewPanel(branches: false)));
            items.Add(("branches", "G", () => OpenReviewPanel(branches: true)));
            items.Add(("workspace", "tab", ToggleWorkspace));
            items.Add(("notes", "L", OpenNotes));
            items.Add(("fit", "F", FitAll));
        }
        _hints.Set(items);
    }

    void FitAll()
    {
        float w = _scene.Data.World.W, h = _scene.Data.World.H;
        float s = Math.Min((float)Bounds.Width / w, (float)Bounds.Height / h) * 0.92f;
        FlyTo(w / 2, h / 2, s);
    }

    public void AttachReview(ReviewOverlay panel, CommitsPanel commits)
    {
        _reviews = panel;
        panel.Chosen += ChooseTarget;
        _commitsPanel = commits;
        commits.Picked += GoToCommit;
    }

    void GoToCommit(int index)
    {
        if (_git is null || _target is null) return;
        if (index == _commitAt) return;
        ShowCommit(Math.Clamp(index, -1, _prCommits.Count - 1));
    }

    void OpenReviewPanel(bool branches)
    {
        if (_reviews is null) return;
        _git ??= GitReview.Open(_scene.Data.Root);
        if (_git is null) { Toast("this repo is not under git"); return; }

        // the panel now, the list when it is read: off the UI thread, since
        // finding merged pull requests walks hundreds of commits
        var what = branches ? "branches" : "pull requests";
        int turn = ++_listing;
        _reviews.Loading(what);
        _caption = "";
        var git = _git;
        Task.Run(() => (Targets: branches ? git.Branches() : git.MergedPrs(), Head: git.HeadName))
            .ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                if (turn != _listing || _reviews is null || !Reveal.Showing(_reviews)) return;
                if (t.IsFaulted)
                {
                    _reviews.Show([], "", what);
                    Toast($"could not read the {what}: {t.Exception?.GetBaseException().Message}");
                    return;
                }
                _reviews.Show(t.Result.Targets, t.Result.Head, what);
                if (!branches) AskForOpenPrs(t.Result.Targets);
            }));
    }

    int _listing;

    int _askingPrs;

    /// <summary>open pull requests are not in the local history at all, so
    /// they come from GitHub, which takes a moment: the merged ones are shown
    /// straight away and the open ones join them at the top when they arrive.</summary>
    void AskForOpenPrs(List<ReviewTarget> merged)
    {
        int turn = ++_askingPrs;
        var root = _scene.Data.Root;
        var branch = _git?.HeadName ?? "";
        Task.Run(() => GitReview.OpenPrs(root)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (turn != _askingPrs || _reviews is null || !Reveal.Showing(_reviews)) return;
            if (t.Result is not { } open)
            {
                Toast("open pull requests come from GitHub: needs the gh tool, signed in");
                return;
            }
            _reviews.Show([.. open.Select(GitReview.RowFor), .. merged], branch, "pull requests");
        }));
    }

    /// <summary>pick something from the review panel. An open pull request
    /// may need its commits fetched first, which is network and so off the
    /// UI thread, and so is reading it once it is here (OpenTarget).</summary>
    void ChooseTarget(ReviewTarget chosen)
    {
        if (chosen.Open is not { } pr || _git is null) { OpenTarget(chosen); return; }
        if (_git.TargetFor(pr) is { } here) { OpenTarget(here); return; }

        _caption = $"#{pr.Number}   -   fetching its commits...";
        InvalidateVisual();
        var root = _scene.Data.Root;
        Task.Run(() => GitReview.FetchPr(root, pr.Number)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            _caption = "";
            if (_git?.TargetFor(pr) is { } fetched) OpenTarget(fetched);
            else Toast($"could not fetch #{pr.Number} - try git fetch origin pull/{pr.Number}/head");
            InvalidateVisual();
        }));
    }

    int _opening;

    /// <summary>open a branch or a pull request for review.
    ///
    /// The reading is all synchronous - libgit2's repository handle is not
    /// thread safe, and the bracket keys reach for the same handle - so the
    /// window does stop for a moment on a large tree. What it must not do
    /// is stop while showing nothing, which is what it did: the caption was
    /// set and then the work ran before any frame could be painted. The
    /// panel goes up first and a frame is allowed through, then the work.</summary>
    public void OpenTarget(ReviewTarget target)
    {
        _git ??= GitReview.Open(_scene.Data.Root);
        if (_git is null) return;

        int turn = ++_opening;
        _reading = true;
        _commitsPanel?.Loading(target.Label);
        _caption = $"{target.Label}   -   reading the tree at this commit...";
        InvalidateVisual();

        // the commits, the diff and every file of the tree at that commit,
        // read off the UI thread: on a big repo that is seconds, and the
        // window used to freeze for all of them. GitReview serialises its
        // handle, so a key that reaches for git meanwhile waits its turn
        var git = _git;
        var opts = _scanOptions;
        bool removed = _showRemoved;
        var root = _scene.Data.Root;
        Task.Run(() =>
        {
            try
            {
                var commits = git.CommitsOf(target);
                var whole = git.Whole(target);
                var tree = BuildTree(git, target, whole, opts, removed, root);
                Dispatcher.UIThread.Post(() =>
                {
                    if (turn != _opening || _git is null) return;   // superseded
                    _reading = false;
                    _target = target;
                    _prCommits = commits;
                    _commitAt = -1;
                    _whole = whole;
                    ++_stepping;
                    if (tree is { } t) _scene.ShowSnapshot(t.Data, path => t.Text.GetValueOrDefault(path), t.Splices);
                    _commitsPanel?.Show(target.Label, _prCommits);
                    ShowChanges(whole);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (turn != _opening) return;
                    _reading = false;
                    _caption = "";
                    Toast($"could not read {target.Label}: {ex.Message}");
                });
            }
        });
    }

    /// <summary>the repo laid out as it was at the target's head. Without this
    /// a branch older than a restructure changes paths that no longer exist,
    /// and lights up nothing at all.
    ///
    /// With removed lines shown, each changed file's text has what the whole
    /// change took out put back where it was, so the cards contain it: the
    /// snapshot is temporary anyway, thrown away when review ends. Built once
    /// for the whole change - stepping through the commits keeps it, rather
    /// than rebuilding a layout that would shift under you at every step.
    /// Nothing here touches Skia or the scene, so it runs on any thread.</summary>
    static (Scan Data, Dictionary<string, string[]> Text, Dictionary<string, Splice> Splices)? BuildTree(
        GitReview git, ReviewTarget target, ChangeSet? whole, ScanOptions opts, bool removed, string root)
    {
        // the same rule the working tree is filtered by, toggle included
        var snapshot = git.Snapshot(target.HeadSha, path => Scanner.Wanted(path, opts));
        if (snapshot is null || snapshot.Count == 0) return null;

        var (text, splices) = removed && whole is not null
            ? Splice.All(snapshot, whole)
            : (snapshot, new Dictionary<string, Splice>());
        return (Scanner.BuildFrom(root, text), text, splices);
    }

    /// <summary>show or hide the commit list. It is a full height strip down
    /// one side, which is worth it while you are stepping through commits
    /// and in the way when you want to see what is under it.</summary>
    void ToggleCommits()
    {
        if (_commitsPanel is not { } panel || _scene.Review is null) return;
        if (Reveal.Showing(panel)) { panel.Close(); Toast("commits hidden - H"); }
        else panel.Reopen();
        InvalidateVisual();
    }

    /// <summary>-1 shows the whole pull request, 0..n a single commit.</summary>
    void StepCommit(int by)
    {
        if (_git is null || _target is null || _prCommits.Count == 0) return;
        int next = Math.Clamp(_commitAt + by, -1, _prCommits.Count - 1);
        if (next == _commitAt) return;
        ShowCommit(next);
    }

    /// <summary>the whole change of the target that is open, as read when it
    /// was opened. Stepping back to it is the slowest diff there is - every
    /// commit at once - and it was worked out again on every visit.</summary>
    ChangeSet? _whole;

    int _stepping;

    /// <summary>show one commit, or the whole change for -1. One commit's
    /// diff is read off the UI thread; holding a key down steps faster than
    /// git answers, so a step overtaken before it starts does no work and
    /// one that lands late is dropped - only the last one shows.</summary>
    void ShowCommit(int at)
    {
        if (_git is null || _target is null) return;
        _commitAt = at;
        int turn = ++_stepping;
        if (at < 0 && _whole is not null) { ShowChanges(_whole); return; }

        var git = _git;
        var target = _target;
        var commit = at >= 0 ? _prCommits[at] : null;
        Task.Run(() => Volatile.Read(ref _stepping) != turn ? null
                : commit is null ? git.Whole(target) : git.OfCommit(commit))
            .ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                if (turn != _stepping || _target != target) return;
                if (t.IsFaulted) { Toast($"could not read that commit: {t.Exception?.GetBaseException().Message}"); return; }
                ShowChanges(t.Result);
            }));
    }

    void ShowChanges(ChangeSet? set)
    {
        if (set is null) return;
        // into the rows of the spliced text, if the removed lines are in it
        set = Splice.Remap(set, _scene.Splices, whole: _commitAt < 0);
        _scene.Review = set;
        _scene.Highlight = null;

        // the details live in the commits panel now; a log line at the bottom
        // of the canvas was impossible to follow
        _caption = "";
        int placed = set.Files.Count(f => _scene.IndexOfPath(f.Path) >= 0);
        _commitsPanel?.Sync(_commitAt, set, placed, _scene.OnSnapshot);
        RefreshHints();

        // every route to a different change set comes through here, so this
        // is the only place the gathered board can be rebuilt without one of
        // them being forgotten - and one of them was. Picking a commit in
        // the panel left the board showing the previous commit's files, and
        // only the windows both commits happened to share changed at all
        if (_scene.BoardReadOnly) { RebuildChangeBoard(); return; }

        var w = (float)Bounds.Width;
        var h = (float)Bounds.Height;
        float camX = _scene.CamX, camY = _scene.CamY, camS = _scene.CamS;
        _scene.FitChanges(w, h);
        float toX = _scene.CamX, toY = _scene.CamY, toS = _scene.CamS;
        _scene.CamX = camX; _scene.CamY = camY; _scene.CamS = camS;
        FlyTo(toX, toY, toS);
    }

    void LeaveReview()
    {
        if (_scene.Review is null) return;
        _scene.Review = null;
        _scene.ShowLive();
        _commitsPanel?.Close();
        RefreshHints();
        _target = null;
        _whole = null;
        _prCommits = [];
        _commitAt = -1;
        _caption = "";
        InvalidateVisual();
    }

    GrepOverlay? _grep;
    CancellationTokenSource? _grepping;

    List<Found> _found = [];
    int _foundAt = -1;
    DispatcherTimer? _grepSoon;

    public void AttachGrep(GrepOverlay panel)
    {
        _grep = panel;
        panel.Typed += HighlightAs;
        panel.Requested += QueueGrep;
        panel.Stepped += StepMatch;
        panel.Picked += GoToMatch;
    }

    /// <summary>light up what is already on screen, on every keystroke.
    ///
    /// This costs a substring search of the lines being drawn, so it can
    /// run at typing speed - unlike the list, which has to read files.</summary>
    void HighlightAs(string query)
    {
        _scene.Find = Grep.Pattern(query, _grep?.Regex ?? false, _grep?.Word ?? false);
        _scene.FindAt = null;
        InvalidateVisual();
    }

    /// <summary>and the list after a pause. Every keystroke would start a
    /// read of every file that is not already loaded, and "cl" on the way
    /// to "ClampCamera" matches most of the repo.</summary>
    void QueueGrep(string query)
    {
        _grepSoon?.Stop();
        _grepSoon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _grepSoon.Tick += (_, _) =>
        {
            _grepSoon?.Stop();
            _grepSoon = null;
            RunGrep(query);
        };
        _grepSoon.Start();
    }

    /// <summary>the next match, or the previous one. Works with the panel
    /// closed: the matches belong to the view, and the panel is a list of
    /// them rather than the thing that holds them.</summary>
    void StepMatch(int by)
    {
        if (_found.Count == 0) { Toast("nothing to step through"); return; }
        _foundAt = _foundAt < 0
            ? (by > 0 ? 0 : _found.Count - 1)
            : (_foundAt + by + _found.Count) % _found.Count;

        _grep?.Select(_foundAt);
        GoToMatch(_found[_foundAt]);
    }

    /// <summary>stop highlighting. The innermost thing Escape can peel off
    /// once the panel itself has gone, so the marks do not outlive the
    /// search that made them.</summary>
    void ClearFind()
    {
        _scene.Find = null;
        _scene.FindAt = null;
        _found = [];
        _foundAt = -1;
        // and what going to a match left behind, or Escape would clear the
        // marks and leave the line it had picked out still lit
        _scene.Highlight = null;
        _scene.Selection = null;
        _caption = "";
        InvalidateVisual();
    }

    void OpenGrep()
    {
        if (_grep is null) return;
        _found = [];
        _foundAt = -1;
        _scene.FindAt = null;
        _grep.Open(GrepScope().Where);
        InvalidateVisual();
    }

    /// <summary>what a search covers. On a board that is the files it has
    /// windows onto - a board is a chosen subset of the repo, and searching
    /// the whole repo from one would answer a question nobody asked.</summary>
    (HashSet<string>? Only, string Where) GrepScope()
    {
        if (_scene.ActiveBoard is not { } board)
            return (null, $"{_scene.Data.Files.Count} files");

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var it in board.Items)
            if (it.Kind == "file" && it.File is { } p) paths.Add(p);
        return (paths, paths.Count == 1 ? "1 file on this board" : $"{paths.Count} files on this board");
    }

    /// <summary>run it off the UI thread. Reading a repo's worth of files is
    /// not a thing to do between two frames, and a search that has been
    /// superseded by another keystroke stops rather than finishing.</summary>
    void RunGrep(string query)
    {
        if (_grep is null) return;

        _grepping?.Cancel();
        var cts = new CancellationTokenSource();
        _grepping = cts;

        var (only, where) = GrepScope();
        bool regex = _grep.Regex, word = _grep.Word;
        if (query.Length == 0 || Grep.Pattern(query, regex, word) is null)
        {
            _found = [];
            _foundAt = -1;
            _grep.Show([], query, where, false);
            return;
        }

        _grep.Searching();
        var scan = _scene.Data;
        var scene = _scene;
        Task.Run(() =>
        {
            // ReadLines is safe here: it reads a concurrent dictionary, or a
            // file, or the commit snapshot - which is a plain dictionary
            // built before the search. No Skia, and no libgit2
            var found = Grep.Run(scan, query, scene.ReadLines, only, cancel: cts.Token, regex: regex, word: word);
            if (cts.IsCancellationRequested) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (cts.IsCancellationRequested || _grep is null) return;
                found = PerWindow(found);
                _found = found;
                _foundAt = -1;
                _grep.Show(found, query, where, found.Count >= Grep.Limit);
                InvalidateVisual();
            });
        }, cts.Token);
    }

    /// <summary>re-pin everything that was just dragged. A multi-selection
    /// moves as one, so every member of it may have landed somewhere new.</summary>
    void RepinPicked()
    {
        if (_scene.ActiveBoard is not { } board) return;
        foreach (var it in board.Items)
            if (_scene.Picked.Contains(it.Id)) { _scene.PinOver(it); _boardDirty = true; }
    }

    /// <summary>how far to push the view down so the match lands below the
    /// results, in world units.
    ///
    /// Centring it puts the line under the panel, which is the one place it
    /// must not be: you walk the list to look at the matches, and the list
    /// was sitting on top of every one of them.</summary>
    float Uncovered(float scale)
    {
        if (_grep is not { } panel || !Reveal.Showing(panel)) return 0;
        double covered = panel.Margin.Top + panel.Bounds.Height;
        // negative: raising the camera's Y moves the world *up* the screen,
        // and the line needs to come down. Half, because the camera centres -
        // this moves the line from the middle of the window to the middle of
        // what is left below the panel
        return -(float)Math.Min(covered, Bounds.Height * 0.7) / 2 / Math.Max(scale, 0.0001f);
    }

    /// <summary>go and look at a match: the line framed, highlighted and
    /// selected, on the map or inside the board window showing that file.</summary>
    public void GoToMatch(Found m)
    {
        int i = _scene.ResolveFile(m.Path, null);
        if (i < 0) { Toast($"{m.Path} is not in this scan"); return; }

        // so the one being looked at is drawn differently from the rest
        _scene.FindAt = (i, m.Line, m.Col);
        int seen = _found.IndexOf(m);
        if (seen >= 0) _foundAt = seen;

        if (_scene.ActiveBoard is not null) { ShowMatchOnBoard(m, i); return; }

        var f = _scene.Data.Files[i];
        var target = Places.Resolve(_scene, new Place
        {
            Name = m.Text, File = f.P,
            Line = Math.Max(0, m.Line - 12),
            EndLine = Math.Min(Math.Max(0, f.N - 1), m.Line + 12),
        }, (float)Bounds.Width, (float)Bounds.Height);

        _scene.Highlight = (i, m.Line, m.Line);
        Select(i, m.Line, m.Line);
        FlyTo(target.X, target.Y + Uncovered(target.S), target.S);
        _caption = $"{f.P}:{m.Line + 1}";
        InvalidateVisual();
    }

    /// <summary>on a board, one entry per window that shows the line. Two
    /// windows onto one file, cropped to two methods, are two places to look,
    /// and every match used to be sent to whichever window was made first -
    /// clamped to its range, so a match in the second was never reached.
    /// A match no window shows is kept once, and goes to the nearest.</summary>
    public List<Found> PerWindow(List<Found> found)
    {
        if (_scene.ActiveBoard is not { } board) return found;
        var windows = board.Items
            .Where(it => it.Kind == "file" && it.File is not null)
            .GroupBy(it => _scene.ResolveFile(it.File!, it.Key))
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<Found>(found.Count);
        foreach (var m in found)
        {
            if (!windows.TryGetValue(m.File, out var mine)) { result.Add(m); continue; }
            var f = _scene.Data.Files[m.File];
            int before = result.Count;
            for (int n = 0; n < mine.Count; n++)
            {
                var (from, to) = _scene.RangeOf(mine[n], f);
                if (m.Line >= from && m.Line <= to)
                    result.Add(m with { Window = mine[n].Id, Copy = mine.Count > 1 ? n + 1 : 0 });
            }
            if (result.Count == before) result.Add(m);
        }
        return result;
    }

    void ShowMatchOnBoard(Found m, int fileIndex)
    {
        if (_scene.ActiveBoard is not { } board) return;
        var f = _scene.Data.Files[fileIndex];

        var windows = board.Items.Where(
            it => it.Kind == "file" && it.File is not null &&
                  _scene.ResolveFile(it.File, it.Key) == fileIndex).ToList();
        var window = windows.FirstOrDefault(it => it.Id == m.Window)
            ?? windows.MinBy(it =>
            {
                var (a, b) = _scene.RangeOf(it, f);
                return m.Line < a ? a - m.Line : m.Line > b ? m.Line - b : 0;
            });
        if (window is null) { Toast($"no window onto {m.Path} on this board"); return; }

        var (from, to) = _scene.RangeOf(window, f);
        int line = Math.Clamp(m.Line, from, to);

        // a window scales its card, so a line's height on the board is not
        // the file's line height
        float k = window.W / f.W;
        float y = window.Y + Scene.WinHeadH + (line - from + 0.5f) * _scene.Data.LineH * k;

        _scene.Highlight = (fileIndex, m.Line, m.Line);
        Select(fileIndex, m.Line, m.Line);
        float s = Math.Max(_scene.CamS, 1f);
        GlideTo(window.X + window.W / 2, y + Uncovered(s), s);
        _caption = m.Line >= from && m.Line <= to
            ? $"{m.Path}:{m.Line + 1}"
            : $"{m.Path}:{m.Line + 1}   (outside this window's lines)";
        InvalidateVisual();
    }

    public void AttachNotes(AnnotationStore store, AnnotationOverlay panel)
    {
        _noteStore = store;
        _notes = panel;
        panel.ScopeRequested += (notes, local) => SetAnnotationScope(notes, local);
    }

    /// <summary>open the annotation list, telling it which board it is being
    /// read from - "keep on this board" needs to know which one.</summary>
    void OpenNotes()
    {
        if (_notes is null) return;
        _notes.OnBoard = _scene.ActiveBoard is { } b && !_scene.BoardReadOnly ? (b.Id, b.Name) : null;
        _notes.Open();
    }

    /// <summary>notes are written on boards now. from the map, the useful
    /// move is to put the code on a board first.</summary>
    void Annotate()
    {
        if (_scene.ActiveBoard is not null) return;
        Toast("notes live on boards: add this to a board, then right click the line there");
    }

    public void FlyToAnnotation(Annotation a)
    {
        _scene.EnsureAnchored(a.File);
        int i = _scene.ResolveFile(a.File, a.Key);
        if (i < 0) { _caption = $"{a.File} is not in this scan"; InvalidateVisual(); return; }

        int line = a.Line;
        AnchorKind kind = AnchorKind.Line;
        foreach (var (candidate, anchor) in _scene.AnchorsFor(a.File))
            if (ReferenceEquals(candidate, a)) { line = anchor.Line; kind = anchor.Kind; }

        var f = _scene.Data.Files[i];
        var t = Places.Resolve(_scene, new Place
        {
            Name = a.Text, File = a.File, Key = a.Key,
            Line = Math.Max(0, line - 12),
            EndLine = Math.Min(f.N - 1, line + 12),
        }, (float)Bounds.Width, (float)Bounds.Height);
        FlyTo(t.X, t.Y, t.S);
        _scene.Highlight = (i, line, line);
        _caption = kind is AnchorKind.Symbol or AnchorKind.Line ? a.Text : $"{a.Text}   [{kind}]";
    }

    public void AttachBoards(BoardStore store, BoardOverlay panel)
    {
        _boardStore = store;
        _boards = panel;
        panel.Open += OpenBoard;
        // Home is the map: leaving whatever board is open, generated or not
        panel.HomeRequested += () => { if (_scene.ActiveBoard is not null) LeaveBoard(); Focus(); };
        panel.CreateRequested += CreateBoard;
        panel.DeleteRequested += picked => _prompt?.Ask(
            picked.Count == 1
                ? $"delete the board \"{picked[0].Name}\"? type y to confirm"
                : $"delete {picked.Count} boards? type y to confirm",
            "",
            answer =>
            {
                Focus();
                if (!answer.Trim().StartsWith('y')) { Toast("kept"); return; }
                foreach (var b in picked) store.Delete(b);
                panel.Rebuild();
                PruneImages();
                Saved($"deleted {picked.Count} board{(picked.Count == 1 ? "" : "s")}");
            });
        panel.GroupRequested += b => _prompt?.Ask("group name, blank for none", b.Group, name =>
        {
            b.Group = name == "-" ? "" : name;
            store.Save(b);
            panel.Rebuild();
            Focus();
            Saved("group");
        });
        panel.RenameRequested += b => _prompt?.Ask("rename board", b.Name, name =>
        {
            store.Rename(b, name);
            panel.Rebuild();
            Focus();
            InvalidateVisual();
        });
    }

    FileSystemWatcher? _boardWatch;
    int _reloadAsk;

    /// <summary>reload boards when their files change on disk, so a board
    /// built by `atlas board` - or brought in by a pull - shows up without
    /// restarting. The watcher's events arrive on a pool thread and in
    /// bursts (one save is several), so each one asks for a reload a moment
    /// later and only the last ask of a burst is acted on.</summary>
    public void WatchBoards()
    {
        if (_boardStore is null) return;
        try
        {
            // a repo with no boards yet has no folder to watch, and the
            // first board may well come from outside
            Directory.CreateDirectory(_boardStore.Dir);
            _boardWatch = new FileSystemWatcher(Path.GetDirectoryName(_boardStore.Dir)!)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            FileSystemEventHandler poke = (_, _) => ScheduleReload();
            _boardWatch.Changed += poke;
            _boardWatch.Created += poke;
            _boardWatch.Deleted += poke;
            _boardWatch.Renamed += (s, e) => poke(s, e);
            _boardWatch.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"not watching boards: {ex.Message}");
        }
    }

    void ScheduleReload()
    {
        int ask = Interlocked.Increment(ref _reloadAsk);
        Task.Delay(250).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (ask == _reloadAsk) ReloadBoards();
        }));
    }

    /// <summary>take in whatever changed on disk. Not in the middle of a
    /// drag: the board would change under the hand holding it, so it waits
    /// for the gesture to end. What changed on disk wins over what is open -
    /// the app saves as it goes, so all it can lose is the gesture that was
    /// in flight, and undo is cleared because its snapshots are of a board
    /// that is no longer there.</summary>
    public void ReloadBoards()
    {
        if (_boardStore is null) return;
        if (_drag) { ScheduleReload(); return; }

        var changed = _boardStore.Refresh();
        if (changed is null) return;
        _boards?.Rebuild();

        var open = _scene.ActiveBoard;
        if (open is not null && !_scene.BoardReadOnly && changed.Contains(open))
        {
            if (!_boardStore.Boards.Contains(open))
            {
                LeaveBoard();
                Toast($"\"{open.Name}\" was deleted on disk");
                return;
            }
            // a tour walks a list of stops that has just been replaced
            if (_tour is not null) EndTour();
            if (Reveal.Showing(_stops)) _stops!.Rebuild(0);
            _boardDirty = false;
            _history.Clear();
            var ids = open.Items.Select(i => i.Id).ToHashSet();
            _scene.Picked.RemoveWhere(id => !ids.Contains(id));
            _caption = open.Name;
            RefreshBoardBar();
            Toast("board changed on disk - reloaded");
        }
        InvalidateVisual();
    }

    /// <summary>put a board back on its code, and save it only if that moved
    /// something. Whether anything moved is judged by where things are, not
    /// by whether the anchoring filled anything in: a board without
    /// fingerprints or anchors - made by hand, or by an older Atlas - gets
    /// them now, and saving for that alone rewrote the whole file of a board
    /// someone had only opened. What was filled in stays in memory and goes
    /// out with the next edit that saves.</summary>
    bool FollowCode(Board b)
    {
        static string Where(Board b) => string.Join(";", b.Items.Select(i =>
            $"{i.X},{i.Y},{i.W},{i.H},{i.X2},{i.Y2},{i.Line},{i.EndLine}"));
        var before = Where(b);
        _scene.EnsureKeys(b);
        _scene.AnchorBoard(b);
        if (Where(b) == before) return false;
        _boardStore?.Save(b);
        return true;
    }

    /// <summary>open or close the workspace panel. Tab, from anywhere - the
    /// window hands it over before any control can take it as "next field".</summary>
    public void ToggleWorkspace()
    {
        if (_boards is null) return;
        _boards.Toggle();
        if (!Reveal.Showing(_boards)) Focus();
        InvalidateVisual();
    }

    void CreateBoard()
    {
        if (_boardStore is null || _prompt is null) return;
        _prompt.Ask("name the new board", "board", name =>
        {
            var b = _boardStore.Create(name);
            _boards?.Rebuild();
            OpenBoard(b);
            Focus();
        });
    }

    void OpenBoard(Board b)
    {
        _flight = null;
        _glide.Stop();
        _boards?.Close();
        _stops?.Close();
        if (_tour is not null) EndTour();
        if (_scene.ActiveBoard is null) _mapCam = (_scene.CamX, _scene.CamY, _scene.CamS);
        _scene.ActiveBoard = b;
        // a board made before windows kept fingerprints gets them now, and
        // every window is put back on the code it was opened on - lines may
        // have been inserted above it since, which would otherwise leave it
        // showing different code with the drawings still over the old spot
        if (FollowCode(b)) Toast("windows followed their code");
        _scene.Grid = Editing ? GridStep : 0;
        _scene.Picked.Clear();
        _history.Clear();
        _lastBoard = b;
        RefreshBoardBar();
        RefreshHints();
        FitBoard();
        _caption = b.Name;
        Focus();
        InvalidateVisual();
    }

    /// <summary>gather whatever is being reviewed onto one board. The map says
    /// where a change landed; this says what it was.</summary>
    void ToggleChangeBoard()
    {
        if (_scene.BoardReadOnly) { LeaveBoard(); return; }
        if (_scene.Review is not { } set) { Toast("nothing under review"); return; }
        if (_scene.ActiveBoard is not null) return;

        var label = _commitAt < 0 ? _target?.Label ?? "changes" : _prCommits[_commitAt].Subject;
        var board = ChangeBoard.Build(set, _scene, label, _showRemoved);
        if (board.Items.Count == 0)
        {
            Toast("none of these changes are on the map");
            return;
        }

        _flight = null;
        _glide.Stop();
        _boards?.Close();
        _mapCam = (_scene.CamX, _scene.CamY, _scene.CamS);
        _scene.ActiveBoard = board;
        _scene.BoardReadOnly = true;
        _scene.Grid = 0;
        _scene.Picked.Clear();
        _history.Clear();
        // deliberately not _lastBoard: `A` must not try to add to a board that
        // is thrown away the moment you leave
        RefreshBoardBar();
        RefreshHints();
        ShowFirstChange(board, set);
        _caption = $"{label}  [changed code]";
        Focus();
        InvalidateVisual();
    }

    /// <summary>frame the first change rather than the whole board.
    ///
    /// The windows are whole files now, so fitting the board puts three
    /// thousand lines on screen at a size nobody can read, and landing at
    /// the top of the first one puts you at line 1 with the change you came
    /// for somewhere off the bottom.</summary>
    void ShowFirstChange(Board board, ChangeSet set)
    {
        if (ChangeBoard.FirstChange(board, set, _scene) is not { } spot)
        {
            FitBoard();
            return;
        }
        _scene.CamX = spot.X;
        _scene.CamY = spot.Y + (float)Bounds.Height / 6;
        _scene.CamS = 1f;
    }

    /// <summary>whether the change view shows the lines a change removed, in
    /// red between the pieces of each file. On by default: it is the half of
    /// a diff the map cannot show.</summary>
    bool _showRemoved = true;

    void ToggleRemoved()
    {
        if (_git is null || _target is null) return;
        // one read at a time: a toggle racing the open it follows could land
        // the tree with the other setting from the one the key now says
        if (_reading) { Toast("still reading - a moment"); return; }
        _showRemoved = !_showRemoved;
        Toast(_showRemoved ? "putting the removed lines back..." : "taking the removed lines out...");

        // the text itself changes, so the tree is read again - off the UI
        // thread, as opening it was. A pick of another target meanwhile wins
        int turn = ++_opening;
        _reading = true;
        var git = _git;
        var target = _target;
        int at = _commitAt;
        var commit = at >= 0 ? _prCommits[at] : null;
        var cached = _whole;
        var opts = _scanOptions;
        bool removed = _showRemoved;
        var root = _scene.Data.Root;
        Task.Run(() =>
        {
            var whole = cached ?? git.Whole(target);
            return (Tree: BuildTree(git, target, whole, opts, removed, root), Set: commit is null ? whole : git.OfCommit(commit));
        }).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (turn != _opening) return;
            _reading = false;
            if (t.IsFaulted) { Toast($"could not read {target.Label} again: {t.Exception?.GetBaseException().Message}"); return; }

            // with the camera left where it is: this is the same change,
            // seen another way
            float x = _scene.CamX, y = _scene.CamY, s = _scene.CamS;
            if (t.Result.Tree is { } tree) _scene.ShowSnapshot(tree.Data, path => tree.Text.GetValueOrDefault(path), tree.Splices);
            // stepped to another commit while this was reading: show that one
            if (at == _commitAt) ShowChanges(t.Result.Set);
            else ShowCommit(_commitAt);
            if (_scene.BoardReadOnly) RebuildChangeBoard(frame: false);
            else (_scene.CamX, _scene.CamY, _scene.CamS) = (x, y, s);
            Toast(_showRemoved ? "removed lines shown" : "removed lines hidden");
            RefreshHints();
            InvalidateVisual();
        }));
    }

    /// <summary>a review target's tree is being read in the background.</summary>
    bool _reading;

    /// <summary>after stepping to another commit, gather that one instead.</summary>
    void RebuildChangeBoard(bool frame = true)
    {
        if (!_scene.BoardReadOnly || _scene.Review is not { } set) return;

        var label = _commitAt < 0 ? _target?.Label ?? "changes" : _prCommits[_commitAt].Subject;
        var board = ChangeBoard.Build(set, _scene, label, _showRemoved);

        // an empty board is shown rather than refused. Leaving the previous
        // commit's windows up because this one touched nothing on the map is
        // how the view came to be showing one commit while the panel said
        // another, which is worse than showing nothing
        _scene.ActiveBoard = board;
        _scene.Picked.Clear();
        if (board.Items.Count == 0)
        {
            _caption = $"{label}  -  nothing from this commit is on the map";
            InvalidateVisual();
            return;
        }

        if (frame) ShowFirstChange(board, set);
        _caption = $"{label}  [changed code]";
        InvalidateVisual();
    }

    void LeaveBoard()
    {
        DismissPrompt();
        if (_scene.ActiveBoard is null) return;
        _stops?.Close();
        if (_tour is not null) EndTour();

        // a generated board is not saved and owns no images
        if (_scene.BoardReadOnly)
        {
            _scene.BoardReadOnly = false;
            _scene.ActiveBoard = null;
            _scene.Picked.Clear();
            RefreshBoardBar();
            RefreshHints();
            if (_mapCam is { } back) { _scene.CamX = back.X; _scene.CamY = back.Y; _scene.CamS = back.S; }
            _mapCam = null;
            _caption = "";
            InvalidateVisual();
            return;
        }

        SaveBoardIfDirty();
        PruneImages();
        _scene.ActiveBoard = null;
        // the map has a grid too, so hand it back rather than clearing it:
        // leaving a board while editing used to land on a map with no grid
        // until E was pressed twice
        _scene.Grid = Editing ? GridStep : 0;
        _scene.Picked.Clear();
        RefreshBoardBar();
        RefreshHints();
        if (_mapCam is { } c) { _scene.CamX = c.X; _scene.CamY = c.Y; _scene.CamS = c.S; }
        _mapCam = null;
        _caption = "";
        _boards?.Rebuild();
        InvalidateVisual();
    }

    void SaveBoardIfDirty()
    {
        if (!_boardDirty || _scene.ActiveBoard is null || _boardStore is null) return;
        _boardStore.Save(_scene.ActiveBoard);
        _boardDirty = false;
    }

    /// <summary>put whatever the map is showing onto a board as a file window.</summary>
    void AddViewToBoard()
    {
        if (_boardStore is null || _scene.ActiveBoard is not null) return;
        if (_boardStore.Boards.Count == 0) { CreateBoard(); return; }

        int i = _scene.FileAt(_scene.CamX, _scene.CamY);
        if (i < 0 || _scene.Tier < 2) { _caption = "zoom onto a file first"; InvalidateVisual(); return; }

        var mark = Places.Capture(_scene, (float)Bounds.Width, (float)Bounds.Height);
        // whichever board was open last, so adding several files in a row works
        var board = _lastBoard is not null && _boardStore.Boards.Contains(_lastBoard)
            ? _lastBoard
            : _boardStore.Boards[0];
        var item = new BoardItem
        {
            Id = BoardStore.NewId(),
            Kind = "file",
            File = mark.File,
            Key = mark.File is null ? null : _scene.KeyFor(mark.File),
            Line = mark.Line,
            EndLine = mark.EndLine,
            W = 620,
        };
        PlaceBelowExisting(board, item);
        board.Items.Add(item);
        _boardStore.Save(board);
        _boards?.Rebuild();
        _caption = $"added {Path.GetFileName(item.File)}:{item.Line + 1} to  {board.Name}";
        InvalidateVisual();
    }

    void PlaceBelowExisting(Board board, BoardItem item)
    {
        if (board.Items.Count == 0) { item.X = 0; item.Y = 0; return; }
        item.X = board.Items.Min(i => i.X);
        item.Y = board.Items.Max(i => i.Y + _scene.LastHeight(i)) + 40;
    }

    /// <summary>a picture on the board, from a file on disk.</summary>
    async void AddImageFromDisk()
    {
        if (_scene.ActiveBoard is null || TopLevel.GetTopLevel(this) is not { } top) return;
        var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "add an image to the board",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        var file = picked.FirstOrDefault()?.TryGetLocalPath();
        if (file is null) return;
        PlaceImage(ImageStore.Import(_scene.Data.Root, file));
    }

    /// <summary>whatever was copied: a screenshot, or a file in explorer.</summary>
    async void PasteImage()
    {
        if (_scene.ActiveBoard is null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clip) return;

        // windows first: avalonia hands back nothing for a screenshot, which
        // is the whole point of pasting an image
        using (var pasted = ClipboardImage.Read())
            if (pasted is not null) { PlaceImage(ImageStore.Save(_scene.Data.Root, pasted)); return; }

        if (await clip.GetDataAsync(DataFormats.Files) is IEnumerable<Avalonia.Platform.Storage.IStorageItem> files)
        {
            var path = files.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p is not null);
            if (path is not null) { PlaceImage(ImageStore.Import(_scene.Data.Root, path)); return; }
        }

        Toast("nothing on the clipboard that looks like an image");
    }

    /// <summary>drop image files nothing points at any more. only safe where
    /// the undo history that could bring an item back is already gone.</summary>
    void PruneImages()
    {
        if (_boardStore is null) return;
        int gone = ImageStore.Prune(_scene.Data.Root, _boardStore.Boards);
        if (gone > 0) Console.WriteLine($"pruned {gone} unused image file(s)");
    }

    void PlaceImage(string? name)
    {
        if (_scene.ActiveBoard is not { } board) return;
        if (name is null) { Toast("could not read that image"); return; }

        var img = ImageStore.Load(_scene.Data.Root, name);
        float w = 520;
        float h = img is null ? 320 : w * img.Height / Math.Max(1, img.Width);
        Remember();
        board.Items.Add(new BoardItem
        {
            Id = BoardStore.NewId(), Kind = "image", File = name,
            W = w, H = h, X = _scene.CamX - w / 2, Y = _scene.CamY - h / 2,
        });
        _boardStore?.Save(board);
        Focus();
        Saved($"image on  {board.Name}");
    }

    /// <summary>a plain rectangle to group or point at things on a board.</summary>
    void AddShape() => AddShape("shape");

    /// <summary>the shape the next drag will draw, or null.</summary>
    string? _armShape;

    /// <summary>arm a shape tool. It used to drop a 520x240 box in the middle
    /// of the view, which is Atlas choosing the size and the place - the two
    /// things about a shape that are actually yours. Like the arrow, arming
    /// does nothing until you drag out the box you want.</summary>
    void AddShape(string kind)
    {
        if (_scene.ActiveBoard is null || _scene.BoardReadOnly) return;
        if (!Editing) SetEditing(true);

        _armBrush = _armEraser = _armArrow = false;
        _armShape = _armShape == kind ? null : kind;   // pressing it again backs out
        _scene.Picked.Clear();
        RefreshBoardBar();
        ApplyCursor();
        Toast(_armShape is null ? "cancelled" : $"drag out the {Named(kind)}");
    }

    /// <summary>place what was dragged out. A drag too small to be deliberate
    /// is a click, and a click is not a shape.</summary>
    void PlaceShape(SKRect box, string kind)
    {
        if (_scene.ActiveBoard is not { } board) return;
        if (box.Width < 6 || (kind != "text" && box.Height < 4)) return;

        if (kind == "text") { PlaceLabel(box); return; }

        Remember();
        var item = new BoardItem
        {
            Id = BoardStore.NewId(), Kind = kind, Text = "", Color = PenColor,
            X = box.Left, Y = box.Top, W = box.Width, H = box.Height,
        };
        board.Items.Add(item);
        _scene.PinOver(item);
        _scene.Picked.Clear();
        _scene.Picked.Add(item.Id);
        _boardStore?.Save(board);
        Saved($"{Named(kind)} on  {board.Name}");
    }

    /// <summary>a label takes its width from the drag - that is what its words
    /// wrap to - and its height from the words themselves.</summary>
    void PlaceLabel(SKRect box)
    {
        if (_scene.ActiveBoard is not { } board) return;

        Remember();
        var item = new BoardItem
        {
            Id = BoardStore.NewId(), Kind = "text", Text = "",
            X = box.Left, Y = box.Top, W = box.Width, Color = PenColor,
        };
        board.Items.Add(item);
        _scene.PinOver(item);
        _scene.Picked.Clear();
        _scene.Picked.Add(item.Id);
        _boardStore?.Save(board);

        // straight into typing, where the label is. Asking for the words in
        // a dialog at the top of the window means drawing a label somewhere
        // and then looking away from it to say what it is
        BeginEdit(item);
    }

    void AddLabel() => AddShape("text");

    static string Named(string kind) => kind switch
    {
        "shape" => "rectangle",
        "ellipse" => "ellipse",
        "diamond" => "diamond",
        // called "text" everywhere the user can see: the kind has always
        // been "text", and only the button said "label", which is why plain
        // words with no panel behind them were hard to find
        "text" => "text",
        _ => kind,
    };

    public static readonly (string Name, string Hex)[] Colours =
    [
        ("amber", "#ffd166"), ("cyan", "#5fd3f3"), ("green", "#3fb96a"),
        ("red", "#d95c5c"), ("violet", "#b48ae8"), ("slate", "#8aa0b0"),
    ];

    /// <summary>the fill choices: nothing, the border's own colour, or one of
    /// its own. "No fill" comes first because it is the one people reach for
    /// - a frame round a group of windows has to be see-through.</summary>
    List<MenuItem> Fills(List<BoardItem> picked)
    {
        var items = new List<MenuItem>
        {
            ContextActions.Item("No fill", () => SetFill(picked, BoardItem.NoFill)),
            ContextActions.Item("Match the border", () => SetFill(picked, BoardItem.BorderFill)),
            ContextActions.Separator(),
        };
        items.AddRange(Colours.Select(c => ContextActions.Item(c.Name, () => SetFill(picked, c.Hex))));
        items.Add(ContextActions.Separator());
        items.Add(ContextActions.Item("Hex...", () => AskHex(
            "fill colour: rrggbb, or aarrggbb for opacity",
            picked.Count == 1 ? picked[0].Fill : null,
            hex => SetFill(picked, hex))));
        return items;
    }

    void SetFill(List<BoardItem> picked, string? fill)
    {
        Remember();
        foreach (var it in picked) it.Fill = fill;
        if (_scene.ActiveBoard is { } b) _boardStore?.Save(b);
        Saved(fill == BoardItem.NoFill ? "no fill" : "fill");
        InvalidateVisual();
    }

    /// <summary>the same ladder [ and ] step through, so the menu and the
    /// keyboard cannot offer two different sets of widths.</summary>
    List<MenuItem> LineWidths(List<BoardItem> picked)
    {
        var items = new List<MenuItem>
        {
            ContextActions.Item("Default", () => SetLineWidth(picked, 0)),
            ContextActions.Separator(),
        };
        items.AddRange(Weights.Select(w =>
            ContextActions.Item($"{w:0.#}", () => SetLineWidth(picked, w))));
        return items;
    }

    /// <summary>type sizes, as a ladder for the same reason the pen widths
    /// are one: the useful sizes are few and far apart.</summary>
    static readonly float[] TextLadder = [11f, 14f, 18f, 24f, 34f, 48f, 72f];

    List<MenuItem> TextSizes(List<BoardItem> picked)
    {
        var items = new List<MenuItem>
        {
            ContextActions.Item("Default", () => SetTextSize(picked, 0)),
            ContextActions.Separator(),
        };
        items.AddRange(TextLadder.Select(sz =>
            ContextActions.Item($"{sz:0.#}", () => SetTextSize(picked, sz))));
        return items;
    }

    void SetTextSize(List<BoardItem> picked, float size)
    {
        if (picked.Count == 0) return;
        Remember();
        foreach (var it in picked) it.Size = size;
        if (_scene.ActiveBoard is { } b) _boardStore?.Save(b);
        Saved(size > 0 ? $"text {size:0.#}" : "default text size");
        InvalidateVisual();
    }

    /// <summary>the border's colour, and how solid it is.
    ///
    /// A shape's outline is drawn at part opacity unless its colour says
    /// otherwise, which is what makes one look like a frame round code
    /// rather than a box in front of it - good for a frame, wrong when you
    /// want a line. Solid and soft rewrite the stored colour's alpha, so
    /// there is one field rather than a colour plus a flag that can
    /// disagree with it.</summary>
    List<MenuItem> Palette(List<BoardItem> picked)
    {
        var items = Colours.Select(c => ContextActions.Item(c.Name, () =>
            SetColour(picked, Keeping(picked, c.Hex)))).ToList();

        items.Add(ContextActions.Separator());
        items.Add(ContextActions.Item("Solid", () => SetOpacity(picked, 255)));
        items.Add(ContextActions.Item("Soft", () => SetOpacity(picked, 150)));
        items.Add(ContextActions.Item("Faint", () => SetOpacity(picked, 60)));
        items.Add(ContextActions.Item("Hex...", () => AskHex(
            "border colour: rrggbb, or aarrggbb for opacity",
            picked.Count == 1 ? picked[0].Color : null,
            hex => SetColour(picked, hex))));
        return items;
    }

    /// <summary>a new colour keeps the opacity the old one asked for, so
    /// picking a different swatch does not quietly make a solid border
    /// see-through again.</summary>
    string Keeping(List<BoardItem> picked, string hex)
    {
        var had = picked.FirstOrDefault(i => Scene.HasOpacity(i.Color))?.Color;
        if (had is null) return hex;
        var alpha = Convert.ToByte(had.TrimStart('#')[..2], 16);
        return Scene.WithOpacity(hex, SkiaSharp.SKColors.White, alpha);
    }

    void SetColour(List<BoardItem> picked, string? hex)
    {
        if (hex is null) return;
        Remember();
        foreach (var it in picked) it.Color = hex;
        if (_scene.ActiveBoard is { } b) _boardStore?.Save(b);
        Saved("colour");
        InvalidateVisual();
    }

    void SetOpacity(List<BoardItem> picked, byte alpha)
    {
        Remember();
        foreach (var it in picked)
            it.Color = Scene.WithOpacity(it.Color, Scene.DefaultShape, alpha);
        if (_scene.ActiveBoard is { } b) _boardStore?.Save(b);
        Saved(alpha == 255 ? "solid" : "see-through");
        InvalidateVisual();
    }

    /// <summary>ask for a colour by hand. Anything that is not one is
    /// refused rather than silently ignored - a typo that turns a border
    /// invisible is worse than being told.</summary>
    void AskHex(string label, string? current, Action<string> use)
    {
        if (_prompt is null) return;
        _prompt.Ask(label, current ?? "", typed =>
        {
            if (Scene.NormaliseHex(typed) is { } hex) use(hex);
            else Toast($"\"{typed}\" is not a colour");
            Focus();
        });
    }

    readonly List<BoardItem> _clipboard = [];

    /// <summary>where the pointer last was, which is where ctrl+V pastes.</summary>
    Point _pointer;

    List<BoardItem> PickedItems() =>
        _scene.ActiveBoard?.Items.Where(i => _scene.Picked.Contains(i.Id)).ToList() ?? [];
    readonly History _history = new();

    /// <summary>whether a gesture has left anything to undo. For the tests:
    /// the rule is about what does not get recorded, which is otherwise only
    /// visible by counting ctrl+Z presses.</summary>
    public bool CanUndo => _history.CanUndo;

    /// <summary>snapshot the board before changing it.</summary>
    void Remember()
    {
        if (_scene.ActiveBoard is { } b) _history.Record(b);
    }

    bool _recorded;

    /// <summary>about to change something in this gesture: take a snapshot,
    /// once.
    ///
    /// A press used to record one, which meant picking a thing up and putting
    /// it down unchanged - or just clicking it to select it - left an undo
    /// step that undid nothing. Three clicks and ctrl+Z three times to get
    /// back to the last real change. A gesture that changes nothing now
    /// records nothing.</summary>
    void Mutating()
    {
        if (_recorded) return;
        _recorded = true;
        Remember();
    }

    /// <summary>a new gesture: nothing recorded for it yet.</summary>
    void StartGesture() => _recorded = false;

    void Undo()
    {
        if (_scene.ActiveBoard is not { } b) return;
        if (!_history.Undo(b)) { Toast("nothing to undo"); return; }
        _scene.Picked.Clear();
        _boardStore?.Save(b);
        _boards?.Rebuild();
        Saved("undo");
    }

    void Redo()
    {
        if (_scene.ActiveBoard is not { } b) return;
        if (!_history.Redo(b)) { Toast("nothing to redo"); return; }
        _scene.Picked.Clear();
        _boardStore?.Save(b);
        _boards?.Rebuild();
        Saved("redo");
    }

    /// <summary>copy by round trip through json, so every field comes along.
    /// the copy used to list fields by hand and dropped whatever was added
    /// after it was written: a stroke's ink, an arrow's ties, fill, weight,
    /// size, a window's anchor.</summary>
    void CopyPicked(List<BoardItem> picked)
    {
        if (picked.Count == 0) return;
        _clipboard.Clear();
        var ids = picked.Select(i => i.Id).ToHashSet();
        foreach (var it in picked)
        {
            var copy = System.Text.Json.JsonSerializer.Deserialize<BoardItem>(
                System.Text.Json.JsonSerializer.Serialize(it))!;
            // a tie to something left behind becomes a loose end where the
            // line is drawn now, or the copy would point back at the original
            if (copy.Kind == "arrow")
            {
                var (a, b) = _scene.ArrowEnds(it);
                if (copy.From is not null && !ids.Contains(copy.From)) { copy.From = null; copy.X = a.X; copy.Y = a.Y; }
                if (copy.To is not null && !ids.Contains(copy.To)) { copy.To = null; copy.X2 = b.X; copy.Y2 = b.Y; }
            }
            _clipboard.Add(copy);
        }
        // replaces whatever is on the system clipboard, so a screenshot taken
        // before this copy does not win the next ctrl+V
        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync($"{picked.Count} atlas item(s)");
        Toast($"copied {picked.Count}");
    }

    /// <summary>a copied file window is a reference, not a copy of the code.
    /// the pasted set keeps its shape, with its top left corner at the point.</summary>
    public void Paste(Point at)
    {
        if (_scene.ActiveBoard is not { } board || _scene.BoardReadOnly || _clipboard.Count == 0) return;
        Remember();
        var (wx, wy) = WorldAt(at);
        var boxes = _clipboard.Select(_scene.BoundsOf).ToList();
        float dx = wx - boxes.Min(r => r.Left), dy = wy - boxes.Min(r => r.Top);

        var fresh = _clipboard.ToDictionary(i => i.Id, _ => BoardStore.NewId());
        var pasted = new List<BoardItem>();
        foreach (var it in _clipboard)
        {
            var copy = System.Text.Json.JsonSerializer.Deserialize<BoardItem>(
                System.Text.Json.JsonSerializer.Serialize(it))!;
            copy.Id = fresh[it.Id];
            if (copy.From is not null) copy.From = fresh[copy.From];
            if (copy.To is not null) copy.To = fresh[copy.To];
            Scene.Move(copy, dx, dy);
            pasted.Add(copy);
        }

        // added before pinning, so a drawing pasted along with its window
        // pins to the new window rather than the one it was copied off
        board.Items.AddRange(pasted);
        _scene.Picked.Clear();
        foreach (var copy in pasted)
        {
            _scene.PinOver(copy);
            _scene.Picked.Add(copy.Id);
        }
        _boardStore?.Save(board);
        InvalidateVisual();
        Saved($"pasted {pasted.Count}");
    }

    /// <summary>ctrl+V: an image on the system clipboard, else what was
    /// copied here.</summary>
    void PasteAny()
    {
        if (_scene.ActiveBoard is null || _scene.BoardReadOnly) return;
        if (_clipboard.Count > 0 && !ClipboardImage.Has()) { Paste(_pointer); return; }
        PasteImage();
    }

    void EditRange(BoardItem window)
    {
        if (_prompt is null) return;
        _prompt.Ask("first and last line, e.g. 40-88", $"{window.Line + 1}-{window.EndLine + 1}", text =>
        {
            Remember();
            var parts = text.Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var a) && int.TryParse(parts[1], out var b))
            {
                window.Line = Math.Max(0, a - 1);
                window.EndLine = Math.Max(window.Line, b - 1);
                // typed on purpose, so this is where the window belongs now
                _scene.Reanchor(window);
                if (_scene.ActiveBoard is { } board) _boardStore?.Save(board);
                Saved("line range");
            }
            else Toast("expected something like 40-88");
            Focus();
        });
    }

    void BringToFront()
    {
        if (_scene.ActiveBoard is not { } board || _scene.Picked.Count == 0) return;
        Remember();
        var moved = board.Items.Where(i => _scene.Picked.Contains(i.Id)).ToList();
        board.Items.RemoveAll(moved.Contains);
        board.Items.AddRange(moved);
        _boardStore?.Save(board);
        Saved("brought to front");
    }

    void SendToBack()
    {
        if (_scene.ActiveBoard is not { } board || _scene.Picked.Count == 0) return;
        Remember();
        var moved = board.Items.Where(i => _scene.Picked.Contains(i.Id)).ToList();
        board.Items.RemoveAll(moved.Contains);
        board.Items.InsertRange(0, moved);
        _boardStore?.Save(board);
        Saved("sent to back");
    }

    void DeletePicked()
    {
        if (_scene.ActiveBoard is not { } board || _scene.Picked.Count == 0) return;
        Remember();
        int n = _scene.Remove(board, board.Items.Where(i => _scene.Picked.Contains(i.Id)).ToList());
        _scene.Picked.Clear();
        _boardStore?.Save(board);
        _boards?.Rebuild();
        Saved("removed " + n + (n == 1 ? " item" : " items"));
    }

    void AddNote()
    {
        if (_scene.ActiveBoard is not { } board) return;

        Remember();
        var item = new BoardItem
        {
            Id = BoardStore.NewId(), Kind = "note", Text = "",
            W = 380,
            // where the user is looking, not off beside everything else
            X = _scene.CamX - 190,
            Y = _scene.CamY - 40,
        };
        board.Items.Add(item);
        _scene.PinOver(item);
        _scene.Picked.Clear();
        _scene.Picked.Add(item.Id);
        _boardStore?.Save(board);
        _boards?.Rebuild();
        BeginEdit(item);
    }

    public void AttachTour(TourPanel panel)
    {
        _stops = panel;
        panel.Preview += i => GoToStop(i, playing: false);
        panel.Play += PlayTour;
        panel.Capture += CaptureStop;
        panel.RenameRequested += RenameStop;
        panel.Selecting += InvalidateVisual;
        panel.Changed += () =>
        {
            if (_scene.ActiveBoard is not { } b) return;
            _boardStore?.Save(b);
            Saved($"tour on  {b.Name}");
        };
    }

    void ToggleTourPanel()
    {
        if (_stops is null || _scene.ActiveBoard is not { } b || _scene.BoardReadOnly) return;
        if (Reveal.Showing(_stops)) _stops.Close();
        else _stops.Show(b);
        Focus();
        // the stop frames come and go with the panel
        InvalidateVisual();
    }

    /// <summary>how much of the right of the canvas the tour panel covers.
    /// A stop is the region you could *see*, and with the panel open that is
    /// less than the window.</summary>
    float Covered() => Reveal.Showing(_stops) ? (float)_stops!.Width : 0;

    /// <summary>what is on screen becomes the next stop.</summary>
    void CaptureStop()
    {
        if (_scene.ActiveBoard is not { } b || _scene.BoardReadOnly) return;
        // where the camera is going rather than where it has got to, so a
        // capture straight after a scroll takes the view being scrolled to
        var (x, y, s) = Destination();
        float c = Covered();
        b.Stops.Add(Stop.Of(x - c / 2 / s, y, s, (float)Bounds.Width - c, (float)Bounds.Height));
        _boardStore?.Save(b);
        if (Reveal.Showing(_stops)) _stops!.Rebuild(b.Stops.Count - 1);
        Saved($"stop {b.Stops.Count} on  {b.Name}");
    }

    void RenameStop(int i)
    {
        if (_prompt is null || _scene.ActiveBoard is not { } b || i < 0 || i >= b.Stops.Count) return;
        _prompt.Ask("name this stop", b.Stops[i].Name ?? "", name =>
        {
            b.Stops[i].Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            _boardStore?.Save(b);
            _stops?.Rebuild(i);
            Focus();
        });
    }

    /// <summary>frame a stop. The region is fitted to the part of the window
    /// that is not under the panel, whatever size the window is now.</summary>
    void GoToStop(int i, bool playing)
    {
        if (_scene.ActiveBoard is not { } b || i < 0 || i >= b.Stops.Count) return;
        var stop = b.Stops[i];
        float c = Covered();
        float s = stop.ScaleFor((float)Bounds.Width - c, (float)Bounds.Height);
        FlyTo(stop.X + c / 2 / s, stop.Y, s);
        if (!playing) return;
        _stops?.Select(i);
        _caption = $"{TourPanel.Label(stop, i)}   {i + 1}/{b.Stops.Count}   -   space, arrows: step   esc: stop";
    }

    /// <summary>play this board's tour from a stop.</summary>
    public void PlayTour(int from)
    {
        if (_scene.ActiveBoard is not { } b || _scene.BoardReadOnly) return;
        if (b.Stops.Count == 0) { Toast("no stops yet: frame a view and press M"); return; }
        _tour = b;
        _stop = Math.Clamp(from, 0, b.Stops.Count - 1);
        GoToStop(_stop, playing: true);
    }

    /// <summary>the next stop, or the previous one. Both ends clamp rather
    /// than leaving: walking off the end would mean the left arrow could not
    /// bring you back. Escape is how you leave.</summary>
    void Step(int by)
    {
        if (_tour is null || _tour != _scene.ActiveBoard || _tour.Stops.Count == 0) return;
        int next = Math.Clamp(_stop + Math.Sign(by), 0, _tour.Stops.Count - 1);
        if (next == _stop) return;
        _stop = next;
        GoToStop(_stop, playing: true);
    }

    void EndTour()
    {
        _tour = null;
        _stop = -1;
        _caption = "";
        InvalidateVisual();
    }

    /// <summary>the search list means "go there" on the map and "put it here"
    /// on a board, so it is the same picker in both places.</summary>
    public void OnFileChosen(int i)
    {
        if (_scene.ActiveBoard is not null) AddFileWindow(i);
        else FlyToFile(i);
    }

    void AddFileWindow(int i)
    {
        if (_scene.ActiveBoard is not { } board) return;
        var f = _scene.Data.Files[i];
        Remember();
        var window = new BoardItem
        {
            Id = BoardStore.NewId(), Kind = "file", File = f.P, Key = _scene.KeyFor(f.P),
            Line = 0, EndLine = -1, W = 620,
            X = _scene.CamX - 310, Y = _scene.CamY - 120,
        };
        // where it points, recorded now, so inserting above it later moves
        // the range rather than the code under it
        _scene.Reanchor(window);
        board.Items.Add(window);
        _boardStore?.Save(board);
        _boards?.Rebuild();
        Focus();
        Saved(Path.GetFileName(f.P) + " added");
    }

    public void FlyToFile(int i)
    {
        var f = _scene.Data.Files[i];
        float s = (float)Bounds.Width * 0.7f / f.W;
        float visibleH = (float)Bounds.Height / s;
        FlyTo(f.X + f.W / 2, f.Y + Math.Min(f.H, visibleH) / 2, s);
    }

    (float X, float Y) WorldAt(Point p) => (
        _scene.CamX + ((float)p.X - (float)Bounds.Width / 2) / _scene.CamS,
        _scene.CamY + ((float)p.Y - (float)Bounds.Height / 2) / _scene.CamS);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _flight = null;
        _glide.Stop();

        // the OS already swaps buttons for left handed mice, so "right" here
        // simply means whichever button the user treats as secondary. it pans,
        // and opens the menu only when the press did not turn into a drag
        _secondary = e.GetCurrentPoint(this).Properties.IsRightButtonPressed;

        if (_scene.ActiveBoard is null && Editing && !_secondary)
        {
            // the map is selection only: sweep files, nothing else
            var (mx, my) = WorldAt(e.GetPosition(this));
            bool add = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                       || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (!add) _scene.PickedFiles.Clear();
            _bandBaseFiles.Clear();
            _bandBaseFiles.AddRange(_scene.PickedFiles);

            _band = true;
            _bandStart = e.GetPosition(this);
            _scene.Rubberband = new SkiaSharp.SKRect(mx, my, mx, my);
            _scene.RubberbandFade = 1f;
            _bandFadeAt = -1;
            _drag = true;
            _last = _bandStart;
            _clickCount = e.ClickCount;
            InvalidateVisual();
            return;
        }

        if (_scene.ActiveBoard is not null && Editing && !_secondary)
        {
            var (wx, wy) = WorldAt(e.GetPosition(this));

            if (_armShape is not null)
            {
                _scene.ShapeDraft = (_armShape, new SkiaSharp.SKRect(wx, wy, wx, wy));
                _shapeFrom = new SkiaSharp.SKPoint(wx, wy);
                _drag = true;
                _last = e.GetPosition(this);
                _pressAt = _last;
                _grabbed = false;
                return;
            }

            if (_armBrush)
            {
                _scene.StrokeDraft = new BoardItem
                {
                    Id = BoardStore.NewId(), Kind = "stroke",
                    Color = PenColor, Weight = PenWeight,
                };
                Strokes.Add(_scene.StrokeDraft, wx, wy);
                _drag = true;
                _last = e.GetPosition(this);
                _pressAt = _last;
                _grabbed = false;
                return;
            }

            if (_armEraser)
            {
                _erasing = false;
                EraseAt(wx, wy);
                _drag = true;
                _last = e.GetPosition(this);
                _pressAt = _last;
                _grabbed = false;
                return;
            }

            if (_armArrow)
            {
                _scene.ArrowDraft = (new SkiaSharp.SKPoint(wx, wy), new SkiaSharp.SKPoint(wx, wy));
                _drag = true;
                _last = e.GetPosition(this);
                _pressAt = _last;
                _grabbed = false;
                return;
            }

            if (_scene.ArrowEndAt(wx, wy) is { } end)
            {
                _arrowEnd = end.Arrow;
                _arrowEndWhich = end.End;
                _scene.ShowAnchors = true;
                _drag = true;
                _last = e.GetPosition(this);
                _pressAt = _last;
                _grabbed = false;
                return;
            }

            if (_scene.GripAt(wx, wy) is { } grip)
            {
                _resizing = grip.Item;
                _resizeCorner = grip.Corner;
                _resizeEdge = -1;
                _drag = true;
                _last = e.GetPosition(this);
                _pressAt = _last;
                _grabbed = false;
                return;
            }

            // after the corners, so a corner wins where the two overlap: it
            // is the smaller target and the more precise thing to have aimed
            // at. On a file window this is the clip handle
            if (_scene.EdgeAt(wx, wy) is { } wall)
            {
                // how long the file is now, once, before the dragging starts
                _scene.NoteLength(wall.Item);
                _resizing = wall.Item;
                _resizeEdge = wall.Edge;
                _drag = true;
                _last = e.GetPosition(this);
                _pressAt = _last;
                _grabbed = false;
                return;
            }

            var hit = _scene.PickAt(wx, wy);
            if (hit is null)
            {
                // empty canvas: sweep out a selection
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
                    !e.KeyModifiers.HasFlag(KeyModifiers.Control)) _scene.Picked.Clear();
                _bandBase.Clear();
                _bandBase.AddRange(_scene.Picked);
                _band = true;
                _bandStart = e.GetPosition(this);
                _scene.Rubberband = new SkiaSharp.SKRect(wx, wy, wx, wy);
                _scene.RubberbandFade = 1f;
                _bandFadeAt = -1;
                _drag = true;
                _last = _bandStart;
                InvalidateVisual();
                return;
            }

            // ctrl as well as shift. The map accepted both and a board only
            // shift, so ctrl+click on a board threw the selection away and
            // started a new one - for every kind of item, not just the ones
            // it was noticed on
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ||
                e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (!_scene.Picked.Add(hit.Id)) _scene.Picked.Remove(hit.Id);
            }
            else if (!_scene.Picked.Contains(hit.Id))
            {
                _scene.Picked.Clear();
                _scene.Picked.Add(hit.Id);
            }
            _dragItem = hit;
        }

        _clickCount = e.ClickCount;
        _drag = true;
        StartGesture();
        ApplyCursor();
        _panVx = _panVy = 0;
        _panAt = -1;
        _dragDist = 0;
        _grabbed = false;
        _axis = 0;
        _dragOrigin = e.GetPosition(this);
        _pressAt = e.GetPosition(this);
        _last = e.GetPosition(this);
        e.Pointer.Capture(this);
        Focus();
        InvalidateVisual();     // the selection has to show before anything moves
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_secondary)
        {
            // a secondary click that never moved means "show me the actions"
            bool moved = _dragDist > 5;
            _secondary = false;
            _drag = false;
            ApplyCursor();
            e.Pointer.Capture(null);
            if (!moved) { ShowContextMenu(e.GetPosition(this)); return; }

            // a secondary drag pans exactly as a primary one does, so letting
            // go of it throws the canvas the same way. This used to return
            // before the throw, which made the momentum feel like a property
            // of the left button rather than of the gesture
            ThrowPan();
            return;
        }

        _drag = false;
        _axis = 0;
        ApplyCursor();

        // a pan let go with speed on it keeps going
        ThrowPan();

        _erasing = false;
        _scene.ShowAnchors = false;

        if (_scene.ShapeDraft is { } placed)
        {
            _scene.ShapeDraft = null;
            // one shape per arming, like the arrow: a tool still live after it
            // is done catches the next drag you meant for something else
            _armShape = null;
            RefreshBoardBar();
            ApplyCursor();
            PlaceShape(placed.Box, placed.Kind);

            _drag = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
            return;
        }

        if (_scene.StrokeDraft is { } drawn)
        {
            _scene.StrokeDraft = null;
            // a click that never moved is not a stroke, it is a click
            if (_scene.ActiveBoard is { } sb && Strokes.CountOf(drawn) > 1)
            {
                Remember();
                Strokes.Reframe(drawn);
                sb.Items.Add(drawn);
                // ink drawn across a file window is about that code
                _scene.PinOver(drawn);
                _boardDirty = true;
                // a finished stroke is finished. This used to return before
                // the save, so a drawing sat in memory until something else
                // happened to write the board out
                SaveBoardIfDirty();
            }
            e.Pointer.Capture(null);
            InvalidateVisual();
            return;
        }

        if (_scene.ArrowDraft is { } made)
        {
            _scene.ArrowDraft = null;

            // one arrow per arming. Staying armed is right for the brush,
            // where you draw stroke after stroke; an arrow is a deliberate
            // single thing, and a tool still live after it is done is a tool
            // that catches the next drag you meant for something else
            _armArrow = false;
            RefreshBoardBar();
            ApplyCursor();

            if (_scene.ActiveBoard is { } b &&
                (Math.Abs(made.B.X - made.A.X) > 4 || Math.Abs(made.B.Y - made.A.Y) > 4))
            {
                Remember();

                // only a node connects. Landing inside a box is not aiming at
                // it, and a line that crosses a box is often just a line
                var from = _scene.AnchorAt(made.A.X, made.A.Y);
                var to = _scene.AnchorAt(made.B.X, made.B.Y);
                if (from is not null && to is not null &&
                    ReferenceEquals(from.Value.Item, to.Value.Item)) to = null;   // not to itself

                var arrow = new BoardItem
                {
                    Id = BoardStore.NewId(), Kind = "arrow",
                    X = made.A.X, Y = made.A.Y, X2 = made.B.X, Y2 = made.B.Y,
                    From = from?.Item.Id, To = to?.Item.Id,
                    FromSide = from?.Side ?? -1,
                    ToSide = to?.Side ?? -1,
                };
                b.Items.Add(arrow);
                // an arrow with both ends tied follows those items already;
                // one drawn loose across a window is about the code it
                // crosses, so it gets pinned like anything else
                if (from is null || to is null) _scene.PinOver(arrow);
                _boardStore?.Save(b);
                Saved(from is null && to is null ? "arrow"
                    : from is not null && to is not null ? "connector"
                    : "arrow, one end tied");
            }
            _drag = false;
            e.Pointer.Capture(null);
            return;
        }
        // an arrow whose end was dragged: its start is what it is pinned by,
        // so a loose one is pinned again where it now starts, and one tied at
        // both ends goes where its ties take it and needs no pin at all
        if (_arrowEnd is { } bent && _grabbed)
        {
            if (bent.From is null || bent.To is null) _scene.PinOver(bent);
            else bent.Host = null;
            _boardDirty = true;
        }
        _arrowEnd = null;

        if (_band) { _band = false; FadeRubberband(); }

        // what a drawing is about is whatever it was let go of on top of,
        // so this is where it gains a window - or loses one, by being
        // dragged off onto bare canvas
        if (_dragItem is not null && _grabbed) RepinPicked();
        // and a resize changes what a drawing covers as much as a move does.
        // It was left out, so the anchors stayed as they were when the box
        // was drawn, and reopening the board put the old size back. A file
        // window records its own crop as it goes, and pins nothing of its own
        if (_resizing is { Kind: not "file" } resized && _grabbed)
        {
            _scene.PinOver(resized);
            _boardDirty = true;
        }

        _dragItem = null;
        _resizing = null;
        _resizeEdge = -1;
        _unsnapped.Clear();
        e.Pointer.Capture(null);
        SaveBoardIfDirty();
        if (_dragDist > 5) return;

        var p = e.GetPosition(this);
        var (wx, wy) = WorldAt(p);

        if (_scene.ActiveBoard is not null)
        {
            if (!Editing)
            {
                if (_scene.Selection is not null) { _scene.Selection = null; InvalidateVisual(); }
                return;
            }
            // a click on code inside a window picks a line there
            if (_scene.LineInWindowAt(wx, wy) is { } spot)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
                    _scene.Selection is { } held && held.File == spot.File)
                    Select(spot.File, held.From, spot.Line);
                else
                    Select(spot.File, spot.Line, spot.Line);
                return;
            }
            if (_scene.Selection is not null) { _scene.Selection = null; InvalidateVisual(); }
            return;
        }

        // at reading zoom a click picks a line whether or not you are editing:
        // flying to a file you are already inside of is not useful
        if (_scene.Tier >= 3)
        {
            // the second click of a double click is handled by OnDoubleTapped,
            // which runs after this and would otherwise be overwritten
            if (_clickCount > 1) return;

            // reading zoom: a click picks a line rather than flying somewhere
            var hit = _scene.LineAt(wx, wy);
            if (hit is null) { _scene.Selection = null; InvalidateVisual(); return; }

            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                && _scene.Selection is { } cur && cur.File == hit.Value.File)
            {
                Select(hit.Value.File, cur.From, hit.Value.Line);
                return;
            }

            // a click inside the picked range keeps it: collapsing to one line
            // is never what you wanted when you are reaching for the menu
            if (_scene.Selection is { } held && held.File == hit.Value.File &&
                hit.Value.Line >= held.From && hit.Value.Line <= held.To)
                return;

            Select(hit.Value.File, hit.Value.Line, hit.Value.Line);
            return;
        }

        if (!Editing && (_scene.PickedFiles.Count > 0 || _scene.Selection is not null))
        {
            _scene.PickedFiles.Clear();
            _scene.Selection = null;
            InvalidateVisual();
        }

        int file = _scene.FileAt(wx, wy);
        if (file >= 0) FlyToFile(file);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        _pointer = p;
        if (_scene.Spotlight is not null)
        {
            _scene.Spotlight = new SkiaSharp.SKPoint((float)p.X, (float)p.Y);
            InvalidateVisual();
        }
        if (_scene.ActiveBoard is null) UpdateHoverLine(p);
        else if (!_drag) HoverHandles(p);

        if (!_drag) return;
        if ((_dragItem ?? _resizing ?? _arrowEnd) is not null && !_grabbed)
        {
            // _last stays at the press, so the first real move carries the
            // whole distance and nothing lags behind the pointer
            if (Math.Abs(p.X - _pressAt.X) + Math.Abs(p.Y - _pressAt.Y) < GrabSlop) return;
            _grabbed = true;
        }
        _dragDist += Math.Abs(p.X - _last.X) + Math.Abs(p.Y - _last.Y);

        if (_scene.ShapeDraft is { } shaping)
        {
            var (sx2, sy2) = WorldAt(p);
            // normalised, so dragging up and left works as well as down right
            _scene.ShapeDraft = (shaping.Kind, new SkiaSharp.SKRect(
                Math.Min(_shapeFrom.X, sx2), Math.Min(_shapeFrom.Y, sy2),
                Math.Max(_shapeFrom.X, sx2), Math.Max(_shapeFrom.Y, sy2)));
            _last = p;
            InvalidateVisual();
            return;
        }

        if (_scene.StrokeDraft is { } pen)
        {
            var (sx, sy) = WorldAt(p);
            // the sample spacing is in board units, so a stroke drawn zoomed
            // out is not stored coarser than one drawn zoomed in
            if (Strokes.Add(pen, sx, sy, Strokes.MinStep / Math.Max(_scene.CamS, 0.05f)))
                InvalidateVisual();
            _last = p;
            return;
        }

        if (_armEraser)
        {
            var (ex0, ey0) = WorldAt(p);
            EraseAt(ex0, ey0);
            _last = p;
            return;
        }

        if (_scene.ArrowDraft is { } draft)
        {
            var (ax, ay) = WorldAt(p);
            _scene.ArrowDraft = (draft.A, new SkiaSharp.SKPoint(ax, ay));
            _last = p;
            InvalidateVisual();
            return;
        }

        if (_arrowEnd is not null)
        {
            var (ex, ey) = WorldAt(p);

            // dragging an end onto a node ties it there, and anywhere else
            // lets it go - the same rule that made it in the first place
            var node = _scene.AnchorAt(ex, ey);

            Mutating();
            if (_arrowEndWhich == 1)
            {
                _arrowEnd.X = ex; _arrowEnd.Y = ey;
                if (node?.Item.Id == _arrowEnd.To) node = null;   // not to itself
                _arrowEnd.From = node?.Item.Id;
                _arrowEnd.FromSide = node?.Side ?? -1;
            }
            else
            {
                _arrowEnd.X2 = ex; _arrowEnd.Y2 = ey;
                if (node?.Item.Id == _arrowEnd.From) node = null;
                _arrowEnd.To = node?.Item.Id;
                _arrowEnd.ToSide = node?.Side ?? -1;
            }
            _boardDirty = true;
            _last = p;
            InvalidateVisual();
            return;
        }

        if (_resizing is not null)
        {
            Mutating();
            var (rx, ry) = WorldAt(p);
            // the edge being dragged snaps as a move does, so boxes can be
            // lined up by their sides and not only by where they start. Not a
            // file window: its walls crop whole lines, and a grid would fight
            if (SnapToGrid && _resizing.Kind != "file")
            {
                float cell = _scene.SnapStep(GridStep);
                (rx, ry) = (MathF.Round(rx / cell) * cell, MathF.Round(ry / cell) * cell);
            }
            float ratio = _scene.LastHeight(_resizing) / Math.Max(1, _resizing.W);

            if (_resizeEdge >= 0)
            {
                // one wall follows the pointer and the other three stay put
                _scene.ResizeEdge(_resizing, _resizeEdge, rx, ry);
                // clipping moves a window on purpose, so its anchor moves
                // with it - otherwise the next open would drag it back
                if (_resizing.Kind == "file") _scene.Reanchor(_resizing);
            }
            else
            {
                // the corner follows the pointer and the opposite one stays put
                _scene.Resize(_resizing, _resizeCorner, rx, ry);
                // an image keeps its shape, which only makes sense when both
                // dimensions moved - dragging one wall is asking for the other
                // not to
                if (_resizing.Kind == "image") _resizing.H = _resizing.W * ratio;
            }
            _boardDirty = true;
        }
        else if (_band)
        {
            var (bx, by) = WorldAt(p);
            var (sx, sy) = WorldAt(_bandStart);
            _scene.Rubberband = new SkiaSharp.SKRect(
                Math.Min(sx, bx), Math.Min(sy, by), Math.Max(sx, bx), Math.Max(sy, by));
            // recompute from scratch each move, so shrinking the band lets go
            // of what it has passed back over
            if (_scene.ActiveBoard is null)
            {
                _scene.PickedFiles.Clear();
                foreach (var i in _bandBaseFiles) _scene.PickedFiles.Add(i);
                foreach (var i in _scene.FilesIn(_scene.Rubberband.Value)) _scene.PickedFiles.Add(i);
            }
            else
            {
                _scene.Picked.Clear();
                foreach (var id in _bandBase) _scene.Picked.Add(id);
                foreach (var it in _scene.ItemsIn(_scene.Rubberband.Value)) _scene.Picked.Add(it.Id);
            }
        }
        else if (_dragItem is not null)
        {
            Mutating();
            // everything picked moves together
            float mx = (float)(p.X - _last.X) / _scene.CamS;
            float my = (float)(p.Y - _last.Y) / _scene.CamS;
            foreach (var it in _scene.ActiveBoard!.Items)
            {
                if (!_scene.Picked.Contains(it.Id)) continue;

                // track the true position and snap only what is shown. rounding
                // the live position swallows every move smaller than a cell,
                // which reads as the item being stuck
                var raw = _unsnapped.TryGetValue(it.Id, out var u) ? u : (it.X, it.Y);
                raw = (raw.X + mx, raw.Y + my);
                _unsnapped[it.Id] = raw;

                float cell = _scene.SnapStep(GridStep);
                float nx = SnapToGrid ? MathF.Round(raw.X / cell) * cell : raw.X;
                float ny = SnapToGrid ? MathF.Round(raw.Y / cell) * cell : raw.Y;
                float dx = nx - it.X, dy = ny - it.Y;
                if (Strokes.Is(it)) { Strokes.Move(it, dx, dy); continue; }
                it.X = nx;
                it.Y = ny;
                if (it.Kind == "arrow") { it.X2 += dx; it.Y2 += dy; }
            }
            _boardDirty = true;
        }
        else
        {
            float panX = -(float)(p.X - _last.X) / _scene.CamS;
            float panY = -(float)(p.Y - _last.Y) / _scene.CamS;
            _scene.CamX += panX;
            _scene.CamY += panY;
            _scene.ClampCamera((float)Bounds.Width, (float)Bounds.Height);
            TrackPan(panX, panY);
        }
        _last = p;
        InvalidateVisual();
    }

    /// <summary>where the camera is heading: the glide's target while one is
    /// running, else where it already is. Aiming from here rather than from
    /// the live camera is what lets a flurry of wheel notches add up instead
    /// of each one restarting from a camera that has not caught up.</summary>
    /// <summary>where the camera will come to rest: the end of a flight if
    /// one is under way, otherwise where the glide is heading.</summary>
    public (float X, float Y, float S) Destination()
    {
        if (_flight is null) return Aim();
        // false at the end, since the flight is then over - but the numbers
        // it hands back are the end
        _flight.Sample(_flight.DurationMs, out var x, out var y, out var s);
        return (x, y, s);
    }

    public (float X, float Y, float S) Aim() =>
        _glide.Running ? (_glide.X, _glide.Y, _glide.S) : (_scene.CamX, _scene.CamY, _scene.CamS);

    void GlideTo(float x, float y, float s)
    {
        float camX = _scene.CamX, camY = _scene.CamY, camS = _scene.CamS;
        // the clamp works on the camera, so aim it at the target, read the
        // result back and put the camera where it was
        _scene.CamX = x; _scene.CamY = y; _scene.CamS = s;
        _scene.ClampCamera((float)Bounds.Width, (float)Bounds.Height);
        _glide.ToAtOnce(_scene.CamX, _scene.CamY, _scene.CamS);
        _scene.CamX = camX; _scene.CamY = camY; _scene.CamS = camS;
        InvalidateVisual();
    }

    void GlideBy(float dx, float dy)
    {
        var a = Aim();
        GlideTo(a.X + dx, a.Y + dy, a.S);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        // the flight goes, but not the glide: this notch aims from where the
        // last one was already heading, which is what makes a fast wheel add
        // up instead of stalling
        _flight = null;
        var p = e.GetPosition(this);

        // alt+wheel sizes the spotlight while it is on
        if (_spotOn && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            _scene.SpotlightRadius = Math.Clamp(_scene.SpotlightRadius * (e.Delta.Y > 0 ? 1.15f : 1 / 1.15f), 40, 600);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        // a tilt wheel or a trackpad swipe reads sideways in either mode
        if (Math.Abs(e.Delta.X) > 0.01)
        {
            GlideBy(-(float)e.Delta.X * (float)Bounds.Width * 0.12f / Aim().S, 0);
            return;
        }

        // in scroll mode the wheel reads down the file; shift reads across it
        // in either mode; ctrl still zooms
        bool sideways = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if ((!WheelZoom || sideways) && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var aim = Aim();
            float across = (float)Bounds.Width * 0.12f / aim.S;
            float down = (float)Bounds.Height * 0.12f / aim.S;
            if (sideways) GlideBy(-(float)e.Delta.Y * across, 0);
            else GlideBy(0, -(float)e.Delta.Y * down);
            return;
        }

        float vw = (float)Bounds.Width, vh = (float)Bounds.Height;
        var from = Aim();

        // keep the world point under the cursor pinned while zooming. The pin
        // is on where the wheel is taking us, not on where the camera has got
        // to, so a second notch mid-glide zooms toward the same place
        float wx = from.X + ((float)p.X - vw / 2) / from.S;
        float wy = from.Y + ((float)p.Y - vh / 2) / from.S;
        float s = Math.Clamp(from.S * MathF.Exp((float)e.Delta.Y * 0.18f),
            _scene.MinZoomFor(vw, vh), 40f);

        GlideTo(wx - ((float)p.X - vw / 2) / s, wy - ((float)p.Y - vh / 2) / s, s);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // space turns the spotlight on or off, wherever you are - a tour
        // included, whose stops still step with the arrows. It used to be
        // hold-to-pan while editing a board; tab took the spotlight's old key
        // for the workspace panel
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None)
        {
            ToggleSpotlight();
            e.Handled = true;
            return;
        }

        // ctrl+F is find *in* files. Finding a file by name is "/", which is
        // a different question and was what this used to do
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            OpenGrep();
            e.Handled = true;
            return;
        }

        // F3 steps the matches wherever you are, panel open or not - it is
        // the one search key that has to work while you are reading the
        // code rather than reading the list
        if (e.Key == Key.F3)
        {
            StepMatch(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            e.Handled = true;
            return;
        }
        HandleKey(e.Key, e.KeyModifiers);
    }

    bool _ctrl, _alt;

    public void HandleKey(Key key) => HandleKey(key, KeyModifiers.None);

    /// <summary>one place that turns modifiers into the flags the handlers
    /// read, so a test can send alt+left without reaching into fields.</summary>
    public void HandleKey(Key key, KeyModifiers mods)
    {
        _ctrl = mods.HasFlag(KeyModifiers.Control);
        _alt = mods.HasFlag(KeyModifiers.Alt);
        HandleKey(key, mods.HasFlag(KeyModifiers.Shift));
    }

    public void HandleKey(Key key, bool e_shift)
    {
        // Escape belongs to the window, which has already peeled a layer off
        // by the time the key gets here. Nothing below may claim it
        if (key == Key.Escape) return;

        // panels have no focus of their own; the canvas drives them
        if (Reveal.Showing(_grep) && _grep!.HandleKey(key)) { InvalidateVisual(); return; }
        if (Reveal.Showing(_stops) && _stops!.HandleKey(key)) { InvalidateVisual(); return; }
        if (Reveal.Showing(_boards) && _boards!.HandleKey(key)) { InvalidateVisual(); return; }
        if (Reveal.Showing(_notes) && _notes!.HandleKey(key)) { InvalidateVisual(); return; }
        if (Reveal.Showing(_reviews) && _reviews!.HandleKey(key)) { InvalidateVisual(); return; }

        if (_scene.Review is not null)
        {
            switch (key)
            {
                case Key.OemCloseBrackets: StepCommit(1); return;
                case Key.OemOpenBrackets: StepCommit(-1); return;
                // the arrows walk the commit list the same way, which is
                // what a list of commits down the side looks like it does.
                // Not while a tour is running: those are its arrows
                case Key.Down when _tour is null: StepCommit(1); return;
                case Key.Up when _tour is null: StepCommit(-1); return;
                case Key.H: ToggleCommits(); return;
                case Key.R when !_scene.BoardReadOnly: ToggleRemoved(); return;
            }
        }

        if (_scene.BoardReadOnly)
        {
            // a generated board reads and navigates; it does not author.
            // It is still a *view* though, and everything about how you look
            // at it was unreachable here - S in particular, so the wheel
            // could not be switched between zoom and scroll once you were in
            switch (key)
            {
                // copying off a generated board onto one of your own is fine
                case Key.C when _ctrl: CopyPicked(PickedItems()); return;
                case Key.C: LeaveBoard(); return;
                case Key.R: ToggleRemoved(); return;
                case Key.F: FitBoard(); InvalidateVisual(); return;
                case Key.S: SetWheelZoom(!WheelZoom); InvalidateVisual(); return;
                case Key.H: ToggleCommits(); return;
                case Key.OemQuestion: OpenSearch?.Invoke(); return;
                default: return;
            }
        }

        if (_scene.ActiveBoard is not null)
        {
            switch (key)
            {
                case Key.Back or Key.Delete: DeletePicked(); return;
                case Key.Left when _alt: LeaveBoard(); return;
                case Key.N: AddNote(); return;
                case Key.T or Key.D1: AddShape("shape"); return;
                case Key.D2: AddShape("ellipse"); return;
                case Key.D3: AddShape("diamond"); return;
                case Key.D4: AddLabel(); return;
                case Key.G:
                    SnapToGrid = !SnapToGrid;
                    RefreshBoardBar();
                    Toast(SnapToGrid ? "snap on" : "snap off");
                    return;
                case Key.C when _ctrl: CopyPicked(PickedItems()); return;
                case Key.V when _ctrl: PasteAny(); return;
                case Key.Z when _ctrl: Undo(); return;
                case Key.Y when _ctrl: Redo(); return;
                case Key.Y: AddArrow(); return;
                case Key.B: ArmBrush(); return;
                case Key.X: ArmEraser(); return;
                // one pair of keys, whichever tool is armed
                // the bar says "file  A", and it opens the picker - which on
                // a board adds a window. The key fell through to the map's
                // A, which refuses outright while a board is open
                case Key.A: OpenSearch?.Invoke(); return;
                case Key.OemCloseBrackets: StepTool(1); return;
                case Key.OemOpenBrackets: StepTool(-1); return;
                case Key.F: FitBoard(); InvalidateVisual(); return;
                case Key.M when e_shift: ToggleTourPanel(); return;
                case Key.M: CaptureStop(); return;
                case Key.P: PlayTour(Reveal.Showing(_stops) ? Math.Max(0, _stops!.Selected) : 0); return;
            }
        }

        switch (key)
        {
            case Key.F:
            {
                float w = _scene.Data.World.W, h = _scene.Data.World.H;
                float s = Math.Min((float)Bounds.Width / w, (float)Bounds.Height / h) * 0.92f;
                FlyTo(w / 2, h / 2, s);
                break;
            }
            case Key.G: OpenReviewPanel(branches: true); break;
            case Key.C: ToggleChangeBoard(); break;
            case Key.D: _scene.ShowFolders = !_scene.ShowFolders; break;
            case Key.OemPeriod: ToggleHidden(); break;
            case Key.A: AddViewToBoard(); break;
            case Key.I: Annotate(); break;
            case Key.E: SetEditing(!Editing); break;
            case Key.S: SetWheelZoom(!WheelZoom); break;
            case Key.L: OpenNotes(); break;
            case Key.P: OpenReviewPanel(branches: false); break;
            case Key.Right when _tour is not null: Step(1); break;
            case Key.Left when _tour is not null: Step(-1); break;
            case Key.OemQuestion:
                OpenSearch?.Invoke();
                break;
            case Key.F9 when _phase < 0: _phase = 0; _phaseFrame = 0; _benchText = ""; break;
        }
        InvalidateVisual();
    }
}

sealed class SceneOp(Rect bounds, Scene scene, float w, float h) : ICustomDrawOperation
{
    public Rect Bounds => bounds;
    public void Dispose() { }
    public bool Equals(ICustomDrawOperation? other) => false;
    public bool HitTest(Point p) => false;

    public void Render(ImmediateDrawingContext context)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature is null) return;
        using var lease = feature.Lease();
        scene.Draw(lease.SkCanvas, w, h);
    }
}

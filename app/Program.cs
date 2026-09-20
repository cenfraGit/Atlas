using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
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
        if (args.Contains("--tokentest")) { TokenTest.Run(args); return; }
        if (args.Contains("--flighttest")) { FlightTest.Run(); return; }
        if (args.Contains("--searchtest")) { SearchTest.Run(args); return; }
        if (args.Contains("--bookmarktest")) { BookmarkTest.Run(args); return; }
        if (args.Contains("--boardtest")) { BoardTest.Run(args); return; }
        if (args.Contains("--annotationtest")) { AnnotationTest.Run(); return; }
        if (args.Contains("--gittest")) { GitTest.Run(args); return; }
        if (args.Contains("--samples")) { Samples.Run(args); return; }
        if (args.Contains("--pickingtest")) { PickingTest.Run(args); return; }
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

            var store = BookmarkStore.Load(scene.Data.Root);
            var prompt = new PromptOverlay();
            var marks = new BookmarkOverlay(store, scene);
            marks.FlyTo += view.FlyToBookmark;
            marks.Play += view.PlayTour;
            view.Attach(store, prompt, marks);

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

            var hints = new HintBar();
            view.AttachHints(hints);

            var boardBar = new BoardBar();
            view.AttachBoardBar(boardBar);

            var islands = new ModeIslands();
            islands.EditChanged += view.SetEditing;
            islands.ZoomChanged += view.SetWheelZoom;
            view.MouseModeChanged += () => islands.Reflect(view.Editing, view.WheelZoom);
            islands.Reflect(view.Editing, view.WheelZoom);

            var root = new Grid();
            root.Children.Add(view);
            root.Children.Add(search);
            root.Children.Add(marks);
            root.Children.Add(boards);
            root.Children.Add(notes);
            root.Children.Add(boardBar);
            root.Children.Add(hints);
            root.Children.Add(islands);
            root.Children.Add(commits);
            root.Children.Add(reviews);
            root.Children.Add(prompt);
            desktop.MainWindow = new Window
            {
                Title = "Atlas",
                Width = 1400,
                Height = 900,
                Background = Brushes.Black,
                Content = root,
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    static Scan LoadScan(string[] args)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "data")) &&
               !Directory.EnumerateFiles(dir.FullName, "Atlas.sln*").Any())
            dir = dir.Parent;
        var cache = Path.Combine(dir?.FullName ?? ".", "data", "scan.json");

        // first non-flag argument is a repo to scan; otherwise reuse the last scan
        var repo = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--") && Directory.Exists(a));
        if (repo is not null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var fresh = Scanner.Build(repo);
            Scanner.Save(fresh, cache);
            Console.WriteLine($"scanned {fresh.Files.Count} files, {fresh.Districts.Count} districts " +
                              $"in {sw.ElapsedMilliseconds}ms -> {cache}");
            return fresh;
        }
        // the cache records the folder it scanned, which is the one thing in it
        // that belongs to this machine. with no cache, or one written on
        // another machine, fall back to Atlas itself rather than failing
        if (File.Exists(cache))
        {
            using var fs = File.OpenRead(cache);
            var cached = JsonSerializer.Deserialize<Scan>(fs)!;
            if (Directory.Exists(cached.Root)) return cached;
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

    Flight? _flight;
    double _flightT0;
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

        if (_flight is not null)
        {
            if (!_flight.Sample(_clock.Elapsed.TotalMilliseconds - _flightT0,
                                out var fx, out var fy, out var fs))
                _flight = null;
            _scene.CamX = fx; _scene.CamY = fy; _scene.CamS = fs;
        }

        if (_autoBench && _phase < 0 && !_benchDone && ++_warmFrames > 30) { _phase = 0; _phaseFrame = 0; }
        if (_phase >= 0) StepBench(w);

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
        if (_phase >= 0 || _flight is not null || (_autoBench && !_benchDone))
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);
    }

    void DrawHud(DrawingContext ctx)
    {
        var sorted = _ring.Where(v => v > 0).OrderBy(v => v).ToArray();
        var med = sorted.Length > 0 ? sorted[sorted.Length / 2] : 0;
        var tierName = new[] { "districts", "cards", "bars", "text" }[_scene.Tier];
        var line1 = $"{med:F2} ms draw  |  zoom {_scene.CamS:F3}x  |  tier {tierName}";
        var line2 = $"{_scene.Data.Files.Count} files  {_scene.Data.Districts.Count} districts  " +
                    $"{_scene.VisibleCards} visible  {_scene.ChunksBuilt} built" +
                    (_scene.BuiltThisFrame > 0 ? $"  +{_scene.BuiltThisFrame}" : "");
        Text(ctx, line1, 12, 10, Color.FromRgb(0xff, 0xd1, 0x66));
        Text(ctx, line2, 12, 26, Color.FromRgb(0x35, 0x70, 0x8f));
        if (_benchText.Length > 0) Text(ctx, _benchText, 12, 46, Color.FromRgb(0x5f, 0xd3, 0xf3));
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
            var (bx, by) = WorldAt(e.GetPosition(this));
            if (_scene.ItemAt(bx, by) is { Kind: "note" } note) EditNote(note);
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
            ContextActions.Item("Bookmark this", () => BookmarkSelection(target, what)),
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
    void DismissPrompt() => _prompt?.Close();

    void OpenMenu(List<MenuItem> items)
    {
        DismissPrompt();
        _menu?.Close();
        _menu = new ContextMenu { ItemsSource = items, Placement = PlacementMode.Pointer };
        _menu.Open(this);
    }

    void ShowBoardMenu(Point p)
    {
        var (wx, wy) = WorldAt(p);
        var item = _scene.ItemAt(wx, wy);
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
                    () => AnnotateSelection(_scene.Selection is { } sel && sel.File == spot.File
                        ? sel
                        : (spot.File, spot.Line, spot.Line))),
            };
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
                board.Items.Remove(spot.Item);
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

        if (picked.Count == 1 && picked[0].Kind == "note")
            items.Add(ContextActions.Item("Edit text...", () => EditNote(picked[0])));
        if (picked.Count == 1 && picked[0].Kind == "file")
            items.Add(ContextActions.Item("Change line range...", () => EditRange(picked[0])));
        if (picked.Count == 2)
            items.Add(ContextActions.Item("Arrow between these", AddArrow));

        // only what applies to everything picked
        if (picked.Count > 0 && picked.All(i => i.Kind is "note" or "shape" or "arrow"))
            items.Add(ContextActions.Submenu("Colour", Palette(picked)));
        if (picked.Count > 0)
        {
            items.Add(ContextActions.Item("Bring to front", BringToFront));
            items.Add(ContextActions.Item("Send to back", SendToBack));
            items.Add(ContextActions.Item("Copy", () => CopyPicked(picked)));
            items.Add(ContextActions.Item(picked.Count > 1 ? "Remove these" : "Remove", DeletePicked));
        }

        items.Add(ContextActions.Item("Add board note...", AddNote));
        items.Add(ContextActions.Item("Add rectangle", AddShape));
        items.Add(ContextActions.Item("Add image...", AddImageFromDisk));
        items.Add(ContextActions.Item("Paste image (ctrl+V)", PasteImage));
        if (_clipboard.Count > 0)
            items.Add(ContextActions.Item($"Paste {_clipboard.Count}", () => Paste(p)));
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

    void AnnotateSelection((int File, int From, int To) target)
    {
        if (_noteStore is null || _prompt is null || ReadOnlyHere()) return;
        var f = _scene.Data.Files[target.File];
        var lines = LinesFor(target.File);
        if (lines is null) return;
        int line = Math.Clamp(target.From < 0 ? 0 : target.From, 0, lines.Length - 1);

        _prompt.Ask($"annotate {f.P[(f.P.LastIndexOf('/') + 1)..]}:{line + 1}", "", text =>
        {
            var full = Path.Combine(_scene.Data.Root, f.P.Replace('/', Path.DirectorySeparatorChar));
            int to = target.To < 0 ? line : Math.Clamp(target.To, line, lines.Length - 1);
            var a = Anchors.Create(f.P, full, lines, line, to, text);
            _noteStore.Annotations.Add(a);
            _noteStore.Save();
            _scene.Reanchor(f.P, full, lines);
            Saved($"annotation on {a.Symbol ?? f.P}");
            Focus();
        });
    }

    void BookmarkSelection((int File, int From, int To) target, string what)
    {
        if (_store is null || _prompt is null) return;
        var f = _scene.Data.Files[target.File];
        _prompt.Ask("name this bookmark", what, name =>
        {
            var b = new Bookmark
            {
                Id = BookmarkStore.NewId(), Name = name, File = f.P,
                Line = target.From, EndLine = target.To,
                X = _scene.CamX, Y = _scene.CamY, S = _scene.CamS,
            };
            _store.Bookmarks.Add(b);
            _recording?.Add(b.Id);
            _store.Save();
            Saved($"bookmark  {name}");
            Focus();
        });
    }

    /// <summary>the selection becomes the window's line range, so a board shows
    /// exactly the method you picked rather than the whole file.</summary>
    BoardItem WindowFor((int File, int From, int To) target)
    {
        var f = _scene.Data.Files[target.File];
        return new BoardItem
        {
            Id = BookmarkStore.NewId(), Kind = "file", File = f.P,
            Line = target.From < 0 ? 0 : target.From,
            EndLine = target.To, W = 620,
        };
    }

    void AddFilesToBoard(Board board, List<int> files)
    {
        if (_boardStore is null) return;
        float y = board.Items.Count == 0 ? 0 : board.Items.Max(i => i.Y + _scene.ItemHeight(i)) + 40;
        foreach (var i in files)
        {
            var f = _scene.Data.Files[i];
            board.Items.Add(new BoardItem
            {
                Id = BookmarkStore.NewId(), Kind = "file", File = f.P,
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

    void Toast(string message)
    {
        _caption = message;
        InvalidateVisual();
    }

    /// <summary>ease the band out instead of snapping it away.</summary>
    /// <summary>snap the picked items as a group, keeping their spacing.</summary>
    void SnapPicked()
    {
        if (_scene.ActiveBoard is not { } board) return;
        var picked = board.Items.Where(i => _scene.Picked.Contains(i.Id)).ToList();
        if (picked.Count == 0) return;

        float ax = picked[0].X, ay = picked[0].Y;
        float dx = MathF.Round(ax / GridStep) * GridStep - ax;
        float dy = MathF.Round(ay / GridStep) * GridStep - ay;
        if (dx == 0 && dy == 0) return;

        foreach (var it in picked)
        {
            it.X += dx;
            it.Y += dy;
            if (it.Kind == "arrow") { it.X2 += dx; it.Y2 += dy; }
        }
    }

    void FadeRubberband()
    {
        _bandFade = 1f;
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            _bandFade -= 0.12f;
            _scene.RubberbandFade = Math.Max(0, _bandFade);
            if (_bandFade <= 0) { _scene.Rubberband = null; timer.Stop(); }
            InvalidateVisual();
        };
        timer.Start();
    }

    /// <summary>the plain arrow means you are looking; the move cursor means
    /// you can grab things here, which is what edit mode is.</summary>
    void ApplyCursor()
    {
        Cursor = new Cursor(
            _spaceDown || (_drag && !Editing) ? StandardCursorType.SizeAll
            : Editing ? StandardCursorType.DragMove
            : StandardCursorType.Arrow);
    }

    void Select(int fileIndex, int from, int to)
    {
        _scene.Selection = (fileIndex, Math.Min(from, to), Math.Max(from, to));
        InvalidateVisual();
    }

    /// <summary>the tour narration, centred near the bottom.</summary>
    void DrawCaption(DrawingContext ctx)
    {
        if (_caption.Length == 0) return;
        var ft = new FormattedText(_caption, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Ui.Mono), 17, new SolidColorBrush(Color.FromRgb(0xff, 0xd1, 0x66)));
        double x = (Bounds.Width - ft.Width) / 2;
        double y = Bounds.Height - 78;
        var pad = 14.0;
        ctx.FillRectangle(Ui.PanelBg, new Rect(x - pad, y - 8, ft.Width + pad * 2, ft.Height + 16));
        ctx.DrawText(ft, new Point(x, y));
    }

    void Text(DrawingContext ctx, string s, int x, int y, Color c)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Ui.Mono), 12, new SolidColorBrush(c));
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

    void FlyTo(float x, float y, float s)
    {
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
    bool _spaceDown;
    bool _band;
    readonly List<string> _bandBase = [];
    readonly List<int> _bandBaseFiles = [];
    readonly Dictionary<string, (float X, float Y)> _unsnapped = [];
    Point _bandStart;
    float _bandFade = 1f;
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
        if (Editing == on) return;
        Editing = on;
        _dragItem = null;
        _armArrow = false;
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
    }
    int _clickCount = 1;
    bool _secondary;
    bool _armArrow;
    BoardItem? _arrowEnd;
    int _arrowEndWhich;
    ContextMenu? _menu;
    bool _boardDirty;

    // file under the cursor on the map, and the boards that reference it

    BookmarkStore? _store;
    PromptOverlay? _prompt;
    BookmarkOverlay? _marks;

    Tour? _tour;
    int _stop = -1;
    string _caption = "";

    // while recording, every bookmark saved joins the tour being built
    List<string>? _recording;

    public void Attach(BookmarkStore store, PromptOverlay prompt, BookmarkOverlay marks)
    {
        _store = store;
        _prompt = prompt;
        _marks = marks;
    }

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

    void RefreshBoardBar() =>
        _boardBar?.Reflect(_scene.ActiveBoard is not null && Editing, SnapToGrid);

    void AddOfKind(string kind)
    {
        switch (kind)
        {
            case "note": AddNote(); break;
            case "shape": AddShape(); break;
            case "arrow": AddArrow(); break;
            case "file": OpenSearch?.Invoke(); break;
        }
    }

    /// <summary>an arrow needs two picked items: it joins them and follows
    /// them when either is moved.</summary>
    /// <summary>arms the arrow tool: the next drag draws one.</summary>
    void AddArrow()
    {
        if (_scene.ActiveBoard is null) return;
        if (!Editing) SetEditing(true);
        _armArrow = true;
        _scene.Picked.Clear();
        Toast("drag to draw the arrow");
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

        if (_scene.ActiveBoard is not null)
        {
            items.Add(("undo", "ctrl+Z", Undo));
            items.Add(("redo", "ctrl+Y", Redo));
            items.Add(("add file", "/", () => OpenSearch?.Invoke()));
            items.Add(("board note", "N", AddNote));
            items.Add(("rectangle", "T", AddShape));
            items.Add(("boards", "O", () => _boards?.Show()));
            items.Add(("fit", "F", () => { _scene.FitBoard((float)Bounds.Width, (float)Bounds.Height); InvalidateVisual(); }));
            items.Add(("back to map", "esc", LeaveBoard));
        }
        else if (_scene.Review is not null)
        {
            items.Add(("previous commit", "[", () => StepCommit(-1)));
            items.Add(("next commit", "]", () => StepCommit(1)));
            items.Add(("leave review", "esc", LeaveReview));
        }
        else
        {
            items.Add(("search", "/", () => OpenSearch?.Invoke()));
            items.Add(("pull requests", "P", () => OpenReviewPanel(branches: false)));
            items.Add(("branches", "G", () => OpenReviewPanel(branches: true)));
            items.Add(("boards", "O", () => _boards?.Show()));
            items.Add(("notes", "L", () => _notes?.Open()));
            items.Add(("bookmarks", "B", () => _marks?.Open()));
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
        panel.Chosen += OpenTarget;
        _commitsPanel = commits;
        commits.Picked += GoToCommit;
    }

    void GoToCommit(int index)
    {
        if (_git is null || _target is null) return;
        if (index == _commitAt) return;
        _commitAt = Math.Clamp(index, -1, _prCommits.Count - 1);
        ShowChanges(_commitAt < 0 ? _git.Whole(_target) : _git.OfCommit(_prCommits[_commitAt]));
    }

    void OpenReviewPanel(bool branches)
    {
        if (_reviews is null) return;
        _git ??= GitReview.Open(_scene.Data.Root);
        if (_git is null) { Toast("this repo is not under git"); return; }
        Toast(branches ? "reading branches..." : "reading pull requests...");

        var targets = branches ? _git.Branches() : _git.MergedPrs();
        _reviews.Show(targets, _git.HeadName, branches ? "branches" : "pull requests");
        _caption = "";
    }

    void OpenTarget(ReviewTarget target)
    {
        if (_git is null) return;
        _target = target;
        _prCommits = _git.CommitsOf(target);
        _commitAt = -1;

        // draw the repo as it was at the branch head. without this a branch
        // older than a restructure changes paths that no longer exist, and
        // lights up nothing at all
        _caption = $"{target.Label}   -   reading the tree at this commit...";
        InvalidateVisual();

        var snapshot = _git.Snapshot(target.HeadSha, Scanner.Wanted);
        if (snapshot is not null && snapshot.Count > 0)
        {
            var data = Scanner.BuildFrom(_scene.Data.Root, snapshot);
            _scene.ShowSnapshot(data, path => snapshot.GetValueOrDefault(path));
        }

        _commitsPanel?.Show(target.Label, _prCommits);
        ShowChanges(_git.Whole(target));
    }

    /// <summary>-1 shows the whole pull request, 0..n a single commit.</summary>
    void StepCommit(int by)
    {
        if (_git is null || _target is null || _prCommits.Count == 0) return;
        int next = Math.Clamp(_commitAt + by, -1, _prCommits.Count - 1);
        if (next == _commitAt) return;
        _commitAt = next;
        ShowChanges(next < 0 ? _git.Whole(_target) : _git.OfCommit(_prCommits[next]));
    }

    void ShowChanges(ChangeSet? set)
    {
        if (set is null) return;
        _scene.Review = set;
        _scene.Highlight = null;

        // the details live in the commits panel now; a log line at the bottom
        // of the canvas was impossible to follow
        _caption = "";
        int placed = set.Files.Count(f => _scene.IndexOfPath(f.Path) >= 0);
        _commitsPanel?.Sync(_commitAt, set, placed, _scene.OnSnapshot);
        RefreshHints();

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
        _prCommits = [];
        _commitAt = -1;
        _caption = "";
        InvalidateVisual();
    }

    public void AttachNotes(AnnotationStore store, AnnotationOverlay panel)
    {
        _noteStore = store;
        _notes = panel;
    }

    /// <summary>the I key annotates the picked lines, or the middle of the view
    /// when nothing is picked.</summary>
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
        int i = _scene.IndexOfPath(a.File);
        if (i < 0) { _caption = $"{a.File} is not in this scan"; InvalidateVisual(); return; }

        int line = a.Line;
        AnchorKind kind = AnchorKind.Line;
        foreach (var (candidate, anchor) in _scene.AnchorsFor(a.File))
            if (ReferenceEquals(candidate, a)) { line = anchor.Line; kind = anchor.Kind; }

        var f = _scene.Data.Files[i];
        var bookmark = new Bookmark
        {
            Name = a.Text, File = a.File,
            Line = Math.Max(0, line - 12),
            EndLine = Math.Min(f.N - 1, line + 12),
        };
        FlyToBookmark(bookmark);
        _scene.Highlight = (i, line, line);
        _caption = kind is AnchorKind.Symbol or AnchorKind.Line ? a.Text : $"{a.Text}   [{kind}]";
    }

    public void AttachBoards(BoardStore store, BoardOverlay panel)
    {
        _boardStore = store;
        _boards = panel;
        panel.Open += OpenBoard;
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
        _boards?.Close();
        if (_scene.ActiveBoard is null) _mapCam = (_scene.CamX, _scene.CamY, _scene.CamS);
        _scene.ActiveBoard = b;
        _scene.Grid = Editing ? GridStep : 0;
        _scene.Picked.Clear();
        _history.Clear();
        _lastBoard = b;
        RefreshBoardBar();
        RefreshHints();
        _scene.FitBoard((float)Bounds.Width, (float)Bounds.Height);
        _caption = b.Name;
        Focus();
        InvalidateVisual();
    }

    void LeaveBoard()
    {
        DismissPrompt();
        if (_scene.ActiveBoard is null) return;
        SaveBoardIfDirty();
        PruneImages();
        _scene.ActiveBoard = null;
        _scene.Grid = 0;
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

        var mark = BookmarkTargets.Capture(_scene, "", (float)Bounds.Width, (float)Bounds.Height);
        // whichever board was open last, so adding several files in a row works
        var board = _lastBoard is not null && _boardStore.Boards.Contains(_lastBoard)
            ? _lastBoard
            : _boardStore.Boards[0];
        var item = new BoardItem
        {
            Id = BookmarkStore.NewId(),
            Kind = "file",
            File = mark.File,
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
        item.Y = board.Items.Max(i => i.Y + _scene.ItemHeight(i)) + 40;
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
            Id = BookmarkStore.NewId(), Kind = "image", File = name,
            W = w, H = h, X = _scene.CamX - w / 2, Y = _scene.CamY - h / 2,
        });
        _boardStore?.Save(board);
        Focus();
        Saved($"image on  {board.Name}");
    }

    /// <summary>a plain rectangle to group or point at things on a board.</summary>
    void AddShape()
    {
        if (_scene.ActiveBoard is not { } board) return;
        Remember();
        var item = new BoardItem
        {
            Id = BookmarkStore.NewId(), Kind = "shape", W = 520,
            X = _scene.CamX - 260, Y = _scene.CamY - 120, Text = "",
        };
        board.Items.Add(item);
        _boardStore?.Save(board);
        Saved($"rectangle on  {board.Name}");
    }

    static readonly (string Name, string Hex)[] Colours =
    [
        ("amber", "#ffd166"), ("cyan", "#5fd3f3"), ("green", "#3fb96a"),
        ("red", "#d95c5c"), ("violet", "#b48ae8"), ("slate", "#8aa0b0"),
    ];

    List<MenuItem> Palette(List<BoardItem> picked) =>
        Colours.Select(c => ContextActions.Item(c.Name, () =>
        {
            Remember();
            foreach (var it in picked) it.Color = c.Hex;
            if (_scene.ActiveBoard is { } b) _boardStore?.Save(b);
            Saved("colour");
        })).ToList();

    readonly List<BoardItem> _clipboard = [];
    readonly History _history = new();

    /// <summary>snapshot the board before changing it.</summary>
    void Remember()
    {
        if (_scene.ActiveBoard is { } b) _history.Record(b);
    }

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

    void CopyPicked(List<BoardItem> picked)
    {
        _clipboard.Clear();
        foreach (var it in picked)
            _clipboard.Add(new BoardItem
            {
                Id = it.Id, Kind = it.Kind, File = it.File, Line = it.Line, EndLine = it.EndLine,
                Text = it.Text, X = it.X, Y = it.Y, W = it.W, H = it.H, Color = it.Color,
            });
        Toast($"copied {picked.Count}");
    }

    /// <summary>a copied file window is a reference, not a copy of the code.</summary>
    void Paste(Point at)
    {
        if (_scene.ActiveBoard is not { } board || _clipboard.Count == 0) return;
        Remember();
        var (wx, wy) = WorldAt(at);
        float ox = _clipboard.Min(i => i.X), oy = _clipboard.Min(i => i.Y);

        _scene.Picked.Clear();
        foreach (var it in _clipboard)
        {
            var copy = new BoardItem
            {
                Id = BookmarkStore.NewId(), Kind = it.Kind, File = it.File,
                Line = it.Line, EndLine = it.EndLine, Text = it.Text,
                X = wx + (it.X - ox), Y = wy + (it.Y - oy),
                W = it.W, H = it.H, Color = it.Color,
            };
            board.Items.Add(copy);
            _scene.Picked.Add(copy.Id);
        }
        _boardStore?.Save(board);
        Saved($"pasted {_clipboard.Count}");
    }

    void EditNote(BoardItem note)
    {
        if (_prompt is null) return;
        _prompt.Ask("edit the note", note.Text ?? "", text =>
        {
            Remember();
            note.Text = text;
            if (_scene.ActiveBoard is { } b) _boardStore?.Save(b);
            Saved("note");
            Focus();
        });
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
        int n = board.Items.RemoveAll(i => _scene.Picked.Contains(i.Id));
        _scene.Picked.Clear();
        _boardStore?.Save(board);
        _boards?.Rebuild();
        Saved("removed " + n + (n == 1 ? " item" : " items"));
    }

    void AddNote()
    {
        if (_scene.ActiveBoard is not { } board || _prompt is null) return;
        _prompt.Ask("note text", "", text =>
        {
            Remember();
            var item = new BoardItem
            {
                Id = BookmarkStore.NewId(), Kind = "note", Text = text,
                W = 380,
                // where the user is looking, not off beside everything else
                X = _scene.CamX - 190,
                Y = _scene.CamY - 40,
            };
            board.Items.Add(item);
            _boardStore?.Save(board);
            _boards?.Rebuild();
            Focus();
            InvalidateVisual();
        });
    }

    public void FlyToBookmark(Bookmark b)
    {
        var t = BookmarkTargets.Resolve(_scene, b, (float)Bounds.Width, (float)Bounds.Height);
        _caption = t.Orphaned ? $"{b.Name}  (file is gone: {b.File})" : b.Name;

        int i = b.File is null ? -1 : _scene.IndexOfPath(b.File);
        _scene.Highlight = i >= 0 && b.EndLine >= b.Line && b.Line >= 0
            ? (i, b.Line, b.EndLine)
            : null;

        FlyTo(t.X, t.Y, t.S);
    }

    public void PlayTour(Tour tour)
    {
        if (_store is null || tour.Stops.Count == 0) return;
        _tour = tour;
        _stop = -1;
        Step(1);
    }

    void Step(int by)
    {
        if (_tour is null || _store is null) return;
        int next = _stop + by;
        if (next < 0) return;
        if (next >= _tour.Stops.Count) { EndTour(); return; }

        _stop = next;
        var b = _store.ById(_tour.Stops[_stop]);
        if (b is null) { Step(by); return; }  // stop was deleted, skip it
        FlyToBookmark(b);
        _caption = $"{_tour.Name}   {_stop + 1}/{_tour.Stops.Count}   -   {_caption}";
    }

    void EndTour()
    {
        _tour = null;
        _stop = -1;
        _caption = "";
        _scene.Highlight = null;
        InvalidateVisual();
    }

    void SaveBookmark()
    {
        if (_store is null || _prompt is null) return;
        var suggested = _scene.FileAt(_scene.CamX, _scene.CamY) is var i && i >= 0
            ? Path.GetFileNameWithoutExtension(_scene.Data.Files[i].P)
            : "view";
        _prompt.Ask("name this bookmark", suggested, name =>
        {
            var b = BookmarkTargets.Capture(_scene, name, (float)Bounds.Width, (float)Bounds.Height);
            _store.Bookmarks.Add(b);
            _recording?.Add(b.Id);
            _store.Save();
            _caption = _recording is null
                ? $"saved  {b.Name}"
                : $"saved  {b.Name}   (stop {_recording.Count} of this tour)";
            InvalidateVisual();
            Focus();
        });
    }

    void ToggleRecording()
    {
        if (_store is null || _prompt is null) return;
        if (_recording is null)
        {
            _recording = [];
            _caption = "recording a tour - press M at each stop, R to finish";
            InvalidateVisual();
            return;
        }
        var stops = _recording;
        _recording = null;
        if (stops.Count == 0) { _caption = "tour discarded - no stops"; InvalidateVisual(); return; }

        _prompt.Ask("name this tour", "tour", name =>
        {
            _store.Tours.Add(new Tour { Id = BookmarkStore.NewId(), Name = name, Stops = stops });
            _store.Save();
            _caption = $"saved tour  {name}   ({stops.Count} stops)";
            InvalidateVisual();
            Focus();
        });
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
        board.Items.Add(new BoardItem
        {
            Id = BookmarkStore.NewId(), Kind = "file", File = f.P,
            Line = 0, EndLine = -1, W = 620,
            X = _scene.CamX - 310, Y = _scene.CamY - 120,
        });
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
            _drag = true;
            _last = _bandStart;
            _clickCount = e.ClickCount;
            InvalidateVisual();
            return;
        }

        if (_scene.ActiveBoard is not null && Editing && !_spaceDown && !_secondary)
        {
            var (wx, wy) = WorldAt(e.GetPosition(this));

            if (_armArrow)
            {
                _scene.ArrowDraft = (new SkiaSharp.SKPoint(wx, wy), new SkiaSharp.SKPoint(wx, wy));
                _drag = true;
                _last = e.GetPosition(this);
                return;
            }

            if (_scene.ArrowEndAt(wx, wy) is { } end)
            {
                Remember();
                _arrowEnd = end.Arrow;
                _arrowEndWhich = end.End;
                _drag = true;
                _last = e.GetPosition(this);
                return;
            }

            _resizing = _scene.GripAt(wx, wy);
            if (_resizing is not null) { Remember(); _drag = true; _last = e.GetPosition(this); return; }

            var hit = _scene.ItemAt(wx, wy) ?? _scene.ArrowAt(wx, wy);
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
                _drag = true;
                _last = _bandStart;
                InvalidateVisual();
                return;
            }

            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                if (!_scene.Picked.Add(hit.Id)) _scene.Picked.Remove(hit.Id);
            }
            else if (!_scene.Picked.Contains(hit.Id))
            {
                _scene.Picked.Clear();
                _scene.Picked.Add(hit.Id);
            }
            if (_dragItem != hit) Remember();
            _dragItem = hit;
        }

        _clickCount = e.ClickCount;
        _drag = true;
        ApplyCursor();
        _dragDist = 0;
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
            if (!moved) ShowContextMenu(e.GetPosition(this));
            return;
        }

        _drag = false;
        _axis = 0;
        ApplyCursor();

        if (_scene.ArrowDraft is { } made)
        {
            _scene.ArrowDraft = null;
            _armArrow = false;
            if (_scene.ActiveBoard is { } b &&
                (Math.Abs(made.B.X - made.A.X) > 4 || Math.Abs(made.B.Y - made.A.Y) > 4))
            {
                Remember();
                b.Items.Add(new BoardItem
                {
                    Id = BookmarkStore.NewId(), Kind = "arrow",
                    X = made.A.X, Y = made.A.Y, X2 = made.B.X, Y2 = made.B.Y,
                });
                _boardStore?.Save(b);
                Saved("arrow");
            }
            _drag = false;
            e.Pointer.Capture(null);
            return;
        }
        _arrowEnd = null;

        if (_band) { _band = false; FadeRubberband(); }

        _dragItem = null;
        _resizing = null;
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
        if (_scene.ActiveBoard is null) UpdateHoverLine(p);

        if (!_drag) return;
        _dragDist += Math.Abs(p.X - _last.X) + Math.Abs(p.Y - _last.Y);

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
            if (_arrowEndWhich == 1) { _arrowEnd.X = ex; _arrowEnd.Y = ey; }
            else { _arrowEnd.X2 = ex; _arrowEnd.Y2 = ey; }
            _boardDirty = true;
            _last = p;
            InvalidateVisual();
            return;
        }

        if (_resizing is not null)
        {
            float ratio = _scene.ItemHeight(_resizing) / Math.Max(1, _resizing.W);
            // the box changes; the font stays where it is
            _resizing.W = Math.Max(120, _resizing.W + (float)(p.X - _last.X) / _scene.CamS);
            _resizing.H = _resizing.Kind == "image"
                ? _resizing.W * ratio                  // an image keeps its shape
                : Math.Max(40, _scene.ItemHeight(_resizing) + (float)(p.Y - _last.Y) / _scene.CamS);
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

                float nx = SnapToGrid ? MathF.Round(raw.X / GridStep) * GridStep : raw.X;
                float ny = SnapToGrid ? MathF.Round(raw.Y / GridStep) * GridStep : raw.Y;
                float dx = nx - it.X, dy = ny - it.Y;
                it.X = nx;
                it.Y = ny;
                if (it.Kind == "arrow") { it.X2 += dx; it.Y2 += dy; }
            }
            _boardDirty = true;
        }
        else
        {
            _scene.CamX -= (float)(p.X - _last.X) / _scene.CamS;
            _scene.CamY -= (float)(p.Y - _last.Y) / _scene.CamS;
        }
        _last = p;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _flight = null;
        var p = e.GetPosition(this);

        // a tilt wheel or a trackpad swipe reads sideways in either mode
        if (Math.Abs(e.Delta.X) > 0.01)
        {
            _scene.CamX -= (float)e.Delta.X * (float)Bounds.Width * 0.12f / _scene.CamS;
            InvalidateVisual();
            return;
        }

        // in scroll mode the wheel reads down the file; shift reads across it
        // in either mode; ctrl still zooms
        bool sideways = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if ((!WheelZoom || sideways) && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            float across = (float)Bounds.Width * 0.12f / _scene.CamS;
            float down = (float)Bounds.Height * 0.12f / _scene.CamS;
            if (sideways) _scene.CamX -= (float)e.Delta.Y * across;
            else _scene.CamY -= (float)e.Delta.Y * down;
            InvalidateVisual();
            return;
        }

        float vw = (float)Bounds.Width, vh = (float)Bounds.Height;
        // keep the world point under the cursor pinned while zooming
        float wx = _scene.CamX + ((float)p.X - vw / 2) / _scene.CamS;
        float wy = _scene.CamY + ((float)p.Y - vh / 2) / _scene.CamS;
        _scene.CamS = Math.Clamp(_scene.CamS * MathF.Exp((float)e.Delta.Y * 0.18f), 0.006f, 40f);
        _scene.CamX = wx - ((float)p.X - vw / 2) / _scene.CamS;
        _scene.CamY = wy - ((float)p.Y - vh / 2) / _scene.CamS;
        InvalidateVisual();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key != Key.Space || !_spaceDown) return;
        _spaceDown = false;
        ApplyCursor();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // hold space to pan without leaving edit mode
        if (e.Key == Key.Space && _scene.ActiveBoard is not null && _tour is null)
        {
            if (!_spaceDown) { _spaceDown = true; ApplyCursor(); }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            OpenSearch?.Invoke();
            e.Handled = true;
            return;
        }
        _ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        HandleKey(e.Key, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
    }

    bool _ctrl;

    public void HandleKey(Key key) => HandleKey(key, false);

    public void HandleKey(Key key, bool e_shift)
    {
        if (key == Key.Escape && _prompt is { IsVisible: true })
        {
            _prompt.Close();
            Focus();
            InvalidateVisual();
            return;
        }

        // panels have no focus of their own; the canvas drives them
        if (_marks is { IsVisible: true } && _marks.HandleKey(key)) { InvalidateVisual(); return; }
        if (_boards is { IsVisible: true } && _boards.HandleKey(key)) { InvalidateVisual(); return; }
        if (_notes is { IsVisible: true } && _notes.HandleKey(key)) { InvalidateVisual(); return; }
        if (_reviews is { IsVisible: true } && _reviews.HandleKey(key)) { InvalidateVisual(); return; }

        if (_scene.Review is not null)
        {
            switch (key)
            {
                case Key.OemCloseBrackets: StepCommit(1); return;
                case Key.OemOpenBrackets: StepCommit(-1); return;
                case Key.Escape: LeaveReview(); return;
            }
        }

        if (_scene.ActiveBoard is not null)
        {
            switch (key)
            {
                case Key.Escape or Key.Back: LeaveBoard(); return;
                case Key.N: AddNote(); return;
                case Key.T: AddShape(); return;
                case Key.G:
                    SnapToGrid = !SnapToGrid;
                    RefreshBoardBar();
                    Toast(SnapToGrid ? "snap on" : "snap off");
                    return;
                case Key.V when _ctrl: PasteImage(); return;
                case Key.Z when _ctrl: Undo(); return;
                case Key.Y when _ctrl: Redo(); return;
                case Key.Y: AddArrow(); return;
                case Key.Delete: DeletePicked(); return;
                case Key.F: _scene.FitBoard((float)Bounds.Width, (float)Bounds.Height); InvalidateVisual(); return;
                case Key.O: _boards?.Show(); InvalidateVisual(); return;
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
            case Key.D: _scene.ShowDistricts = !_scene.ShowDistricts; break;
            case Key.M: SaveBookmark(); break;
            case Key.O: _boards?.Show(); break;
            case Key.A: AddViewToBoard(); break;
            case Key.I: Annotate(); break;
            case Key.E: SetEditing(!Editing); break;
            case Key.S: SetWheelZoom(!WheelZoom); break;
            case Key.L: _notes?.Open(); break;
            case Key.P: OpenReviewPanel(branches: false); break;
            case Key.R: ToggleRecording(); break;
            case Key.B: _marks?.Open(); break;
            case Key.Space or Key.Right when _tour is not null: Step(1); break;
            case Key.Left when _tour is not null: Step(-1); break;
            case Key.Escape: EndTour(); break;
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

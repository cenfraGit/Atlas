namespace Atlas;

/// <summary>writes the shared sample boards and annotations into a repo, built
/// through the real anchoring code so the symbols and fingerprints are genuine.
/// the samples describe Atlas itself, so running it on this repo gives you a
/// tour of the thing you are looking at. run: dotnet run -- --samples &lt;repo&gt;</summary>
public static class Samples
{
    /// <summary>what to say about a file, and which declaration to say it at.
    /// A suffix rather than a full path, so the files can move.
    ///
    /// Methods rather than classes, deliberately. A note is tinted
    /// across the lines it covers, so pointing one at a nine hundred line
    /// class either tints all of it or tints an arbitrary window onto it -
    /// and neither reads as a note about anything. A method is a thing a
    /// sentence can be about.</summary>
    static readonly (string Suffix, string? Symbol, string Text)[] Notes =
    [
        ("Scene.cs", "TierFor",
            "What you see changes with the zoom rather than just getting bigger. Four tiers: " +
            "folders, then cards, then a coloured bar per line, then real text."),
        ("Scanner.cs", "Classify",
            "Every line is sorted into a kind once, at scan time, so drawing the bars tier is " +
            "a lookup rather than a parse. Layout lives here too, not in the view."),
        ("Annotations.cs", "Resolve",
            "Symbol first, then the context fingerprint, then the stored line. That ladder is " +
            "what keeps a note attached while the code moves underneath it."),
        ("FileKeys.cs", "Of",
            "A hash of the first forty meaningful lines with the whitespace taken out, so a " +
            "reference finds its file again after a rename, and reindenting changes nothing."),
        ("Images.cs", "Prune",
            "Images nothing points at are deleted - but only once the undo history that could " +
            "bring one back has been dropped."),
        ("Strokes.cs", "Erase",
            "Rubbing a hole in a stroke leaves the surviving pieces as strokes in their own " +
            "right, so everything that works on a stroke goes on working on them."),
    ];

    static readonly (string Id, string Name, (string Suffix, string? Symbol, string Note)[] Parts)[] Boards =
    [
        ("sample-1", "How a frame is drawn",
        [
            ("Scanner.cs", "Scanner", "First the repo is read and laid out. Once only, cached to data/scan.json."),
            ("Scene.cs", "Scene", "Then the camera decides the tier, and each tier draws a different thing."),
            ("Highlighter.cs", "Highlighter", "Syntax colours come from TextMate grammars, built once per file."),
        ]),
        ("sample-2", "How a note stays attached",
        [
            ("Annotations.cs", "Anchors", "A note records the symbol it was written on, plus the lines around it."),
            ("Symbols.cs", "Symbols", "Symbols come from Roslyn, parsing only: no build, no project references."),
            ("FileKeys.cs", "FileKeys", "And the file itself is fingerprinted, so a rename does not orphan anything."),
        ]),
    ];

    public static void Run(string[] args)
    {
        var repo = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--") && Directory.Exists(a));
        if (repo is null) { Console.WriteLine("usage: --samples <repo>"); return; }

        var scan = Scanner.Build(repo);
        var scene = new Scene(scan);
        Console.WriteLine($"{scan.Files.Count} files in {repo}");

        var notes = AnnotationStore.Load(repo);
        var boards = BoardStore.Load(repo);
        notes.Annotations.RemoveAll(a => a.Id.StartsWith("sample"));
        foreach (var b in boards.Boards.Where(b => b.Id.StartsWith("sample")).ToList()) boards.Delete(b);

        int made = 0;
        foreach (var (suffix, symbol, text) in Notes)
            made += Annotate(scene, notes, repo, suffix, symbol, text);
        notes.Save();

        foreach (var (id, name, parts) in Boards)
            Console.WriteLine("  board: " + MakeBoard(scene, boards, id, name, parts));
        Console.WriteLine("  board: " + MakeMessyBoard(scene, boards));
        Console.WriteLine($"{made} annotations");
    }

    /// <summary>a stable id from a name. Everything a sample writes is
    /// derived rather than generated, so `--samples` run twice leaves the
    /// files byte for byte the same and git has nothing to report.</summary>
    static string IdFor(string name) =>
        "sample-" + new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

    static int Annotate(Scene scene, AnnotationStore notes, string repo, string suffix, string? symbol, string text)
    {
        var f = scene.Data.Files.FirstOrDefault(x => x.P.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (f is null) { Console.WriteLine($"  skip: no {suffix}"); return 0; }

        var full = Path.Combine(repo, f.P.Replace('/', Path.DirectorySeparatorChar));
        string[] lines;
        try { lines = File.ReadAllLines(full); } catch { return 0; }

        var (line, end) = SpanOf(full, symbol, lines.Length);
        var a = Anchors.Create(f.P, full, lines, line, end, text);
        a.Id = IdFor(suffix + "-" + (symbol ?? "top"));
        notes.Annotations.Add(a);
        Console.WriteLine($"  annotated {f.P}:{line + 1}  -> {a.Symbol ?? "(no symbol)"}");
        return 1;
    }

    /// <summary>the lines the named declaration actually occupies.
    ///
    /// These used to be a flat eight lines down from wherever the declaration
    /// started, which is why a sample note stopped in the middle of a class
    /// and delimited nothing in particular. Roslyn already knows where the
    /// thing ends, so the tint reads as a note about the declaration rather
    /// than about an arbitrary window onto it.
    ///
    /// A declaration too long to tint whole gets its first line only. Tinting
    /// the first sixty lines of a nine hundred line class would be arbitrary
    /// in exactly the way this is meant to stop being: the note is about the
    /// class, and the line that names it is where the class is.</summary>
    static (int From, int To) SpanOf(string fullPath, string? symbol, int lineCount)
    {
        const int TooLongToTint = 60;
        int last = Math.Max(0, lineCount - 1);

        var sym = Find(fullPath, symbol);
        if (sym is not { } s) return (0, 0);

        int from = Math.Clamp(s.StartLine, 0, last);
        int to = Math.Clamp(s.EndLine, from, last);
        return to - from > TooLongToTint ? (from, from) : (from, to);
    }

    /// <summary>match on the name a human would write. Roslyn's names carry
    /// an arity - Demo.Startup.Configure(0) - which nothing here wants to
    /// spell out, so the arity comes off before comparing.</summary>
    static SymbolSpan? Find(string fullPath, string? symbol)
    {
        var syms = Symbols.ForFile(fullPath);
        if (syms.Count == 0) return null;
        if (symbol is null) return syms[0];

        foreach (var s in syms)
        {
            var name = s.Name;
            int paren = name.IndexOf('(');
            if (paren >= 0) name = name[..paren];
            if (name == symbol || name.EndsWith("." + symbol, StringComparison.Ordinal)) return s;
        }
        return syms[0];
    }

    /// <summary>first line of the named declaration, else the first type in the file.</summary>
    static int? LineOf(string fullPath, string? symbol)
    {
        var syms = Symbols.ForFile(fullPath);
        if (syms.Count == 0) return null;
        if (symbol is not null)
        {
            var hit = syms.FirstOrDefault(s => s.Name.EndsWith("." + symbol, StringComparison.Ordinal)
                                               || s.Name == symbol);
            if (hit.Name is not null) return hit.StartLine;
        }
        return syms[0].StartLine;
    }

    static string MakeBoard(Scene scene, BoardStore boards, string id, string name,
        (string Suffix, string? Symbol, string Note)[] parts)
    {
        var board = boards.Create(name, id);
        float y = 0;
        int n = 0;

        foreach (var (suffix, symbol, note) in parts)
        {
            var f = scene.Data.Files.FirstOrDefault(x => x.P.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (f is null) { Console.WriteLine($"  skip board item: no {suffix}"); continue; }

            var full = Path.Combine(scene.Data.Root, f.P.Replace('/', Path.DirectorySeparatorChar));
            int start = LineOf(full, symbol) ?? 0;
            int end = Math.Min(f.N - 1, start + 34);

            board.Items.Add(new BoardItem
            {
                Id = $"{id}-{n++}", Kind = "file", File = f.P, Key = scene.KeyFor(f.P),
                Line = start, EndLine = end, X = 0, Y = y, W = 620,
            });
            board.Items.Add(new BoardItem
            {
                Id = $"{id}-{n++}", Kind = "note", Text = note,
                // clear of the annotation callouts, which sit just right
                // of the window
                X = 900, Y = y, W = 380,
            });
            float h = (end - start + 1) * scene.Data.LineH * (620f / f.W) + 26;
            // a stop per window and its note, so the board comes with a tour
            // through it - there is no other way to see one without making
            // it by hand first
            board.Stops.Add(new Stop { Name = Path.GetFileName(f.P), X = 640, Y = y + h / 2, W = 1400, H = h + 120 });
            y += h + 48;
        }

        // and the whole board first, so the tour opens on where it is going
        if (board.Stops.Count > 0)
            board.Stops.Insert(0, new Stop { Name = "the whole board", X = 640, Y = y / 2, W = 1500, H = y + 120 });

        boards.Save(board);
        return $"{name} ({board.Items.Count} items)";
    }

    /// <summary>a board that is deliberately awkward.
    ///
    /// The two tidy boards are for reading; this one is for editing. Every
    /// kind of item, shapes that overlap, a stroke, an empty shape over other
    /// things, connectors between boxes, a long file window and a label. The
    /// things that break in edit mode break on a board like this: picking the
    /// wrong item of an overlapping pair, a rubberband that takes too much,
    /// a connector left pointing at nothing.</summary>
    static string MakeMessyBoard(Scene scene, BoardStore boards)
    {
        var board = boards.Create("Everything at once", "sample-3");

        void Add(BoardItem it) => board.Items.Add(it);

        // counted rather than random: the whole point of a sample is that
        // regenerating it rewrites the same file, and an id nothing else
        // refers to is still a line of the diff
        int n = 0;
        string Id() => $"sample-3-{n++}";

        Add(new BoardItem
        {
            Id = Id(), Kind = "text", Text = "a board to break", Size = 44,
            X = 0, Y = -110, W = 800, Color = "#ffd166",
        });

        // two boxes to connect, and a third overlapping one of them
        var a = new BoardItem { Id = Id(), Kind = "shape", X = 0, Y = 0, W = 300, H = 140, Color = "#5fd3f3" };
        var b = new BoardItem { Id = Id(), Kind = "ellipse", X = 620, Y = 40, W = 280, H = 160, Color = "#3fb96a" };
        var c = new BoardItem
        {
            Id = Id(), Kind = "diamond", X = 220, Y = 90, W = 240, H = 180,
            Color = "#b48ae8", Fill = "#b48ae8",
        };
        Add(a);
        Add(b);
        Add(c);

        // a frame with no fill, laid over the lot: picking through it is
        // exactly the thing that is easy to get wrong
        Add(new BoardItem
        {
            Id = Id(), Kind = "shape", X = -40, Y = -40, W = 1000, H = 360,
            Color = "#8aa0b0", Fill = BoardItem.NoFill,
        });

        Add(new BoardItem
        {
            Id = Id(), Kind = "arrow", From = a.Id, To = b.Id,
            FromSide = Scene.Right, ToSide = Scene.Left, Color = "#ffd166",
            X = 300, Y = 70, X2 = 620, Y2 = 120,
        });
        // one end tied and one loose, which is the awkward case
        Add(new BoardItem
        {
            Id = Id(), Kind = "arrow", From = c.Id, FromSide = Scene.Bottom,
            X = 340, Y = 270, X2 = 180, Y2 = 470, Color = "#d95c5c",
        });

        // a stroke laid across the shapes
        var ink = new BoardItem { Id = Id(), Kind = "stroke", Color = "#d95c5c", Weight = 5 };
        for (int i = 0; i <= 40; i++)
        {
            float t = i / 40f;
            Strokes.Add(ink, 40 + t * 840, 300 + MathF.Sin(t * 7) * 40, minStep: 0);
        }
        Add(ink);

        Add(new BoardItem
        {
            Id = Id(), Kind = "note", X = 980, Y = 300, W = 360,
            Text = "notes, shapes, ink, connectors and a file window, " +
                   "overlapping on purpose. If edit mode has a bug, it shows up here.",
        });

        // something long, so the board does not fit on one screen
        var big = scene.Data.Files.OrderByDescending(f => f.N).FirstOrDefault();
        if (big is not null)
            Add(new BoardItem
            {
                Id = Id(), Kind = "file", File = big.P, Key = scene.KeyFor(big.P),
                Line = 0, EndLine = Math.Min(big.N - 1, 120),
                X = 0, Y = 520, W = 620,
            });

        boards.Save(board);
        return $"Everything at once ({board.Items.Count} items)";
    }
}

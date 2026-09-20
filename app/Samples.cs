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
        Console.WriteLine($"{made} annotations");
    }

    static int Annotate(Scene scene, AnnotationStore notes, string repo, string suffix, string? symbol, string text)
    {
        var f = scene.Data.Files.FirstOrDefault(x => x.P.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (f is null) { Console.WriteLine($"  skip: no {suffix}"); return 0; }

        var full = Path.Combine(repo, f.P.Replace('/', Path.DirectorySeparatorChar));
        string[] lines;
        try { lines = File.ReadAllLines(full); } catch { return 0; }

        var (line, end) = SpanOf(full, symbol, lines.Length);
        var a = Anchors.Create(f.P, full, lines, line, end, text);
        a.Id = "sample-" + Guid.NewGuid().ToString("n")[..6];
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
        var board = boards.Create(name);
        board.Id = id;
        float y = 0;

        foreach (var (suffix, symbol, note) in parts)
        {
            var f = scene.Data.Files.FirstOrDefault(x => x.P.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (f is null) { Console.WriteLine($"  skip board item: no {suffix}"); continue; }

            var full = Path.Combine(scene.Data.Root, f.P.Replace('/', Path.DirectorySeparatorChar));
            int start = LineOf(full, symbol) ?? 0;
            int end = Math.Min(f.N - 1, start + 34);

            board.Items.Add(new BoardItem
            {
                Id = BookmarkStore.NewId(), Kind = "file", File = f.P,
                Line = start, EndLine = end, X = 0, Y = y, W = 620,
            });
            board.Items.Add(new BoardItem
            {
                Id = BookmarkStore.NewId(), Kind = "note", Text = note,
                // clear of the annotation callouts, which sit just right
                // of the window
                X = 900, Y = y, W = 380,
            });
            y += (end - start + 1) * scene.Data.LineH * (620f / f.W) + 26 + 48;
        }

        boards.Save(board);
        return $"{name} ({board.Items.Count} items)";
    }
}

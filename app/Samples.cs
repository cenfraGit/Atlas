namespace Atlas;

/// <summary>writes the shared sample boards and annotations into a repo, built
/// through the real anchoring code so the symbols and fingerprints are genuine.
/// the samples describe Atlas itself, so running it on this repo gives you a
/// tour of the thing you are looking at. run: dotnet run -- --samples &lt;repo&gt;</summary>
public static class Samples
{
    /// <summary>what to say about a file, and which declaration to say it at.
    /// a suffix rather than a full path, so the files can move.</summary>
    static readonly (string Suffix, string? Symbol, string Text)[] Notes =
    [
        ("Scene.cs", "Scene",
            "The renderer. It holds the camera, decides the level of detail from the zoom, " +
            "and replays one recorded picture per file instead of redrawing the geometry."),
        ("Scanner.cs", "Scanner",
            "Reads the repo once and lays every file out by directory. Nothing here knows " +
            "about drawing: it produces positions and line classifications, and that is all."),
        ("Annotations.cs", "Anchors",
            "A note is anchored to a symbol plus the lines around it, never to a line number. " +
            "This is what survives the file being edited underneath it."),
        ("Boards.cs", "BoardStore",
            "Boards are one JSON file each so two people editing different boards never conflict."),
        ("Images.cs", "ImageStore",
            "Images are re-encoded on the way in. A pasted screenshot is megabytes of raw " +
            "clipboard data, and it would sit in git forever at that size."),
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

        int line = LineOf(full, symbol) ?? 0;
        // span the declaration's opening lines so the tint reads as a note
        // about the thing, not about one line of it
        int end = Math.Min(lines.Length - 1, line + 8);
        var a = Anchors.Create(f.P, full, lines, line, end, text);
        a.Id = "sample-" + Guid.NewGuid().ToString("n")[..6];
        notes.Annotations.Add(a);
        Console.WriteLine($"  annotated {f.P}:{line + 1}  -> {a.Symbol ?? "(no symbol)"}");
        return 1;
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

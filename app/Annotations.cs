using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atlas;

public sealed class Annotation
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";

    /// <summary>content fingerprint of that file, so a rename does not orphan
    /// the note. see FileKeys.</summary>
    [JsonPropertyName("key")] public string? Key { get; set; }

    /// <summary>qualified declaration this hangs off, when the language can be
    /// parsed. the whole point: a symbol survives edits above it, a line does not.</summary>
    [JsonPropertyName("symbol")] public string? Symbol { get; set; }

    /// <summary>lines from the start of that symbol.</summary>
    [JsonPropertyName("offset")] public int Offset { get; set; }

    /// <summary>how many lines the annotation covers. one line reads as if the
    /// note were about that line alone, which is wrong when a method was picked.</summary>
    [JsonPropertyName("span")] public int Span { get; set; } = 1;

    /// <summary>line recorded when the annotation was made. only a fallback,
    /// and only trusted when the context still matches.</summary>
    [JsonPropertyName("line")] public int Line { get; set; }

    /// <summary>fingerprint of the anchored line and its neighbours, used to
    /// re-find the spot when the symbol was renamed or removed.</summary>
    [JsonPropertyName("context")] public string? Context { get; set; }

    /// <summary>the board this note belongs to, or null for everywhere.
    ///
    /// An annotation is attached to code, so by default it shows wherever
    /// that code does - on the map and in every board window onto the file.
    /// That is right for "this method is the hot path" and wrong for "this is
    /// step 2 of what this board is explaining", which is about the board and
    /// would be noise anywhere else.
    ///
    /// The board's id *is* the scope rather than a separate flag beside one:
    /// there is no such thing as local to nothing, and a pair of fields that
    /// can disagree is a pair of fields that eventually will.</summary>
    [JsonPropertyName("board")] public string? Board { get; set; }

    /// <summary>shown everywhere, rather than on one board.</summary>
    [JsonIgnore] public bool Global => Board is null;
}

public enum AnchorKind
{
    /// <summary>the declaration and the exact lines were both found.</summary>
    Symbol,
    /// <summary>the declaration is gone, but the same lines were found elsewhere.</summary>
    Context,
    /// <summary>nothing moved; the stored line still matches.</summary>
    Line,
    /// <summary>the declaration is still there but the annotated lines are not.
    /// points at the declaration, which is worth more than nothing.</summary>
    Drifted,
    /// <summary>neither the declaration nor the lines survive.</summary>
    Orphan,
}

public readonly record struct Anchor(int Line, AnchorKind Kind)
{
    public bool Resolved => Kind != AnchorKind.Orphan;
}

public sealed class AnnotationStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // LF, not this machine's newline. Everything in .atlas/ is committed,
        // and a file written with CRLF on windows shows up as modified in git
        // the moment the app saves it, however little changed
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [JsonPropertyName("annotations")] public List<Annotation> Annotations { get; set; } = [];

    [JsonIgnore] public string Path { get; private set; } = "";

    public static string PathFor(string repoRoot) =>
        System.IO.Path.Combine(repoRoot, ".atlas", "annotations.json");

    public static AnnotationStore Load(string repoRoot)
    {
        var path = PathFor(repoRoot);
        AnnotationStore store;
        try
        {
            store = System.IO.File.Exists(path)
                ? JsonSerializer.Deserialize<AnnotationStore>(System.IO.File.ReadAllText(path), Options) ?? new AnnotationStore()
                : new AnnotationStore();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"could not read {path}: {ex.Message}");
            store = new AnnotationStore();
        }
        store.Path = path;
        return store;
    }

    public void Save()
    {
        if (Path.Length == 0) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            System.IO.File.WriteAllText(Path, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"could not write {Path}: {ex.Message}");
        }
    }

    public IEnumerable<Annotation> For(string file) => Annotations.Where(a => a.File == file);
}

public static class Anchors
{
    const int ContextRadius = 2;

    /// <summary>fingerprint of a line and its neighbours, whitespace removed so
    /// reindenting does not break it.</summary>
    public static string ContextOf(string[] lines, int line)
    {
        var parts = new List<string>();
        for (int i = line - ContextRadius; i <= line + ContextRadius; i++)
            parts.Add(i >= 0 && i < lines.Length ? Normalize(lines[i]) : "");
        return Hash(string.Join("", parts));
    }

    static string Normalize(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        int n = 0;
        foreach (var ch in s) if (!char.IsWhiteSpace(ch)) buf[n++] = ch;
        return new string(buf[..n]);
    }

    static string Hash(string s)
    {
        var bytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes)[..12];
    }

    public static Annotation Create(string relPath, string fullPath, string[] lines, int line, string text) =>
        Create(relPath, fullPath, lines, line, line, text);

    public static Annotation Create(string relPath, string fullPath, string[] lines,
        int from, int to, string text)
    {
        int line = Math.Min(from, to);
        var a = new Annotation
        {
            Id = BookmarkStore.NewId(),
            Text = text,
            File = relPath,
            Line = line,
            Span = Math.Max(1, Math.Abs(to - from) + 1),
            Context = ContextOf(lines, line),
            Key = FileKeys.Of(lines),
        };
        var sym = Symbols.Innermost(Symbols.ForFile(fullPath), line);
        if (sym is { } s)
        {
            a.Symbol = s.Name;
            a.Offset = line - s.StartLine;
        }
        return a;
    }

    /// <summary>find where an annotation belongs now. symbol first, then the
    /// context fingerprint, then the stored line only if it still matches.</summary>
    public static Anchor Resolve(Annotation a, string fullPath, string[] lines)
    {
        if (lines.Length == 0) return new Anchor(0, AnchorKind.Orphan);
        int last = lines.Length - 1;

        if (a.Symbol is not null)
        {
            foreach (var s in Symbols.ForFile(fullPath))
            {
                if (s.Name != a.Symbol) continue;
                int from = Math.Clamp(s.StartLine, 0, last);
                int to = Math.Clamp(s.EndLine, from, last);

                // the declaration narrows the search; the fingerprint pinpoints
                // the line inside it, so edits within the method are handled too
                if (a.Context is not null)
                {
                    int found = FindContext(lines, a.Context, from, to, s.StartLine + a.Offset);
                    if (found >= 0) return new Anchor(found, AnchorKind.Symbol);
                }
                else
                {
                    return new Anchor(Math.Clamp(s.StartLine + a.Offset, from, to), AnchorKind.Symbol);
                }

                // right declaration, but those lines are gone
                return new Anchor(Math.Clamp(s.StartLine + a.Offset, from, to), AnchorKind.Drifted);
            }
        }

        if (a.Context is not null)
        {
            int found = FindContext(lines, a.Context, 0, last, a.Line);
            if (found >= 0) return new Anchor(found, AnchorKind.Context);
        }

        if (a.Line >= 0 && a.Line <= last &&
            a.Context is not null && ContextOf(lines, a.Line) == a.Context)
            return new Anchor(a.Line, AnchorKind.Line);

        return new Anchor(Math.Clamp(a.Line, 0, last), AnchorKind.Orphan);
    }

    /// <summary>nearest line to <paramref name="near"/> whose fingerprint matches.</summary>
    static int FindContext(string[] lines, string context, int from, int to, int near)
    {
        int best = -1, bestDistance = int.MaxValue;
        for (int i = from; i <= to; i++)
        {
            if (ContextOf(lines, i) != context) continue;
            int distance = Math.Abs(i - near);
            if (distance < bestDistance) { best = i; bestDistance = distance; }
        }
        return best;
    }
}

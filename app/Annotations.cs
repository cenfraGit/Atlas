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
    /// reindenting does not break it.
    ///
    /// One short hash per line rather than one hash of the lot, so a match
    /// can be partial. A single hash failed the moment anything went in
    /// right next to the line - which is exactly where people edit - and
    /// the anchor fell back to a line count from the top of the method.
    /// Now the line itself has to match and its neighbours break ties.</summary>
    public static string ContextOf(string[] lines, int line)
    {
        var parts = new System.Text.StringBuilder();
        for (int i = line - ContextRadius; i <= line + ContextRadius; i++)
            parts.Append(Hash(i >= 0 && i < lines.Length ? Normalize(lines[i]) : "")[..LineHash]);
        return parts.ToString();
    }

    const int LineHash = 4;

    /// <summary>how well line <paramref name="i"/> fits a fingerprint: -1 when
    /// the line itself is different, otherwise how many of its neighbours
    /// still agree.
    ///
    /// A twelve character fingerprint is the single hash written before
    /// there was one per line. It still matches, whole or not at all, and is
    /// replaced with the new kind whenever the thing holding it re-anchors.</summary>
    public static int Fit(string[] lines, string context, int i)
    {
        int full = ContextRadius * 2;
        if (context.Length != (full + 1) * LineHash)
        {
            var parts = new List<string>();
            for (int j = i - ContextRadius; j <= i + ContextRadius; j++)
                parts.Add(j >= 0 && j < lines.Length ? Normalize(lines[j]) : "");
            return Hash(string.Join("", parts)) == context ? full : -1;
        }

        var mine = ContextOf(lines, i);
        if (string.CompareOrdinal(mine, ContextRadius * LineHash, context, ContextRadius * LineHash, LineHash) != 0)
            return -1;
        int agree = 0;
        for (int k = 0; k <= full; k++)
            if (k != ContextRadius && string.CompareOrdinal(mine, k * LineHash, context, k * LineHash, LineHash) == 0)
                agree++;
        return agree;
    }

    /// <summary>whether a stored fingerprint is the old single-hash kind.</summary>
    public static bool IsOldContext(string? context) =>
        context is not null && context.Length != (ContextRadius * 2 + 1) * LineHash;

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
            Id = BoardStore.NewId(),
            Text = text,
            File = relPath,
            Line = line,
            Span = Math.Max(1, Math.Abs(to - from) + 1),
            Context = ContextOf(lines, line),
            Key = FileKeys.Of(lines),
        };
        var (symbol, offset) = CaptureAt(fullPath, line);
        a.Symbol = symbol;
        a.Offset = offset;
        return a;
    }

    /// <summary>the declaration a line sits in, and how far down it is.
    ///
    /// Split out so a board's file window can hold the same anchor an
    /// annotation does. A window records a range of line *numbers*, and a
    /// line number stops meaning the same thing the moment somebody inserts
    /// above it - which is the whole reason this ladder exists.</summary>
    /// <summary>the lines a named declaration covers now, or null when it
    /// is gone. The name carries its arity, as Symbols writes it.</summary>
    public static (int Start, int End)? SpanOf(string fullPath, string symbol)
    {
        foreach (var s in Symbols.ForFile(fullPath))
            if (s.Name == symbol) return (s.StartLine, s.EndLine);
        return null;
    }

    /// <summary>an anchor for the *last* line of something: the declaration
    /// whose end it should follow, and how far past that end it is.
    ///
    /// Measured from an end rather than a start, so lines added inside the
    /// method push it down with the closing brace. The declaration is the
    /// innermost one the line sits in - unless the line is in a gap between
    /// members (a blank line, a class's closing brace), where it follows the
    /// member just above it. A box round two methods then grows with the
    /// second, which measuring from the first one's end did not.</summary>
    public static (string? Symbol, int Offset) CaptureEnd(string fullPath, int line)
    {
        var all = Symbols.ForFile(fullPath);
        if (Symbols.Innermost(all, line) is not { } inside) return (null, 0);

        SymbolSpan? above = null;
        foreach (var s in all)
        {
            if (s.StartLine < inside.StartLine || s.EndLine >= line || s.EndLine > inside.EndLine) continue;
            if (s.Name == inside.Name) continue;
            if (above is null || s.EndLine > above.Value.EndLine ||
                s.EndLine == above.Value.EndLine && s.Lines > above.Value.Lines)
                above = s;
        }
        var from = above ?? inside;
        return (from.Name, line - from.EndLine);
    }

    /// <summary>where an end anchor lands now, or null when its declaration
    /// is gone.</summary>
    public static int? ResolveEnd(string fullPath, string symbol, int offset) =>
        SpanOf(fullPath, symbol) is { } span ? span.End + offset : null;

    public static (string? Symbol, int Offset) CaptureAt(string fullPath, int line)
    {
        var sym = Symbols.Innermost(Symbols.ForFile(fullPath), line);
        return sym is { } s ? (s.Name, line - s.StartLine) : (null, 0);
    }

    /// <summary>find where an annotation belongs now. symbol first, then the
    /// context fingerprint, then the stored line only if it still matches.</summary>
    public static Anchor Resolve(Annotation a, string fullPath, string[] lines) =>
        Resolve(a.Symbol, a.Offset, a.Context, a.Line, fullPath, lines);

    /// <summary>the same ladder, for anything that stored an anchor - an
    /// annotation, or a board's window onto a range of lines. One ladder
    /// rather than two, because a second copy is a second set of rules
    /// about when a line has moved.</summary>
    public static Anchor Resolve(
        string? symbol, int offset, string? context, int storedLine, string fullPath, string[] lines)
    {
        var a = new Annotation { Symbol = symbol, Offset = offset, Context = context, Line = storedLine };
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
                // inside the declaration the line's own text is enough: the
                // search is already narrowed to one method
                if (a.Context is not null)
                {
                    int found = FindContext(lines, a.Context, from, to, s.StartLine + a.Offset, 0);
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

        // across a whole file a line like "}" is everywhere, so half its
        // neighbours have to agree as well
        if (a.Context is not null)
        {
            int found = FindContext(lines, a.Context, 0, last, a.Line, ContextRadius);
            if (found >= 0) return new Anchor(found, AnchorKind.Context);
        }

        if (a.Line >= 0 && a.Line <= last &&
            a.Context is not null && Fit(lines, a.Context, a.Line) == ContextRadius * 2)
            return new Anchor(a.Line, AnchorKind.Line);

        return new Anchor(Math.Clamp(a.Line, 0, last), AnchorKind.Orphan);
    }

    /// <summary>the line that fits the fingerprint best, with at least
    /// <paramref name="least"/> neighbours agreeing; the nearest to
    /// <paramref name="near"/> among equals.</summary>
    static int FindContext(string[] lines, string context, int from, int to, int near, int least)
    {
        int best = -1, bestFit = -1, bestDistance = int.MaxValue;
        for (int i = from; i <= to; i++)
        {
            int fit = Fit(lines, context, i);
            if (fit < least) continue;
            int distance = Math.Abs(i - near);
            if (fit > bestFit || fit == bestFit && distance < bestDistance)
            {
                best = i; bestFit = fit; bestDistance = distance;
            }
        }
        return best;
    }
}

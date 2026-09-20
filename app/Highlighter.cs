using SkiaSharp;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace Atlas;

/// <summary>one coloured run of characters on a line. columns are char indices,
/// which is enough because the code tier is drawn in a monospace font.</summary>
public readonly record struct Run(int Start, int End, SKColor Color);

/// <summary>textmate grammars give us every language vscode supports for free.
/// tokenising is stateful per line, so a file is always done whole, in the
/// background, at the same time its text is read.</summary>
public sealed class Highlighter
{
    const int MaxLines = 50_000;

    readonly RegistryOptions _options;
    readonly Registry _registry;
    readonly Theme _theme;
    readonly Dictionary<string, IGrammar?> _grammars = [];
    readonly Dictionary<int, SKColor> _colors = [];
    readonly SKColor _fallback;
    readonly object _gate = new();

    public Highlighter(SKColor fallback)
    {
        _fallback = fallback;
        _options = new RegistryOptions(ThemeName.DarkPlus);
        _registry = new Registry(_options);
        _theme = _registry.GetTheme();
    }

    IGrammar? GrammarFor(string ext)
    {
        lock (_gate)
        {
            if (_grammars.TryGetValue(ext, out var g)) return g;
            try
            {
                var scope = _options.GetScopeByExtension(ext);
                g = string.IsNullOrEmpty(scope) ? null : _registry.LoadGrammar(scope);
            }
            catch { g = null; }
            _grammars[ext] = g;
            return g;
        }
    }

    SKColor ColorOf(int id)
    {
        lock (_gate)
        {
            if (_colors.TryGetValue(id, out var c)) return c;
            c = _fallback;
            var hex = _theme.GetColor(id);
            if (!string.IsNullOrEmpty(hex) && SKColor.TryParse(hex, out var parsed)) c = parsed;
            _colors[id] = c;
            return c;
        }
    }

    /// <summary>returns one run list per line, or null when the language is unknown.</summary>
    public Run[][]? Tokenize(string[] lines, string ext)
    {
        // ponytail: textmate's registry and its grammars are not thread safe -
        // tokenising two files at once corrupts the shared rule registry into
        // infinite recursion. one global lock; give each worker its own
        // Registry if tokenising ever shows up as a bottleneck.
        lock (_gate)
        {
            return TokenizeCore(lines, ext);
        }
    }

    Run[][]? TokenizeCore(string[] lines, string ext)
    {
        var grammar = GrammarFor(ext);
        if (grammar is null || lines.Length > MaxLines) return null;

        var result = new Run[lines.Length][];
        IStateStack? stack = null;
        var runs = new List<Run>(32);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var tokenized = grammar.TokenizeLine(line, stack, TimeSpan.FromMilliseconds(200));
            stack = tokenized.RuleStack;
            runs.Clear();

            foreach (var t in tokenized.Tokens)
            {
                int start = Math.Min(t.StartIndex, line.Length);
                int end = Math.Min(t.EndIndex, line.Length);
                if (end <= start) continue;

                var color = _fallback;
                foreach (var rule in _theme.Match(t.Scopes))
                {
                    if (rule.foreground <= 0) continue;
                    color = ColorOf(rule.foreground);
                    break;
                }
                // merge with the previous run when the colour did not change
                if (runs.Count > 0 && runs[^1].End == start && runs[^1].Color == color)
                    runs[^1] = runs[^1] with { End = end };
                else
                    runs.Add(new Run(start, end, color));
            }
            result[i] = runs.ToArray();
        }
        return result;
    }
}

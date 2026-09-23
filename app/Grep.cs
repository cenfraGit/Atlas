namespace Atlas;

/// <summary>one line of one file that contains what was searched for.</summary>
/// <param name="File">index into <see cref="Scan.Files"/>.</param>
/// <param name="Line">0-based.</param>
/// <param name="Col">0-based column of the first match on that line.</param>
/// <param name="Window">on a board, the id of the window showing the line.</param>
/// <param name="Copy">which of several windows onto the same file that is,
/// 1-based, or 0 when the file has only one.</param>
public readonly record struct Found(int File, string Path, int Line, int Col, string Text,
    string? Window = null, int Copy = 0);

/// <summary>searching what the files say, rather than what they are called.
///
/// <see cref="Search"/> matches paths, which answers "where is the panel
/// file". This answers "where is this written", which on a map of a codebase
/// is the question you have once you are looking at it.
///
/// The lines come from a provider rather than from disk, so this is the same
/// code whether it is reading the working copy, a commit's tree, or a couple
/// of arrays in a test. It knows nothing about threads: the caller decides
/// where to run it, and passes a token so a search that has been superseded
/// can stop.</summary>
public static class Grep
{
    /// <summary>enough to find what you are after, few enough that the list
    /// is still a list. A word like "the" matches thousands, and none of
    /// them after the first page tells you anything.</summary>
    public const int Limit = 200;

    /// <summary>a preview longer than this is not a preview. Minified files
    /// are one line of forty thousand characters.</summary>
    const int MaxPreview = 400;

    public static List<Found> Run(
        Scan scan,
        string query,
        Func<string, string[]> lines,
        ISet<string>? only = null,
        int limit = Limit,
        CancellationToken cancel = default)
    {
        var found = new List<Found>();
        if (string.IsNullOrEmpty(query)) return found;

        for (int i = 0; i < scan.Files.Count && found.Count < limit; i++)
        {
            if (cancel.IsCancellationRequested) return found;

            var f = scan.Files[i];
            if (only is not null && !only.Contains(f.P)) continue;

            var src = lines(f.P);
            for (int n = 0; n < src.Length && found.Count < limit; n++)
            {
                int at = src[n].IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (at < 0) continue;
                found.Add(new Found(i, f.P, n, at, Preview(src[n])));
            }
        }
        return found;
    }

    /// <summary>how many files matched, for the count in the hint line. The
    /// list is capped, so "12 of 40 files" says more than "200 lines".</summary>
    public static int FilesIn(List<Found> found)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in found) seen.Add(f.Path);
        return seen.Count;
    }

    /// <summary>trimmed of leading indentation, because a list of matches
    /// twelve spaces deep is a list of blanks, and clipped at both ends.</summary>
    static string Preview(string line)
    {
        var s = line.TrimStart();
        return s.Length > MaxPreview ? s[..MaxPreview] : s;
    }
}

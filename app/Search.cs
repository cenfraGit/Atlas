namespace Atlas;

public readonly record struct Hit(int Index, int Score, string Path);

/// <summary>subsequence matching over file paths, weighted so that matches in
/// the file name beat matches in the directory part.</summary>
public static class Search
{
    public static List<Hit> Run(Scan scan, string query, int limit = 25)
    {
        var hits = new List<Hit>();
        if (string.IsNullOrWhiteSpace(query)) return hits;
        var needle = query.Trim();

        var files = scan.Files;
        for (int i = 0; i < files.Count; i++)
        {
            int score = Score(files[i].P, needle);
            if (score > int.MinValue) hits.Add(new Hit(i, score, files[i].P));
        }
        hits.Sort((a, b) => b.Score != a.Score
            ? b.Score.CompareTo(a.Score)
            : a.Path.Length.CompareTo(b.Path.Length));
        if (hits.Count > limit) hits.RemoveRange(limit, hits.Count - limit);
        return hits;
    }

    static int Score(string path, string needle)
    {
        int nameStart = path.LastIndexOf('/') + 1;
        int score = 0, at = 0, lastMatch = -2, run = 0;

        foreach (var ch in needle)
        {
            if (ch == ' ') continue;
            int found = IndexOfIgnoreCase(path, ch, at);
            if (found < 0) return int.MinValue;

            score += found >= nameStart ? 12 : 3;                 // file name beats folders
            if (found == lastMatch + 1) score += 8 + (++run) * 2;  // contiguous runs
            else run = 0;
            if (found == 0 || found == nameStart) score += 10;     // start of path or name
            else if (IsBoundary(path, found)) score += 6;          // after a separator or a capital
            score -= Math.Min(found - at, 6);                      // distance skipped

            lastMatch = found;
            at = found + 1;
        }
        // an exact file-name prefix should always win
        if (path.AsSpan(nameStart).StartsWith(needle, StringComparison.OrdinalIgnoreCase)) score += 40;
        return score;
    }

    static bool IsBoundary(string s, int i)
    {
        char prev = s[i - 1];
        if (prev is '/' or '.' or '_' or '-') return true;
        return char.IsLower(prev) && char.IsUpper(s[i]);
    }

    static int IndexOfIgnoreCase(string s, char c, int from)
    {
        for (int i = from; i < s.Length; i++)
            if (char.ToLowerInvariant(s[i]) == char.ToLowerInvariant(c)) return i;
        return -1;
    }
}

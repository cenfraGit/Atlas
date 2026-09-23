namespace Atlas;

/// <summary>a changed file's review text with what the change removed put
/// back where it came out, and the map from the file's own lines to the rows
/// that text has.
///
/// Review mode already draws a temporary scan of the commit's tree, thrown
/// away when review ends. Putting the removed lines into that tree's text
/// means the map's cards really contain them - taller by exactly as much -
/// so nothing that draws a card has to know about rows that are not lines.
/// Only the line numbers need translating: the change set's (new-file lines
/// to rows) and the gutter's (rows to the number a reader expects).</summary>
public sealed class Splice
{
    /// <summary>row a line of the new file landed on. One longer than the
    /// file, so a position past the last line - a removal off the end - has
    /// a row too.</summary>
    public required int[] RowOf { get; init; }

    /// <summary>the number a row shows in the gutter: a new-file line's own
    /// number, or on a removed row the number it had in the old file.</summary>
    public required int[] Numbers { get; init; }

    /// <summary>rows that are removed lines, in order.</summary>
    public required List<int> RemovedRows { get; init; }

    /// <summary>put a file's removed blocks back into its lines.</summary>
    public static (string[] Lines, Splice Map) Of(string[] lines, IEnumerable<RemovedBlock> blocks)
    {
        var text = new List<string>(lines.Length);
        var rowOf = new int[lines.Length + 1];
        var numbers = new List<int>(lines.Length);
        var removed = new List<int>();

        var byAt = blocks.GroupBy(b => Math.Clamp(b.At, 0, lines.Length))
                         .ToDictionary(g => g.Key, g => g.ToList());
        for (int n = 0; n <= lines.Length; n++)
        {
            // what came out here goes above the line now in its place, the
            // way a diff shows it
            if (byAt.TryGetValue(n, out var here))
                foreach (var b in here)
                    for (int j = 0; j < b.Lines.Count; j++)
                    {
                        removed.Add(text.Count);
                        numbers.Add(b.OldLine + j + 1);
                        text.Add(b.Lines[j]);
                    }
            rowOf[n] = text.Count;
            if (n < lines.Length)
            {
                numbers.Add(n + 1);
                text.Add(lines[n]);
            }
        }
        return ([.. text], new Splice { RowOf = rowOf, Numbers = [.. numbers], RemovedRows = removed });
    }

    /// <summary>the snapshot with every changed file's removed lines put
    /// back, and the maps that say where things went. A file the snapshot
    /// does not have - one the change deleted - is left out: there is no
    /// card to put its lines in.</summary>
    public static (Dictionary<string, string[]> Text, Dictionary<string, Splice> Maps) All(
        Dictionary<string, string[]> snapshot, ChangeSet whole)
    {
        var text = new Dictionary<string, string[]>(snapshot, StringComparer.Ordinal);
        var maps = new Dictionary<string, Splice>(StringComparer.Ordinal);
        foreach (var change in whole.Files)
        {
            if (change.RemovedText.Count == 0 || !snapshot.TryGetValue(change.Path, out var lines)) continue;
            var (spliced, map) = Of(lines, change.RemovedText);
            text[change.Path] = spliced;
            maps[change.Path] = map;
        }
        return (text, maps);
    }

    /// <summary>a change set in the rows the spliced text has.
    ///
    /// For the whole change the removal positions go: its removed lines are
    /// rows now, drawn red, and a marker as well would say it twice. For one
    /// commit they stay, as markers - that commit's removals are not the
    /// ones spliced in, which were the whole change's.</summary>
    public static ChangeSet Remap(ChangeSet set, IReadOnlyDictionary<string, Splice> maps, bool whole)
    {
        if (maps.Count == 0) return set;
        var mapped = new ChangeSet { Label = set.Label };
        foreach (var f in set.Files)
        {
            if (!maps.TryGetValue(f.Path, out var map)) { mapped.Files.Add(f); continue; }
            var c = new FileChange(f.Path, f.Added, f.Removed);
            int last = map.RowOf.Length - 1;
            c.AddedLines.AddRange(f.AddedLines.Select(n => map.RowOf[Math.Clamp(n, 0, last)]));
            if (!whole) c.RemovedAt.AddRange(f.RemovedAt.Select(n => map.RowOf[Math.Clamp(n, 0, last)]));
            c.RemovedText.AddRange(f.RemovedText);
            mapped.Files.Add(c);
        }
        mapped.Index();
        return mapped;
    }
}

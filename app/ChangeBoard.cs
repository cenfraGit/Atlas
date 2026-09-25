namespace Atlas;

/// <summary>gathers what one commit touched onto a single board.
///
/// The map answers "where in the repo did this land", which is the question
/// worth asking first. It does not answer "what does the change actually
/// say" - for that the changed code has to be side by side, and on a large
/// repo the changed files are nowhere near each other.
///
/// The board this builds is generated, never stored: it is not in the boards
/// panel, it is not written to .atlas/, and it is read only. Leaving it
/// throws it away.
///
/// One window per file, whole. It used to be one window per hunk, which is
/// what a diff tool shows and is wrong here: the windows are real code with
/// real syntax colouring, and cutting them to three lines of context throws
/// away the thing this view has that a diff does not - the rest of the file
/// the change landed in. The changed lines glow, so finding them inside a
/// whole file is no harder than finding them on the map.
///
/// What a change removed is in the windows too, where it was, because the
/// review's text has it put back (<see cref="Splice"/>) - the same text the
/// map's cards are drawn from. A file the change deleted has no card and no
/// window, so it is shown as a red block of its old text.</summary>
public static class ChangeBoard
{
    /// <summary>a run of lines worth showing, in zero-based file lines.</summary>
    public readonly record struct Hunk(int From, int To)
    {
        public int Lines => To - From + 1;
    }

    const int Context = 3;      // lines either side, so a change has a setting
    const int Merge = 10;       // closer than this and two hunks read as one
    const float WindowW = 620;
    const float Gap = 56;
    const int MaxWindows = 40;  // a rewrite-the-world commit is not a board

    /// <summary>the parts of a file a change actually touched, merged when
    /// they are close enough that two windows would just be one window with a
    /// gap in it.</summary>
    public static List<Hunk> HunksOf(FileChange change, int fileLines,
        int context = Context, int merge = Merge)
    {
        if (fileLines <= 0) return [];

        // a deletion has no line in the new file to point at, but the place it
        // was taken from is exactly what a reviewer wants to see
        var marks = new List<int>(change.AddedLines.Count + change.RemovedAt.Count);
        marks.AddRange(change.AddedLines);
        marks.AddRange(change.RemovedAt);
        if (marks.Count == 0) return [];

        marks.Sort();

        var hunks = new List<Hunk>();
        int from = marks[0], to = marks[0];
        foreach (var m in marks)
        {
            if (m - to <= merge) { to = Math.Max(to, m); continue; }
            hunks.Add(new Hunk(from, to));
            from = to = m;
        }
        hunks.Add(new Hunk(from, to));

        // pad for context last, so padding cannot merge two hunks behind our
        // back and produce overlapping windows
        int last = fileLines - 1;
        var padded = new List<Hunk>(hunks.Count);
        foreach (var h in hunks)
        {
            var next = new Hunk(Math.Clamp(h.From - context, 0, last), Math.Clamp(h.To + context, 0, last));
            if (padded.Count > 0 && next.From <= padded[^1].To + 1)
                padded[^1] = padded[^1] with { To = Math.Max(padded[^1].To, next.To) };
            else
                padded.Add(next);
        }
        return padded;
    }

    /// <summary>build the board. Files come out biggest churn first, so the
    /// heart of the change is the first thing read. With
    /// <paramref name="removed"/> off, a deleted file is left out, the way
    /// removed lines are left out of the text.</summary>
    public static Board Build(ChangeSet set, Scene scene, string name, bool removed = true)
    {
        var board = new Board { Id = "changes", Name = name };

        var windows = new List<(BoardItem Item, float H)>();
        foreach (var change in set.Files)
        {
            int i = scene.IndexOfPath(change.Path);
            if (i >= 0)
            {
                var item = new BoardItem
                {
                    Id = BoardStore.NewId(), Kind = "file", File = change.Path,
                    Line = 0, EndLine = Math.Max(0, scene.Data.Files[i].N - 1), W = WindowW,
                };
                windows.Add((item, scene.ItemHeight(item)));
            }
            // not in the scan because the change deleted it: the whole old
            // file, as one block. One off the map because it is hidden is
            // left out, as it is on the map
            else if (removed && Deleted(change, scene) is { } block) windows.Add(block);
            if (windows.Count >= MaxWindows) break;
        }

        Pack(windows, board);
        return board;
    }

    /// <summary>a file the change deleted, which the scan cannot know about.
    /// It gets a header, since there is no window above it to say what it is.</summary>
    static (BoardItem Item, float H)? Deleted(FileChange change, Scene scene)
    {
        var lines = change.RemovedText.SelectMany(b => b.Lines).ToList();
        if (!change.Deleted || lines.Count == 0) return null;
        float cardW = scene.Data.Files.Count > 0 ? scene.Data.Files[0].W : 240;
        float h = Scene.WinHeadH + lines.Count * scene.Data.LineH * (WindowW / cardW);
        return (new BoardItem
        {
            Id = BoardStore.NewId(), Kind = "removed", File = change.Path,
            Line = 0, Text = string.Join("\n", lines), W = WindowW, H = h,
        }, h);
    }

    /// <summary>where in board coordinates the first changed line of the
    /// first window is, so the view can open looking at a change.
    ///
    /// This is what whole-file windows cost and why the hunks are still
    /// worth working out: land at the top of the first window and you are
    /// looking at line one of a three thousand line file, with the thing
    /// you came to see somewhere off the bottom of the screen.</summary>
    public static (float X, float Y)? FirstChange(Board board, ChangeSet set, Scene scene)
    {
        foreach (var item in board.Items)
        {
            if (item.Kind != "file" || item.File is null || !set.ByPath.TryGetValue(item.File, out var change)) continue;
            int i = scene.IndexOfPath(item.File);
            if (i < 0) continue;

            var file = scene.Data.Files[i];
            // the whole change's removals are rows in the text now rather than
            // positions in the change, and are as much a change to land on
            if (scene.Splices.TryGetValue(item.File, out var spliced) && spliced.RemovedRows.Count > 0)
            {
                var both = new FileChange(change.Path, change.Added, change.Removed);
                both.AddedLines.AddRange(change.AddedLines);
                both.AddedLines.AddRange(spliced.RemovedRows);
                both.RemovedAt.AddRange(change.RemovedAt);
                change = both;
            }
            var hunks = HunksOf(change, file.N);
            if (hunks.Count == 0) continue;

            // the file may be cut into several windows; the one to look in is
            // the one holding the first change
            int first = hunks[0].From;
            var window = board.Items.FirstOrDefault(w => w.Kind == "file" && w.File == item.File &&
                                                         w.Line <= first && first <= w.EndLine) ?? item;

            // windows are scaled to a fixed width, so a file's own line
            // height is not the height of a line on the board
            float k = window.W / file.W;
            float y = window.Y + Scene.WinHeadH + (first - window.Line) * scene.Data.LineH * k;
            return (window.X + window.W / 2, y);
        }
        return null;
    }

    /// <summary>columns, each window going to whichever is shortest. The same
    /// shape the scanner uses for a folder, and for the same reason: it
    /// keeps a tall thing from pushing everything beside it down.</summary>
    static void Pack(List<(BoardItem Item, float H)> windows, Board board)
    {
        if (windows.Count == 0) return;

        int cols = Math.Clamp((int)Math.Ceiling(Math.Sqrt(windows.Count)), 1, 5);
        var colH = new float[cols];

        foreach (var (item, h) in windows)
        {
            int c = 0;
            for (int i = 1; i < cols; i++) if (colH[i] < colH[c]) c = i;
            item.X = c * (WindowW + Gap);
            item.Y = colH[c];
            colH[c] += h + Gap;
            board.Items.Add(item);
        }
    }
}

namespace Atlas;

/// <summary>run: dotnet run -- --boardtest &lt;repo&gt;</summary>
public static class BoardTest
{
    static int _fails;

    static void Check(bool ok, string what)
    {
        if (!ok) { _fails++; Console.WriteLine("  FAIL: " + what); }
    }

    public static void Run(string[] args)
    {
        var repo = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--") && Directory.Exists(a));
        if (repo is null) { Console.WriteLine("usage: --boardtest <repo>"); return; }

        var scan = Scanner.Build(repo);
        var scene = new Scene(scan);
        // a middling file with real content in it: half the files in a repo
        // are a few lines long, and a five line file proves nothing
        var candidates = scan.Files.Where(f => f.N > 150).ToList();
        var target = candidates.Count > 0 ? candidates[candidates.Count / 2] : scan.Files[scan.Files.Count / 2];
        Console.WriteLine($"{scan.Files.Count} files from {repo}");

        var dir = Path.Combine(Path.GetTempPath(), "cv_boardtest_" + Guid.NewGuid().ToString("n")[..6]);
        Directory.CreateDirectory(dir);
        try
        {
            var store = BoardStore.Load(dir);
            Check(store.Boards.Count == 0, "a repo with no boards loads empty");

            var board = store.Create("Startup sequence");
            Check(File.Exists(board.Path), "creating a board writes its file");
            Check(Path.GetFileName(board.Path).StartsWith("startup-sequence-"),
                $"the file name is readable, got {Path.GetFileName(board.Path)}");

            board.Items.Add(new BoardItem
            {
                Id = "i1", Kind = "file", File = target.P, Line = 5, EndLine = 24, X = 0, Y = 0, W = 620,
            });
            board.Items.Add(new BoardItem
            {
                Id = "i2", Kind = "note", Text = "the host is already built by this point", X = 700, Y = 0, W = 380,
            });
            store.Save(board);

            var reread = BoardStore.Load(dir);
            Check(reread.Boards.Count == 1 && reread.Boards[0].Items.Count == 2, "board round trips");
            Check(reread.Boards[0].Items[0].EndLine == 24, "the line range survives");

            // the reverse index is what drives the hover popup on the map
            Check(reread.Containing(target.P).Count == 1, "finds the board holding a file");
            Check(reread.Containing("nope/missing.cs").Count == 0, "reports no boards for an unused file");

            // a window shows only its range, so its height follows the range
            var fileItem = reread.Boards[0].Items[0];
            float h20 = scene.ItemHeight(fileItem);
            fileItem.EndLine = 104;
            float h100 = scene.ItemHeight(fileItem);
            Check(h100 > h20 * 3, $"a 100 line window is far taller than a 20 line one ({h100} vs {h20})");

            // a range past the end of the file must clamp, not throw
            fileItem.Line = 0;
            fileItem.EndLine = 999_999;
            var (from, to) = scene.RangeOf(fileItem, target);
            Check(to == target.N - 1, $"a range past the end clamps to the file, got {to} of {target.N}");
            Check(from <= to, "the clamped range stays ordered");

            var missing = new BoardItem { Kind = "file", File = "gone/away.cs", W = 620 };
            Check(scene.ItemHeight(missing) > 0, "a missing file still has a height to draw");

            // renaming moves the file on disk, it does not leave the old one behind
            var oldPath = reread.Boards[0].Path;
            reread.Rename(reread.Boards[0], "How drawing works");
            Check(!File.Exists(oldPath), "renaming removes the old file");
            Check(File.Exists(reread.Boards[0].Path), "renaming writes the new file");
            Check(BoardStore.Load(dir).Boards.Count == 1, "only one board remains after a rename");

            reread.Delete(reread.Boards[0]);
            Check(BoardStore.Load(dir).Boards.Count == 0, "deleting removes the board file");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        Console.WriteLine(_fails == 0 ? "PASS" : $"{_fails} FAILURES");
    }
}

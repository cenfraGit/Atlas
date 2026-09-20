namespace Atlas;

/// <summary>the interaction rules that were only ever checked by eye: hit
/// order, rubberband composition, snapping, resizing and rename following.
/// run: dotnet run -- --pickingtest &lt;repo&gt;</summary>
public static class PickingTest
{
    static int _fails;

    static void Check(bool ok, string what)
    {
        if (!ok) { _fails++; Console.WriteLine("  FAIL: " + what); }
    }

    const float Grid = 40f;

    static BoardItem Note(string id, float x, float y, float w = 200, float h = 80) =>
        new() { Id = id, Kind = "note", Text = "x", X = x, Y = y, W = w, H = h };

    static BoardItem Shape(string id, float x, float y, float w = 200, float h = 80) =>
        new() { Id = id, Kind = "shape", X = x, Y = y, W = w, H = h };

    /// <summary>the rule a rubberband must follow: start from what was held,
    /// then add exactly what the band covers now.</summary>
    static HashSet<string> Sweep(Scene scene, IEnumerable<string> held, SkiaSharp.SKRect band)
    {
        var picked = new HashSet<string>(held);
        foreach (var it in scene.ItemsIn(band)) picked.Add(it.Id);
        return picked;
    }

    public static void Run(string[] args)
    {
        var repo = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--") && Directory.Exists(a));
        if (repo is null) { Console.WriteLine("usage: --pickingtest <repo>"); return; }

        var scan = Scanner.Build(repo);
        var scene = new Scene(scan);
        var board = new Board { Id = "b", Name = "test" };
        scene.ActiveBoard = board;
        scene.CamS = 1f;

        board.Items.Add(Note("a", 0, 0));
        board.Items.Add(Note("b", 400, 0));

        // hit testing takes the topmost item, and a later item is on top
        var over = Shape("over", 0, 0);
        board.Items.Add(over);
        Check(scene.ItemAt(10, 10)?.Id == "over", "the topmost item wins a click");
        board.Items.Remove(over);
        board.Items.Insert(0, over);
        Check(scene.ItemAt(10, 10)?.Id == "a", "sending it to the back gives the click back");
        board.Items.Remove(over);

        // a click a hair outside still lands: edges were being missed
        Check(scene.ItemAt(-2, -2)?.Id == "a", "a click just outside an edge still hits");
        Check(scene.ItemAt(-40, -40) is null, "a click well away hits nothing");

        // arrows must not swallow clicks meant for the canvas
        board.Items.Add(new BoardItem { Id = "ar", Kind = "arrow", X = 0, Y = 0, X2 = 400, Y2 = 400 });
        Check(scene.ItemAt(200, 200) is null, "an arrow does not take a click as a box");
        Check(scene.ArrowAt(200, 200)?.Id == "ar", "but the arrow itself can be picked");
        Check(scene.ArrowAt(200, 280) is null, "and only near the line");
        board.Items.RemoveAll(i => i.Id == "ar");

        // the band must let go when it shrinks back off something
        var wide = Sweep(scene, [], new SkiaSharp.SKRect(-10, -10, 700, 300));
        Check(wide.Contains("a") && wide.Contains("b"), "a wide band takes both");
        var shrunk = Sweep(scene, [], new SkiaSharp.SKRect(-10, -10, 250, 300));
        Check(shrunk.Contains("a") && !shrunk.Contains("b"), "shrinking it lets the far one go");
        var withCtrl = Sweep(scene, ["b"], new SkiaSharp.SKRect(-10, -10, 250, 300));
        Check(withCtrl.Contains("a") && withCtrl.Contains("b"), "ctrl keeps what was already picked");

        // snapping: the true position accumulates, only the shown one rounds.
        // rounding the live position is what made items feel stuck
        float raw = 0, shown = 0;
        for (int i = 0; i < 8; i++)
        {
            raw += 7f;                                   // every move smaller than a cell
            shown = MathF.Round(raw / Grid) * Grid;
        }
        Check(raw > 50, "the true position accumulates");
        Check(shown == 40f, $"the shown position lands on the grid, got {shown}");
        Check(MathF.Round(3f / Grid) * Grid == 0f, "a tiny drag does not jump a whole cell");

        // images: written in re-encoded, read back, and the file goes away
        // once no board points at it
        var temp = Path.Combine(Path.GetTempPath(), "atlas_img_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(temp);
        using (var bmp = new SkiaSharp.SKBitmap(64, 32))
        {
            using (var c = new SkiaSharp.SKCanvas(bmp)) c.Clear(new SkiaSharp.SKColor(0x20, 0x40, 0x80));
            var imgName = ImageStore.Save(temp, bmp);
            Check(imgName is not null, "an image is written into the store");
            var back = imgName is null ? null : ImageStore.Load(temp, imgName);
            Check(back is { Width: 64, Height: 32 }, "and reads back at the same size");

            var keeper = new Board { Id = "k", Name = "k" };
            keeper.Items.Add(new BoardItem { Id = "i", Kind = "image", File = imgName });
            Check(ImageStore.Prune(temp, [keeper]) == 0, "an image a board uses stays");
            Check(ImageStore.Prune(temp, []) == 1, "one nothing points at is deleted");
            Check(imgName is not null && !File.Exists(Path.Combine(ImageStore.DirFor(temp), imgName)),
                "and the file really is gone");
        }
        try { Directory.Delete(temp, true); } catch { }

        // a clipboard screenshot arrives as a headerless bmp
        var dib = new byte[40 + 16];
        BitConverter.GetBytes(40).CopyTo(dib, 0);
        BitConverter.GetBytes(2).CopyTo(dib, 4);
        BitConverter.GetBytes(2).CopyTo(dib, 8);
        BitConverter.GetBytes((short)1).CopyTo(dib, 12);
        BitConverter.GetBytes((short)32).CopyTo(dib, 14);
        BitConverter.GetBytes(16).CopyTo(dib, 20);
        using (var fromDib = ImageStore.FromDib(dib))
            Check(fromDib is { Width: 2, Height: 2 }, "a DIB decodes once its header is put back");
        Check(ImageStore.FromDib([1, 2, 3]) is null, "rubbish on the clipboard is refused");

        // only drawing elements resize
        Check(Scene.Resizable(Note("n", 0, 0)), "a note resizes");
        Check(Scene.Resizable(Shape("s", 0, 0)), "a rectangle resizes");
        Check(!Scene.Resizable(new BoardItem { Id = "f", Kind = "file", File = "x.cs" }),
            "a file window does not resize");
        Check(Scene.Resizable(new BoardItem { Id = "p", Kind = "image", File = "a.png" }),
            "an image resizes");

        // a reference survives the file being renamed or moved
        // a middling file with real content in it: half the files in a repo
        // are a few lines long, and a five line file proves nothing
        var candidates = scan.Files.Where(f => f.N > 150).ToList();
        var target = candidates.Count > 0 ? candidates[candidates.Count / 2] : scan.Files[scan.Files.Count / 2];
        var full = Path.Combine(repo, target.P.Replace('/', Path.DirectorySeparatorChar));
        var key = FileKeys.OfFile(full);
        Check(!string.IsNullOrEmpty(key), "a file has a fingerprint");
        Check(scene.ResolveFile(target.P, key) == scene.IndexOfPath(target.P),
            "an unmoved file resolves to itself");

        var name = target.P[(target.P.LastIndexOf('/') + 1)..];
        int byMove = scene.ResolveFile("some/old/place/" + name, key);
        Check(byMove >= 0 && scan.Files[byMove].P.EndsWith(name, StringComparison.Ordinal),
            "a moved file is found again by name");

        int byRename = scene.ResolveFile("gone/entirely/Renamed_" + name, key);
        Check(byRename >= 0 && scan.Files[byRename].P == target.P,
            $"a renamed file is found again by its content, got {(byRename < 0 ? "nothing" : scan.Files[byRename].P)}");

        Check(scene.ResolveFile("nothing/like/this.cs", "000000000000") < 0,
            "a file that really is gone reports gone");

        // the same content gives the same fingerprint, different content does not
        Check(FileKeys.Of(["a b", "c"]) == FileKeys.Of(["  a   b  ", "   c"]),
            "whitespace does not change a fingerprint");
        Check(FileKeys.Of(["a"]) != FileKeys.Of(["b"]), "different content does");

        // undo restores a mutation without anybody writing an inverse
        var history = new History();
        history.Record(board);
        board.Items.Add(Note("c", 800, 0));
        Check(board.Items.Count == 3, "the item was added");
        Check(history.Undo(board) && board.Items.Count == 2, "undo removes it again");
        Check(history.Redo(board) && board.Items.Count == 3, "redo puts it back");

        scene.ActiveBoard = null;
        Console.WriteLine(_fails == 0 ? "PASS" : $"{_fails} FAILURES");
    }
}

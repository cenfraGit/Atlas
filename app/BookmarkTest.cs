namespace Atlas;

/// <summary>run: dotnet run -- --bookmarktest &lt;repo&gt;</summary>
public static class BookmarkTest
{
    static int _fails;

    static void Check(bool ok, string what)
    {
        if (!ok) { _fails++; Console.WriteLine("  FAIL: " + what); }
    }

    public static void Run(string[] args)
    {
        var repo = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--") && Directory.Exists(a));
        if (repo is null) { Console.WriteLine("usage: --bookmarktest <repo>"); return; }

        var scan = Scanner.Build(repo);
        var scene = new Scene(scan);
        const float vw = 1400, vh = 900;
        Console.WriteLine($"{scan.Files.Count} files from {repo}");

        // a middling file with real content in it: half the files in a repo
        // are a few lines long, and a five line file proves nothing
        var candidates = scan.Files.Where(f => f.N > 150).ToList();
        var target = candidates.Count > 0 ? candidates[candidates.Count / 2] : scan.Files[scan.Files.Count / 2];

        // capture: park the camera inside a file and the bookmark anchors to it
        scene.CamS = 5f;
        scene.CamX = target.X + target.W / 2;
        scene.CamY = target.Y + scan.HeaderH + 40 * scan.LineH;
        scene.Draw(new SkiaSharp.SKPictureRecorder().BeginRecording(new SkiaSharp.SKRect(0, 0, vw, vh)), vw, vh);
        var mark = BookmarkTargets.Capture(scene, "middle of the file", vw, vh);
        Check(mark.File == target.P, $"anchors to the file under the camera, got '{mark.File}'");
        Check(mark.Line < 40 && mark.EndLine > 40,
            $"captures the visible region around the camera, got {mark.Line}..{mark.EndLine}");

        // a region must frame those lines, not the whole file
        var wide = new Bookmark { Id = "w", Name = "whole", File = target.P, Line = -1, EndLine = -1 };
        var region = new Bookmark { Id = "r", Name = "region", File = target.P, Line = 10, EndLine = 29 };
        var wideT = BookmarkTargets.Resolve(scene, wide, vw, vh);
        var regionT = BookmarkTargets.Resolve(scene, region, vw, vh);
        Check(regionT.S > wideT.S, $"a 20 line region zooms in closer than the whole file ({regionT.S} vs {wideT.S})");
        // a short region is capped by readable line width, not by its own height
        float shown = vh / regionT.S / scan.LineH;
        Check(shown < 40, $"a 20 line region shows a tight window, {shown:F0} lines");

        // a tall region is governed by its own height instead
        var tall = new Bookmark { Id = "t", Name = "tall", File = target.P, Line = 0, EndLine = 199 };
        float tallShown = vh / BookmarkTargets.Resolve(scene, tall, vw, vh).S / scan.LineH;
        Check(tallShown is > 200 and < 280, $"a 200 line region frames itself, {tallShown:F0} lines");
        float centre = target.Y + scan.HeaderH + 20 * scan.LineH;
        Check(Math.Abs(regionT.Y - centre) < scan.LineH * 1.5f, "the region sits centred in the view");

        // when the card is wider than the screen, its left edge must stay visible
        float leftEdgeOnScreen = regionT.X - vw / (2 * regionT.S);
        Check(leftEdgeOnScreen <= target.X + 0.01f,
            $"the start of each line stays on screen (card left {target.X}, view left {leftEdgeOnScreen:F1})");

        // the point of anchoring: the layout can move and the bookmark still lands
        var before = BookmarkTargets.Resolve(scene, mark, vw, vh);
        Check(!before.Orphaned, "resolves while the file exists");

        foreach (var f in scan.Files) { f.X += 5000; f.Y += 7000; }
        var moved = new Scene(scan);
        var after = BookmarkTargets.Resolve(moved, mark, vw, vh);
        Check(Math.Abs(after.X - (before.X + 5000)) < 1.5f &&
              Math.Abs(after.Y - (before.Y + 7000)) < 1.5f,
              $"follows the file after a re-layout, got {after.X},{after.Y}");
        Check(Math.Abs(after.S - before.S) < 0.01f, "keeps the same zoom after a re-layout");

        // a deleted file must be reported, not crash or fly somewhere random
        var orphan = new Bookmark { Id = "x", Name = "gone", File = "no/such/file.cs", Line = 3, X = 1, Y = 2, S = 3 };
        var orphaned = BookmarkTargets.Resolve(moved, orphan, vw, vh);
        Check(orphaned.Orphaned, "reports a missing file as orphaned");
        Check(orphaned is { X: 1, Y: 2, S: 3 }, "falls back to the stored camera for an orphan");

        // a free bookmark is left alone
        var free = new Bookmark { Id = "y", Name = "whole map", X = 10, Y = 20, S = 0.03f };
        var freeTarget = BookmarkTargets.Resolve(moved, free, vw, vh);
        Check(freeTarget is { X: 10, Y: 20, Orphaned: false }, "free bookmarks keep their camera");

        // round trip through the json that gets committed to the repo
        var dir = Path.Combine(Path.GetTempPath(), "cv_bmtest_" + Guid.NewGuid().ToString("n")[..6]);
        Directory.CreateDirectory(dir);
        try
        {
            var store = BookmarkStore.Load(dir);
            store.Bookmarks.Add(mark);
            store.Bookmarks.Add(free);
            store.Tours.Add(new Tour { Id = "t1", Name = "a tour", Stops = [mark.Id, free.Id] });
            store.Save();
            Check(File.Exists(BookmarkStore.PathFor(dir)), "writes .atlas/bookmarks.json");

            var reread = BookmarkStore.Load(dir);
            Check(reread.Bookmarks.Count == 2 && reread.Tours.Count == 1, "reads back what it wrote");
            var rt = reread.ById(mark.Id);
            Check(rt is not null && rt.File == mark.File && rt.Line == mark.Line, "bookmark survives the round trip");
            Check(reread.Tours[0].Stops.SequenceEqual(new[] { mark.Id, free.Id }), "tour order survives");
            Check(reread.TourContaining(mark.Id)?.Id == "t1", "finds the tour holding a bookmark");

            var corrupt = BookmarkStore.PathFor(dir);
            File.WriteAllText(corrupt, "{ not json at all");
            var recovered = BookmarkStore.Load(dir);
            Check(recovered.Bookmarks.Count == 0, "a corrupt file loads as empty instead of throwing");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        Console.WriteLine(_fails == 0 ? "PASS" : $"{_fails} FAILURES");
    }
}

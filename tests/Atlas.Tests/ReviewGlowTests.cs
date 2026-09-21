using SkiaSharp;

namespace Atlas.Tests;

/// <summary>what a commit looks like on the map.
///
/// A changed file used to be painted solid, edge to edge, in one colour. A
/// two line fix in a nine hundred line file therefore drew a nine hundred
/// line block, and size on the map means size of file rather than size of
/// change - so the smallest commits looked like the largest ones. The card
/// is now a dim silhouette saying "touched" and the light is only where the
/// diff is.</summary>
public class ReviewGlowTests
{
    // --- runs ---------------------------------------------------------------

    [Fact]
    public void NoLinesAreNoRuns() => Assert.Empty(Scene.Runs([]));

    [Fact]
    public void OneLineIsOneRunOfOne()
    {
        var run = Assert.Single(Scene.Runs([7]));
        Assert.Equal((7, 7), run);
    }

    [Fact]
    public void ConsecutiveLinesAreOneRun()
    {
        var run = Assert.Single(Scene.Runs([4, 5, 6, 7]));
        Assert.Equal((4, 7), run);
    }

    /// <summary>a diff that changed every other line of a method changed
    /// that method, and the gaps are not worth drawing at a zoom where you
    /// cannot read them.</summary>
    [Fact]
    public void LinesACoupleApartAreStillOneRun()
    {
        var run = Assert.Single(Scene.Runs([10, 12, 14]));
        Assert.Equal((10, 14), run);
    }

    [Fact]
    public void LinesFarApartAreSeparateRuns()
    {
        var runs = Scene.Runs([3, 4, 80, 81]);
        Assert.Equal(2, runs.Count);
        Assert.Equal((3, 4), runs[0]);
        Assert.Equal((80, 81), runs[1]);
    }

    [Fact]
    public void RunsComeOutSortedAndDeduplicated()
    {
        var runs = Scene.Runs([9, 1, 9, 2, 1]);
        Assert.Equal(2, runs.Count);
        Assert.Equal((1, 2), runs[0]);
        Assert.Equal((9, 9), runs[1]);
    }

    /// <summary>the whole point: a run is one rectangle. Two hundred changed
    /// lines drawn one at a time is two hundred blurs a frame.</summary>
    [Fact]
    public void ABlockOfChangesIsOneRectangle() =>
        Assert.Single(Scene.Runs(Enumerable.Range(0, 200)));

    // --- a removal is a point, not a span ------------------------------------

    /// <summary>a deletion has no line of its own in the new file: it is
    /// recorded at the line it was taken from, and several in a row all
    /// land on the same number. Merging nearby ones into a span therefore
    /// claims lines that are still there.</summary>
    [Fact]
    public void RemovalsAtTheSamePlaceAreOneMark() =>
        Assert.Equal([40], Scene.Marks([40, 40, 40, 40, 40]));

    [Fact]
    public void RemovalsNearbyStaySeparateMarks() =>
        Assert.Equal([40, 42, 44], Scene.Marks([44, 40, 42, 40]));

    /// <summary>which is the difference from Runs, whose whole job is to
    /// merge - right for additions, wrong for these.</summary>
    [Fact]
    public void RunsWouldHaveMergedThem()
    {
        var run = Assert.Single(Scene.Runs([40, 42, 44]));
        Assert.Equal((40, 44), run);
    }

    // --- what is actually painted -------------------------------------------

    [Collection("render")]
    public class Rendered
    {
        const int W = 600, H = 600;

        static (Scene Scene, TempDir Repo, FileRec File) Reviewing(
            int added, int removed, IEnumerable<int>? addedAt = null, IEnumerable<int>? removedAt = null,
            float zoom = 0.06f, int focusLine = -1)
        {
            var repo = SampleRepo.Build();
            var scene = new Scene(Scanner.Build(repo.Path));

            var f = scene.Data.Files.First(x => x.P == SampleRepo.LongFile);
            var change = new FileChange(f.P, added, removed);
            change.AddedLines.AddRange(addedAt ?? []);
            change.RemovedAt.AddRange(removedAt ?? []);

            var set = new ChangeSet { Label = "c", Files = { change } };
            set.Index();
            scene.Review = set;

            // zoomed out by default, which is where the whole card used to
            // light up regardless of how little had changed
            scene.CamX = f.X + f.W / 2;
            // zoomed in, the middle of a three hundred line card is nowhere
            // near line forty, and a test aimed at empty space measures
            // nothing at all while looking like it passed
            scene.CamY = focusLine >= 0
                ? f.Y + scene.Data.HeaderH + focusLine * scene.Data.LineH
                : f.Y + f.H / 2;
            scene.CamS = zoom;
            scene.Tier = Scene.TierFor(scene.CamS);
            return (scene, repo, f);
        }

        /// <summary>lit pixels: anything appreciably brighter than the
        /// background wash the review veil leaves behind.</summary>
        static int Lit(Scene scene)
        {
            var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bmp)) { scene.Draw(canvas, W, H); canvas.Flush(); }

            int n = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    if (c.Red + c.Green + c.Blue > 150) n++;
                }
            bmp.Dispose();
            return n;
        }

        /// <summary>the defect, as a test: two lines changed and two hundred
        /// changed must not look the same.</summary>
        [Fact]
        public void ASmallChangeLightsLessThanALargeOne()
        {
            var (small, repoA, _) = Reviewing(2, 0, addedAt: [100, 101]);
            var (large, repoB, _) = Reviewing(200, 0, addedAt: Enumerable.Range(40, 200));

            using (repoA)
            using (repoB)
            using (small)
            using (large)
            {
                int a = Lit(small), b = Lit(large);
                Assert.True(b > a * 2,
                    $"two changed lines lit {a} pixels and two hundred lit {b}");
            }
        }

        /// <summary>a file that was touched still has to be findable among
        /// hundreds, even when almost nothing in it changed.</summary>
        [Fact]
        public void ATouchedFileIsStillVisible()
        {
            var (scene, repo, _) = Reviewing(1, 0, addedAt: [50]);
            using (repo)
            using (scene)
                Assert.True(Lit(scene) > 0, "a changed file left no mark at all");
        }

        /// <summary>red pixels: ones where the red channel clearly leads.</summary>
        static int Red(Scene scene)
        {
            var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bmp)) { scene.Draw(canvas, W, H); canvas.Flush(); }

            int n = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    if (c.Red > 120 && c.Red > c.Green + 50 && c.Red > c.Blue + 50) n++;
                }
            bmp.Dispose();
            return n;
        }

        static SKColor[] Pixels(Scene scene)
        {
            var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bmp)) { scene.Draw(canvas, W, H); canvas.Flush(); }

            var px = new SKColor[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    px[y * W + x] = bmp.GetPixel(x, y);
            bmp.Dispose();
            return px;
        }

        /// <summary>a removal is the one case where the absence of code is
        /// the change, and it was a bare hairline: one row of pixels with no
        /// light on it, next to the glow every addition gets.
        ///
        /// Measured by taking the same card with and without the removal and
        /// counting the rows that differ - which is what a glow is, the mark
        /// reaching past the line it marks. Counting red pixels cannot do
        /// this: a syntax-coloured card is full of red already.</summary>
        [Fact]
        public void ARemovalGlowsRatherThanBeingAHairline()
        {
            // the same change either way, so the card's outline is the same
            // colour in both. Only where the removal is marked differs - the
            // outline runs the full height of the card and would otherwise
            // swamp the thing being measured
            var (plain, repoA, _) = Reviewing(0, 3, removedAt: [], zoom: 1.2f, focusLine: 40);
            var (deleted, repoB, _) = Reviewing(0, 3, removedAt: [40], zoom: 1.2f, focusLine: 40);

            using (repoA)
            using (repoB)
            using (plain)
            using (deleted)
            {
                var before = Pixels(plain);
                var after = Pixels(deleted);

                int rows = 0;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                        if (before[y * W + x] != after[y * W + x]) { rows++; break; }

                Assert.True(rows > 6,
                    $"one deleted line changed {rows} rows: that is a hairline, not a glow");
            }
        }

        /// <summary>a wholly new file has every line marked, and at a strong
        /// alpha that is a rectangle of one colour laid over the source -
        /// unreadable exactly when the source has become readable, which is
        /// what the gathered change view looked like for any added file.
        ///
        /// Measured as how far the picture moved. A tint shifts every pixel
        /// a little; a wash drags them all toward one colour. Counting
        /// distinct colours does not work here and was the first thing I
        /// tried: alpha blending is injective, so a wash keeps every colour
        /// distinct while making none of them legible.</summary>
        [Fact]
        public void AWhollyAddedFileIsTintedRatherThanWashedOut()
        {
            var (plain, repoA, _) = Reviewing(0, 0, zoom: 2.4f, focusLine: 30);
            var (allNew, repoB, _) = Reviewing(
                SampleRepo.LongFileLines, 0,
                addedAt: Enumerable.Range(0, SampleRepo.LongFileLines), zoom: 2.4f, focusLine: 30);

            using (repoA)
            using (repoB)
            using (plain)
            using (allNew)
            {
                var before = Pixels(plain);
                var after = Pixels(allNew);

                // over the pixels that moved, not over the frame: most of
                // the frame is empty canvas the tint never touches, and
                // averaging those in hides the difference entirely
                long total = 0;
                int moved = 0;
                for (int i = 0; i < before.Length; i++)
                {
                    int d = Math.Abs(before[i].Red - after[i].Red)
                          + Math.Abs(before[i].Green - after[i].Green)
                          + Math.Abs(before[i].Blue - after[i].Blue);
                    if (d == 0) continue;
                    total += d;
                    moved++;
                }

                Assert.True(moved > 1000, $"only {moved} pixels changed at all");
                double shift = total / (3.0 * moved);
                Assert.True(shift < 40,
                    $"marking every line moved each channel by {shift:0.0} of 255: that is a wash, not a tint");
            }
        }

        /// <summary>and it is still obviously changed: the gutter stripe is
        /// what carries the signal once the wash is only a tint.</summary>
        [Fact]
        public void AndStillObviouslyChanged()
        {
            var (plain, repoA, _) = Reviewing(0, 0, zoom: 2.4f, focusLine: 30);
            var (allNew, repoB, _) = Reviewing(
                SampleRepo.LongFileLines, 0,
                addedAt: Enumerable.Range(0, SampleRepo.LongFileLines), zoom: 2.4f, focusLine: 30);

            using (repoA)
            using (repoB)
            using (plain)
            using (allNew)
            {
                var before = Pixels(plain);
                var after = Pixels(allNew);

                int moved = 0;
                for (int i = 0; i < before.Length; i++) if (before[i] != after[i]) moved++;

                Assert.True(moved > before.Length / 20,
                    $"marking every line changed only {moved} pixels of {before.Length}");
            }
        }

        /// <summary>five removals scattered down a file drew one tall solid
        /// rectangle over the code between them, because the marks were
        /// merged into a span and the span was filled. The lines between
        /// two deletions are lines that are still there.</summary>
        [Fact]
        public void ScatteredRemovalsDoNotPaintOverTheCodeBetweenThem()
        {
            var (spread, repo, _) = Reviewing(
                0, 5, removedAt: [36, 38, 40, 42, 44], zoom: 1.2f, focusLine: 40);

            using (repo)
            using (spread)
            {
                var px = Pixels(spread);

                // rows that are *filled* with red across the card, not rows
                // the glow merely reaches. A glow spreading over the code is
                // the point of it; an opaque band hiding the code is not
                var del = new SKColor(0xd9, 0x5c, 0x5c);
                var filled = new bool[H];
                for (int y = 0; y < H; y++)
                {
                    int solid = 0;
                    for (int x = 0; x < W; x++)
                    {
                        var c = px[y * W + x];
                        if (Math.Abs(c.Red - del.Red) < 40 && Math.Abs(c.Green - del.Green) < 40 &&
                            Math.Abs(c.Blue - del.Blue) < 40) solid++;
                    }
                    filled[y] = solid > 120;
                }

                int longest = 0, run = 0;
                foreach (var hit in filled)
                {
                    run = hit ? run + 1 : 0;
                    longest = Math.Max(longest, run);
                }

                Assert.Contains(true, filled);
                Assert.True(longest < 8,
                    $"a {longest} row unbroken band of solid red - that is a block over live code, not marks");
            }
        }

        /// <summary>everything gone is the whole card, in red - which is the
        /// one time painting the lot is the honest picture.</summary>
        [Fact]
        public void AFileEmptiedEntirelyGoesRedAllOver()
        {
            var (whole, repoA, f) = Reviewing(0, SampleRepo.LongFileLines + 10, removedAt: [0]);
            var (part, repoB, _) = Reviewing(0, 8, removedAt: [10, 11]);

            using (repoA)
            using (repoB)
            using (whole)
            using (part)
                // not a huge multiple, and it should not be: two deleted
                // lines are floored to a few pixels so they stay findable,
                // and at map zoom the whole card is only a few pixels more
                Assert.True(Lit(whole) > Lit(part) * 2,
                    $"wiping a file out lit {Lit(whole)} and deleting two lines lit {Lit(part)}");
        }

        [Fact]
        public void CloseInTheChangedLinesAreLitAndTheRestIsNot()
        {
            var (scene, repo, f) = Reviewing(6, 0, addedAt: [20, 21, 22, 23, 24, 25]);
            using (repo)
            using (scene)
            {
                // in far enough to be reading bars, not blocks
                scene.CamX = f.X + f.W / 2;
                scene.CamY = f.Y + scene.Data.HeaderH + 22 * scene.Data.LineH;
                scene.CamS = 1.2f;
                scene.Tier = Scene.TierFor(scene.CamS);

                int some = Lit(scene);

                scene.Review!.Files[0].AddedLines.Clear();
                scene.Review.Files[0].AddedLines.AddRange(Enumerable.Range(0, 200));
                int lots = Lit(scene);

                Assert.True(lots > some, $"six lit lines drew {some}, two hundred drew {lots}");
            }
        }
    }
}

using SkiaSharp;

namespace Atlas.Tests;

/// <summary>words on a board: a standalone label, a note, and - new - text
/// inside a shape.
///
/// A box with a label in it is most of what a flowchart is, and writing one
/// meant putting a separate label on top of a rectangle and then moving the
/// two together for ever afterwards. The size of any of them is the user's,
/// with a per-kind default rather than one number for everything: a label is
/// large because being large is what makes it a heading, and a note is small
/// because it is an aside.</summary>
public class TextOnBoardTests
{
    [Fact]
    public void ALabelIsLargeAndANoteIsSmall() =>
        Assert.True(Scene.DefaultSize("text") > Scene.DefaultSize("note"));

    [Fact]
    public void WordsInAShapeSitBetweenTheTwo()
    {
        Assert.True(Scene.DefaultSize("shape") > Scene.DefaultSize("note"));
        Assert.True(Scene.DefaultSize("shape") < Scene.DefaultSize("text"));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("note")]
    [InlineData("shape")]
    [InlineData("ellipse")]
    [InlineData("diamond")]
    public void AChosenSizeBeatsTheDefault(string kind) =>
        Assert.Equal(29f, Scene.SizeOf(new BoardItem { Kind = kind, Size = 29f }));

    [Theory]
    [InlineData("text")]
    [InlineData("note")]
    [InlineData("shape")]
    public void ZeroMeansWhateverThatKindIsNormally(string kind) =>
        Assert.Equal(Scene.DefaultSize(kind), Scene.SizeOf(new BoardItem { Kind = kind }));

    /// <summary>lines have to move apart as the type grows, or a note set in
    /// 34 point is one solid block of overlapping words.</summary>
    [Fact]
    public void TheLineStepGrowsWithTheType()
    {
        Assert.True(Scene.LineStep(34) > Scene.LineStep(11));
        Assert.True(Scene.LineStep(11) > 11, "a line needs more room than the type is tall");
    }

    static (Scene Scene, Board Board, TempDir Repo) Boarded()
    {
        var repo = SampleRepo.Build();
        var board = new Board { Id = "b", Name = "words" };
        var scene = new Scene(Scanner.Build(repo.Path)) { ActiveBoard = board, CamS = 1f };
        return (scene, board, repo);
    }

    /// <summary>a note is as tall as its words, so making the words bigger
    /// has to make the note taller or the text runs out of the bottom - the
    /// card overflow bug, in miniature.</summary>
    [Fact]
    public void ABiggerNoteIsATallerNote()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var small = new BoardItem
            {
                Id = "n1", Kind = "note", W = 300,
                Text = "a sentence long enough to wrap onto more than one line",
            };
            var large = new BoardItem
            {
                Id = "n2", Kind = "note", W = 300, Size = 28,
                Text = "a sentence long enough to wrap onto more than one line",
            };
            board.Items.Add(small);
            board.Items.Add(large);

            Assert.True(scene.ItemHeight(large) > scene.ItemHeight(small) * 2,
                $"small {scene.ItemHeight(small)}, large {scene.ItemHeight(large)}");
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void ALabelGrowsWithItsTypeToo()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var small = new BoardItem { Id = "t1", Kind = "text", W = 400, Text = "heading", Size = 12 };
            var large = new BoardItem { Id = "t2", Kind = "text", W = 400, Text = "heading", Size = 48 };
            board.Items.Add(small);
            board.Items.Add(large);

            Assert.True(scene.ItemHeight(large) > scene.ItemHeight(small));
            scene.ActiveBoard = null;
        }
    }

    /// <summary>the shape itself is not resized by its words. A box you drew
    /// 300 wide stays 300 wide when you type into it; the words wrap.</summary>
    [Theory]
    [InlineData("shape")]
    [InlineData("ellipse")]
    [InlineData("diamond")]
    public void WordsDoNotChangeTheSizeOfAShape(string kind)
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = new BoardItem { Id = "s", Kind = kind, W = 300, H = 160 };
            board.Items.Add(it);
            float before = scene.ItemHeight(it);

            it.Text = "a long sentence that would wrap onto several lines if it were allowed to";

            Assert.Equal(before, scene.ItemHeight(it));
            scene.ActiveBoard = null;
        }
    }

    // --- it is really drawn ------------------------------------------------

    [Collection("render")]
    public class Rendered
    {
        const int W = 400, H = 400;
        static readonly SKColor Empty = new(0x16, 0x10, 0x28);

        static int Ink(params BoardItem[] items)
        {
            using var repo = SampleRepo.Build();
            var board = new Board { Id = "b", Name = "words" };
            foreach (var it in items) board.Items.Add(it);

            using var scene = new Scene(Scanner.Build(repo.Path))
            {
                ActiveBoard = board, CamX = 0, CamY = 0, CamS = 1f,
            };

            var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bmp))
            {
                scene.Draw(canvas, W, H);
                canvas.Flush();
            }

            int n = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (bmp.GetPixel(x, y) != Empty) n++;

            bmp.Dispose();
            scene.ActiveBoard = null;
            return n;
        }

        static BoardItem Box(string kind, string text) => new()
        {
            Id = "s", Kind = kind, X = -140, Y = -80, W = 280, H = 160,
            Color = "#5fd3f3", Fill = BoardItem.NoFill, Text = text,
        };

        [Theory]
        [InlineData("shape")]
        [InlineData("ellipse")]
        [InlineData("diamond")]
        public void AShapeWithWordsDrawsThem(string kind)
        {
            int blank = Ink(Box(kind, ""));
            int worded = Ink(Box(kind, "decide"));

            Assert.True(worded > blank,
                $"{kind} with a word in it lit {worded} pixels, empty lit {blank}");
        }

        [Fact]
        public void BiggerWordsInAShapeMakeMoreInk()
        {
            var small = Box("shape", "decide");
            small.Size = 12;
            var large = Box("shape", "decide");
            large.Size = 40;

            Assert.True(Ink(large) > Ink(small));
        }

        /// <summary>the accent used to be a three unit bar down the left
        /// edge, which reads as a quote in a document rather than as a card
        /// on a canvas. A border all the way round touches the top.</summary>
        [Fact]
        public void ANotesBorderGoesAllTheWayRound()
        {
            using var repo = SampleRepo.Build();
            var board = new Board { Id = "b", Name = "note" };
            board.Items.Add(new BoardItem
            {
                Id = "n", Kind = "note", X = -120, Y = -60, W = 240,
                Text = "a note", Color = "#ffd166",
            });

            using var scene = new Scene(Scanner.Build(repo.Path))
            {
                ActiveBoard = board, CamX = 0, CamY = 0, CamS = 1f,
            };
            var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bmp))
            {
                scene.Draw(canvas, W, H);
                canvas.Flush();
            }

            // the note's top edge is at board y -60, which is 140px down
            var accent = new SKColor(0xff, 0xd1, 0x66);
            bool Near(SKColor c) =>
                Math.Abs(c.Red - accent.Red) < 60 &&
                Math.Abs(c.Green - accent.Green) < 60 &&
                Math.Abs(c.Blue - accent.Blue) < 70;

            int onTop = 0, onRight = 0;
            for (int x = 85; x < 315; x++)
                if (Near(bmp.GetPixel(x, 140))) onTop++;
            for (int y = 145; y < 190; y++)
                if (Near(bmp.GetPixel(319, y))) onRight++;

            bmp.Dispose();
            scene.ActiveBoard = null;

            Assert.True(onTop > 150, $"only {onTop} pixels of border along the top");
            Assert.True(onRight > 20, $"only {onRight} pixels of border down the right");
        }
    }
}

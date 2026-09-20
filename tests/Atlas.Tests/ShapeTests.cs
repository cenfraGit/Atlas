using SkiaSharp;

namespace Atlas.Tests;

/// <summary>rectangles, ellipses, diamonds and labels.
///
/// The three outlined shapes are deliberately the same thing with a
/// different path traced round the same box, so most of what is worth
/// checking is that they stayed the same thing: picked the same way, resized
/// the same way, saved the same way. What is worth checking per shape is that
/// they actually come out different, which takes a render.</summary>
public class ShapeTests
{
    static BoardItem Shape(string kind, float w = 200, float h = 120) =>
        new() { Id = "s_" + kind, Kind = kind, X = 0, Y = 0, W = w, H = h };

    static (Scene Scene, Board Board, TempDir Repo) Boarded()
    {
        var repo = SampleRepo.Build();
        var board = new Board { Id = "b", Name = "drawing" };
        var scene = new Scene(Scanner.Build(repo.Path)) { ActiveBoard = board, CamS = 1f };
        return (scene, board, repo);
    }

    [Theory]
    [InlineData("shape")]
    [InlineData("ellipse")]
    [InlineData("diamond")]
    public void AllThreeCountAsShapes(string kind) => Assert.True(Scene.IsShape(kind));

    [Theory]
    [InlineData("note")]
    [InlineData("text")]
    [InlineData("arrow")]
    [InlineData("stroke")]
    [InlineData("file")]
    public void NothingElseDoes(string kind) => Assert.False(Scene.IsShape(kind));

    [Theory]
    [InlineData("shape")]
    [InlineData("ellipse")]
    [InlineData("diamond")]
    [InlineData("text")]
    public void TheyAllResize(string kind) =>
        Assert.True(Scene.Resizable(new BoardItem { Kind = kind, Text = "x" }));

    [Theory]
    [InlineData("shape")]
    [InlineData("ellipse")]
    [InlineData("diamond")]
    public void AShapeKeepsTheHeightItIsGiven(string kind)
    {
        var (scene, _, repo) = Boarded();
        using (repo)
        using (scene)
        {
            Assert.Equal(120, scene.ItemHeight(Shape(kind)));
            scene.ActiveBoard = null;
        }
    }

    [Theory]
    [InlineData("shape")]
    [InlineData("ellipse")]
    [InlineData("diamond")]
    public void AShapeWithNoHeightStillHasOne(string kind)
    {
        var (scene, _, repo) = Boarded();
        using (repo)
        using (scene)
        {
            Assert.True(scene.ItemHeight(Shape(kind, h: 0)) > 0);
            scene.ActiveBoard = null;
        }
    }

    [Theory]
    [InlineData("shape")]
    [InlineData("ellipse")]
    [InlineData("diamond")]
    [InlineData("text")]
    public void TheyArePickedAndSweptLikeAnythingElse(string kind)
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Shape(kind);
            it.Text = "hello";
            board.Items.Add(it);

            Assert.Equal(it.Id, scene.ItemAt(10, 10)?.Id);
            Assert.Equal([it.Id], scene.ItemsIn(new SKRect(-10, -10, 400, 400)).Select(i => i.Id));

            scene.ActiveBoard = null;
        }
    }

    [Theory]
    [InlineData("shape")]
    [InlineData("ellipse")]
    [InlineData("diamond")]
    [InlineData("text")]
    public void TheyCountTowardsTheBoardsBounds(string kind)
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Shape(kind);
            it.X = 400;
            it.Y = 300;
            it.Text = "hello";
            board.Items.Add(it);

            var b = scene.ContentBounds();
            Assert.True(b.Right >= 600, $"{kind} is outside the bounds");

            scene.ActiveBoard = null;
        }
    }

    // --- labels -----------------------------------------------------------

    [Fact]
    public void ALabelsHeightFollowsItsWords()
    {
        var (scene, _, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var one = new BoardItem { Kind = "text", W = 600, Text = "one line", Size = 30 };
            var many = new BoardItem
            {
                Kind = "text", W = 600, Size = 30,
                Text = string.Join(" ", Enumerable.Repeat("word", 60)),
            };

            Assert.True(scene.ItemHeight(many) > scene.ItemHeight(one) * 3,
                "a wrapped label should be far taller than a single line");

            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void ALabelsHeightFollowsItsTypeSize()
    {
        var (scene, _, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var small = new BoardItem { Kind = "text", W = 600, Text = "heading", Size = 12 };
            var large = new BoardItem { Kind = "text", W = 600, Text = "heading", Size = 48 };

            Assert.True(scene.ItemHeight(large) > scene.ItemHeight(small) * 2);

            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void ALabelWithNoSizeFallsBackToTheDefault()
    {
        var (scene, _, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var unset = new BoardItem { Kind = "text", W = 600, Text = "heading" };
            var set = new BoardItem { Kind = "text", W = 600, Text = "heading", Size = Scene.LabelSize };

            Assert.Equal(scene.ItemHeight(set), scene.ItemHeight(unset), 2);

            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void AnEmptyLabelStillHasAHeightToPick()
    {
        var (scene, _, repo) = Boarded();
        using (repo)
        using (scene)
        {
            Assert.True(scene.ItemHeight(new BoardItem { Kind = "text", W = 600, Text = "" }) > 0);
            scene.ActiveBoard = null;
        }
    }

    // --- storage ----------------------------------------------------------

    [Theory]
    [InlineData("shape")]
    [InlineData("ellipse")]
    [InlineData("diamond")]
    public void AShapeRoundTrips(string kind)
    {
        using var dir = new TempDir();
        var store = BoardStore.Load(dir.Path);
        var board = store.Create("shapes");
        var it = Shape(kind);
        it.Color = "#b48ae8";
        board.Items.Add(it);
        store.Save(board);

        var reread = Assert.Single(BoardStore.Load(dir.Path).Boards).Items[0];

        Assert.Equal(kind, reread.Kind);
        Assert.Equal("#b48ae8", reread.Color);
        Assert.Equal(120, reread.H);
    }

    [Fact]
    public void ALabelRoundTripsWithItsSize()
    {
        using var dir = new TempDir();
        var store = BoardStore.Load(dir.Path);
        var board = store.Create("labels");
        board.Items.Add(new BoardItem
        {
            Id = "t1", Kind = "text", Text = "How a frame is drawn",
            W = 760, Size = 48, Color = "#ffd166",
        });
        store.Save(board);

        var reread = Assert.Single(BoardStore.Load(dir.Path).Boards).Items[0];

        Assert.Equal("text", reread.Kind);
        Assert.Equal("How a frame is drawn", reread.Text);
        Assert.Equal(48, reread.Size);
    }

    [Fact]
    public void UndoRestoresAShape()
    {
        var board = new Board { Id = "b", Name = "shapes" };
        board.Items.Add(Shape("diamond"));
        var history = new History();

        history.Record(board);
        board.Items.Clear();
        Assert.True(history.Undo(board));

        Assert.Equal("diamond", Assert.Single(board.Items).Kind);
    }

    // --- they really are different ----------------------------------------

    /// <summary>draw one shape filling the view and count the lit pixels.
    /// Three outlines round the same box could easily all be a rectangle by
    /// accident; the areas are what says otherwise.</summary>
    [Collection("render")]
    public class Rendered
    {
        const int W = 400, H = 400;
        static readonly SKColor Empty = new(0x16, 0x10, 0x28);   // the board background

        static int Lit(string kind)
        {
            using var repo = SampleRepo.Build();
            var board = new Board { Id = "b", Name = "shapes" };
            board.Items.Add(new BoardItem
            {
                Id = "s", Kind = kind, X = -150, Y = -150, W = 300, H = 300,
                Color = "#5fd3f3", Text = "",
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

            int n = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (bmp.GetPixel(x, y) != Empty) n++;

            bmp.Dispose();
            scene.ActiveBoard = null;
            return n;
        }

        [Fact]
        public void AnEllipseCoversLessThanItsBox()
        {
            int rect = Lit("shape");
            int ellipse = Lit("ellipse");

            // pi/4 of the square, give or take the grid and the outline
            Assert.InRange(ellipse, (int)(rect * 0.6), (int)(rect * 0.92));
        }

        [Fact]
        public void ADiamondCoversLessStill()
        {
            int ellipse = Lit("ellipse");
            int diamond = Lit("diamond");

            // half the square against pi/4 of it
            Assert.True(diamond < ellipse, $"diamond {diamond} should be smaller than ellipse {ellipse}");
        }

        [Fact]
        public void AllThreeActuallyDrawSomething()
        {
            foreach (var kind in new[] { "shape", "ellipse", "diamond" })
                Assert.True(Lit(kind) > 5000, $"{kind} drew almost nothing");
        }
    }
}

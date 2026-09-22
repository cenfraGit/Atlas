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
    [InlineData("shape", 3)]
    [InlineData("shape", 12)]
    [InlineData("ellipse", 8)]
    [InlineData("diamond", 20)]
    public void AShapeCanBeAsShortAsYouLike(string kind, float h)
    {
        var (scene, _, repo) = Boarded();
        using (repo)
        using (scene)
        {
            // there used to be a floor of 40, so a long thin rectangle - a
            // divider, an underline - could not be made at all
            Assert.Equal(h, scene.ItemHeight(Shape(kind, w: 600, h: h)));
            scene.ActiveBoard = null;
        }
    }

    // --- resizing from any corner ----------------------------------------

    [Fact]
    public void DraggingTheBottomRightGripMovesThatCornerOnly()
    {
        var (scene, _, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Shape("shape", 200, 120);
            scene.Resize(it, corner: 0, wx: 300, wy: 200);

            Assert.Equal(0, it.X);            // the far corner stays put
            Assert.Equal(0, it.Y);
            Assert.Equal(300, it.W);
            Assert.Equal(200, it.H);
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void DraggingATopLeftGripMovesTheOriginToo()
    {
        var (scene, _, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Shape("shape", 200, 120);      // 0,0 to 200,120
            scene.Resize(it, Scene.GripLeft | Scene.GripTop, wx: 50, wy: 30);

            // the corner you are holding goes to the pointer...
            Assert.Equal(50, it.X);
            Assert.Equal(30, it.Y);
            // ...and the one across from it does not move
            Assert.Equal(200, it.X + it.W);
            Assert.Equal(120, it.Y + it.H);
            scene.ActiveBoard = null;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Scene.GripLeft)]
    [InlineData(Scene.GripTop)]
    [InlineData(Scene.GripLeft | Scene.GripTop)]
    public void TheOppositeCornerNeverMoves(int corner)
    {
        var (scene, _, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Shape("shape", 200, 120);
            float fixedX = (corner & Scene.GripLeft) != 0 ? it.X + it.W : it.X;
            float fixedY = (corner & Scene.GripTop) != 0 ? it.Y + it.H : it.Y;

            scene.Resize(it, corner, wx: 77, wy: 44);

            Assert.Equal(fixedX, (corner & Scene.GripLeft) != 0 ? it.X + it.W : it.X, 2);
            Assert.Equal(fixedY, (corner & Scene.GripTop) != 0 ? it.Y + it.H : it.Y, 2);
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void ABoxDraggedInsideOutStopsAtAMinimumRatherThanInverting()
    {
        var (scene, _, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Shape("shape", 200, 120);
            // drag the bottom right corner way past the top left
            scene.Resize(it, corner: 0, wx: -500, wy: -500);

            Assert.True(it.W > 0, $"width inverted to {it.W}");
            Assert.True(it.H > 0, $"height inverted to {it.H}");
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void AGripIsFoundAtEachOfTheFourCorners()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Shape("shape", 200, 120);
            board.Items.Add(it);
            scene.Picked.Add(it.Id);

            Assert.Equal(0, scene.GripAt(200, 120)!.Value.Corner);
            Assert.Equal(Scene.GripLeft, scene.GripAt(0, 120)!.Value.Corner);
            Assert.Equal(Scene.GripTop, scene.GripAt(200, 0)!.Value.Corner);
            Assert.Equal(Scene.GripLeft | Scene.GripTop, scene.GripAt(0, 0)!.Value.Corner);

            Assert.Null(scene.GripAt(100, 60));     // the middle is not a grip
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void AnUnpickedItemOffersNoGrips()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            board.Items.Add(Shape("shape", 200, 120));

            Assert.Null(scene.GripAt(200, 120));
            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void ALabelIsResizedInWidthOnlyBecauseItsHeightIsItsWords()
    {
        var (scene, _, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = new BoardItem { Id = "t", Kind = "text", X = 0, Y = 0, W = 400, Text = "hello", Size = 20 };
            scene.Resize(it, corner: 0, wx: 700, wy: 900);

            Assert.Equal(700, it.W);
            Assert.Equal(0, it.H);      // untouched; the words decide
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
            var set = new BoardItem { Kind = "text", W = 600, Text = "heading", Size = Scene.CodeSize };

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

    // --- fill and border --------------------------------------------------

    static readonly SKColor Border = new(0x5f, 0xd3, 0xf3);

    [Fact]
    /// <summary>a shape with no fill chosen is empty. It used to be a wash
    /// of its border, and a shape is usually drawn round something - so the
    /// wash was one more thing between you and the code.</summary>
    public void AShapeWithNoFillChoiceIsEmpty() =>
        Assert.Equal(0, Scene.FillOf(Shape("shape"), Border).Alpha);

    /// <summary>the wash is still there for anyone who wants it.</summary>
    [Fact]
    public void AskingForTheBordersColourGivesAWashOfIt()
    {
        var it = Shape("shape");
        it.Fill = BoardItem.BorderFill;
        var fill = Scene.FillOf(it, Border);

        Assert.Equal(Border.Red, fill.Red);
        Assert.Equal(Border.Green, fill.Green);
        Assert.Equal(Border.Blue, fill.Blue);
        Assert.True(fill.Alpha is > 0 and < 64, $"a wash, not a slab: alpha {fill.Alpha}");
    }

    [Fact]
    public void NoFillIsGenuinelyTransparent()
    {
        var it = Shape("shape");
        it.Fill = BoardItem.NoFill;

        Assert.Equal(0, Scene.FillOf(it, Border).Alpha);
    }

    [Fact]
    public void AChosenFillIsThatColour()
    {
        var it = Shape("shape");
        it.Fill = "#d95c5c";

        var fill = Scene.FillOf(it, Border);

        Assert.Equal(0xd9, fill.Red);
        Assert.Equal(0x5c, fill.Green);
        Assert.Equal(0x5c, fill.Blue);
        Assert.True(fill.Alpha > 0);
    }

    [Fact]
    public void AFillStaysSomethingYouCanReadThrough()
    {
        var it = Shape("shape");
        it.Fill = "#d95c5c";

        // a board is full of code; a solid slab over it helps nobody
        Assert.True(Scene.FillOf(it, Border).Alpha < 128);
    }

    [Fact]
    public void AnUnparseableFillFallsBackToTheBorderRatherThanVanishing()
    {
        var it = Shape("shape");
        it.Fill = "not a colour";

        var fill = Scene.FillOf(it, Border);
        Assert.Equal(Border.Red, fill.Red);
        Assert.True(fill.Alpha > 0);
    }

    [Theory]
    [InlineData("shape")]
    [InlineData("ellipse")]
    [InlineData("diamond")]
    public void ATransparentShapeIsStillSolidToTheMouse(string kind)
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Shape(kind, 300, 200);
            it.Fill = BoardItem.NoFill;
            board.Items.Add(it);

            // the whole point of an empty shape is a frame drawn round other
            // things, and a frame you cannot pick up is a frame you cannot move
            Assert.Equal(it.Id, scene.ItemAt(150, 100)?.Id);
            Assert.Equal([it.Id], scene.ItemsIn(new SKRect(-10, -10, 400, 300)).Select(i => i.Id));

            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void ATransparentShapeStillResizesAndErases()
    {
        var (scene, board, repo) = Boarded();
        using (repo)
        using (scene)
        {
            var it = Shape("shape", 300, 200);
            it.Fill = BoardItem.NoFill;
            board.Items.Add(it);
            scene.Picked.Add(it.Id);

            Assert.NotNull(scene.GripAt(300, 200));
            Assert.Contains(scene.ItemsNear(150, 100, radius: 5), i => i.Id == it.Id);

            scene.ActiveBoard = null;
        }
    }

    [Fact]
    public void FillAndBorderAreSetSeparately()
    {
        var it = Shape("shape");
        it.Color = "#ffd166";     // border
        it.Fill = "#3fb96a";      // inside

        var border = Scene.ParseColor(it.Color, SKColors.White);
        var fill = Scene.FillOf(it, border);

        Assert.Equal(0xff, border.Red);
        Assert.Equal(0x3f, fill.Red);
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
    public void AFillRoundTrips()
    {
        using var dir = new TempDir();
        var store = BoardStore.Load(dir.Path);
        var board = store.Create("shapes");

        var empty = Shape("shape");
        empty.Id = "empty";
        empty.Fill = BoardItem.NoFill;
        var filled = Shape("ellipse");
        filled.Id = "filled";
        filled.Fill = "#3fb96a";
        board.Items.Add(empty);
        board.Items.Add(filled);
        store.Save(board);

        var reread = Assert.Single(BoardStore.Load(dir.Path).Boards).Items;

        Assert.Equal(BoardItem.NoFill, reread.Single(i => i.Id == "empty").Fill);
        Assert.Equal("#3fb96a", reread.Single(i => i.Id == "filled").Fill);
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
                // a fill on purpose: these measure the area each shape
                // covers, and the default is empty now
                Id = "s", Kind = kind, X = -150, Y = -150, W = 300, H = 300,
                Color = "#5fd3f3", Text = "", Fill = BoardItem.BorderFill,
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

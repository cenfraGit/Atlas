using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Atlas;

/// <summary>the open and closed hands used for panning.
///
/// CSS calls them grab and grabbing; Avalonia's StandardCursorType has
/// neither, so they are drawn here and handed over as bitmaps. They are
/// built once and kept: a Cursor allocates a native handle.
///
/// White fill with a dark outline, because the canvas is nearly black and a
/// plain black cursor disappears into it.
///
/// The first pair were drawn at 48px and filled it, which put a hand on
/// screen half again the size of every other cursor the system draws. A
/// cursor is not an illustration: it is read at a glance, out of the corner
/// of the eye, and the only thing that has to survive is the silhouette. So
/// these are 32px with a margin, which is what the classic grab hands were.</summary>
public static class Cursors
{
    /// <summary>the bitmap's edge, in pixels. Public so a preview renders at
    /// the size the cursor is actually used at rather than a guess.</summary>
    public const int Size = 32;

    /// <summary>the hands are laid out in this many units and scaled to
    /// <see cref="Size"/>, so the drawing reads as proportions rather than as
    /// pixel coordinates that have to be redone to change the size.</summary>
    const float Grid = 32f;

    static Cursor? _open, _closed;

    public static Cursor Open => _open ??= Build(closed: false);
    public static Cursor Closed => _closed ??= Build(closed: true);

    /// <summary>the hand's outline, so it can be drawn somewhere other than a
    /// cursor - there is no way to look at a cursor bitmap once it is one.</summary>
    public static SKPath PathFor(bool closed)
    {
        using var unit = closed ? ClosedHand() : OpenHand();
        var scaled = new SKPath();
        unit.Transform(SKMatrix.CreateScale(Size / Grid, Size / Grid), scaled);
        return scaled;
    }

    /// <summary>thin enough to stay a line at this size. Two pixels round a
    /// 20px hand is a hand with a border; one and a bit is a hand that can be
    /// told apart from the canvas behind it, which is all the outline is
    /// there to do.</summary>
    public const float EdgeWidth = 1.4f;

    static Cursor Build(bool closed)
    {
        using var bitmap = new SKBitmap(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var path = PathFor(closed);

            using var edge = new SKPaint
            {
                Color = new SKColor(0x10, 0x14, 0x1c, 235),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = EdgeWidth,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
            };
            using var fill = new SKPaint
            {
                Color = SKColors.White,
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
            };
            canvas.DrawPath(path, edge);
            canvas.DrawPath(path, fill);
        }

        // the hot spot is where the fingers meet the palm, not the middle of
        // the bitmap: that is the part of a hand you would say is pointing at
        // something, and it is where the classic grab cursors put it
        return new Cursor(ToAvalonia(bitmap),
            new PixelPoint((int)(Size * 0.5f), (int)(Size * 0.5f)));
    }

    /// <summary>a palm with four fingers up and a thumb out to the side.
    ///
    /// The parts are unioned rather than simply collected: stroking a pile of
    /// overlapping rounded rects outlines every one of them, including the
    /// edges buried inside the palm, and what you get is a blob with lines
    /// through it. A union leaves one silhouette to outline, so the fingers
    /// read as fingers where they are apart and vanish into the hand where
    /// they are not.</summary>
    static SKPath OpenHand()
    {
        var parts = new List<SKPath>
        {
            Palm(top: 15f, bottom: 26f),
            // the middle finger longest and the little one shortest, because
            // a hand with four equal fingers reads as a comb
            Finger(12.0f, 9.0f, 19f),
            Finger(15.4f, 7.2f, 19f),
            Finger(18.8f, 8.0f, 19f),
            Finger(21.8f, 10.5f, 19f),
            Thumb(open: true),
        };
        return Union(parts);
    }

    /// <summary>the same hand with the fingers curled in.</summary>
    static SKPath ClosedHand()
    {
        var parts = new List<SKPath>
        {
            Palm(top: 16f, bottom: 26f),
            // knuckles rather than fingers: a fist is shorter and squarer
            Finger(12.0f, 13.0f, 20f),
            Finger(15.4f, 12.2f, 20f),
            Finger(18.8f, 12.5f, 20f),
            Finger(21.8f, 14.0f, 20f),
            Thumb(open: false),
        };
        return Union(parts);
    }

    static SKPath Union(List<SKPath> parts)
    {
        var whole = parts[0];
        for (int i = 1; i < parts.Count; i++)
        {
            var next = whole.Op(parts[i], SKPathOp.Union);
            whole.Dispose();
            parts[i].Dispose();
            whole = next;
        }
        return whole;
    }

    static SKPath Palm(float top, float bottom)
    {
        var p = new SKPath();
        p.AddRoundRect(new SKRect(9.5f, top, 23f, bottom), 4.5f, 4.5f);
        return p;
    }

    static SKPath Finger(float cx, float top, float bottom)
    {
        var p = new SKPath();
        p.AddRoundRect(new SKRect(cx - 1.5f, top, cx + 1.5f, bottom), 1.5f, 1.5f);
        return p;
    }

    static SKPath Thumb(bool open)
    {
        var p = new SKPath();
        var box = open
            ? new SKRect(6f, 16.5f, 11f, 23f)      // out to the side
            : new SKRect(8.5f, 17.5f, 14f, 22.5f); // tucked across the fist
        p.AddRoundRect(box, 2.4f, 2.4f);
        return p;
    }

    /// <summary>skia bitmap to an avalonia one, through an encoded png. Not
    /// pretty, but it happens twice in the life of the process.</summary>
    static Bitmap ToAvalonia(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }
}

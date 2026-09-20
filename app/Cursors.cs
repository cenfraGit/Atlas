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
/// plain black cursor disappears into it.</summary>
public static class Cursors
{
    const int Size = 48;

    static Cursor? _open, _closed;

    public static Cursor Open => _open ??= Build(closed: false);
    public static Cursor Closed => _closed ??= Build(closed: true);

    /// <summary>the hand's outline, so it can be drawn somewhere other than a
    /// cursor - there is no way to look at a cursor bitmap once it is one.</summary>
    public static SKPath PathFor(bool closed) => closed ? ClosedHand() : OpenHand();

    static Cursor Build(bool closed)
    {
        using var bitmap = new SKBitmap(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var path = PathFor(closed);

            using var edge = new SKPaint
            {
                Color = new SKColor(0x10, 0x14, 0x1c, 230),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2.0f,
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

        // the hot spot is the middle of the palm: what you grab is what is
        // under the centre of the hand, not under a fingertip
        return new Cursor(ToAvalonia(bitmap), new PixelPoint(Size / 2, Size / 2));
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
            Palm(top: 24f, bottom: 40f),
            Finger(14.5f, 12f, 28f),
            Finger(20.5f, 8.5f, 28f),
            Finger(26.5f, 9.5f, 28f),
            Finger(32f, 13f, 28f),
            Thumb(open: true),
        };
        return Union(parts);
    }

    /// <summary>the same hand with the fingers curled in.</summary>
    static SKPath ClosedHand()
    {
        var parts = new List<SKPath>
        {
            Palm(top: 21f, bottom: 40f),
            // knuckles rather than fingers: a fist is shorter and squarer
            Finger(14.5f, 18f, 26f),
            Finger(20.5f, 16.5f, 26f),
            Finger(26.5f, 17f, 26f),
            Finger(32f, 19f, 26f),
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
        p.AddRoundRect(new SKRect(11f, top, 36f, bottom), 8f, 8f);
        return p;
    }

    static SKPath Finger(float cx, float top, float bottom)
    {
        var p = new SKPath();
        p.AddRoundRect(new SKRect(cx - 2.6f, top, cx + 2.6f, bottom), 2.6f, 2.6f);
        return p;
    }

    static SKPath Thumb(bool open)
    {
        var p = new SKPath();
        var box = open
            ? new SKRect(6f, 26f, 13.5f, 36f)      // out to the side
            : new SKRect(8f, 24f, 15f, 33f);       // tucked across the fist
        p.AddRoundRect(box, 3.5f, 3.5f);
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

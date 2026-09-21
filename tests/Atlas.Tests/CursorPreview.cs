using SkiaSharp;

namespace Atlas.Tests;

/// <summary>not an assertion about how a hand should look - that is a matter
/// of taste and a human has to see it. This writes the two cursors out at 8x
/// so they can be looked at, and checks the only things code can judge: that
/// there is ink, that it is inside the bitmap, and that the closed hand is
/// the shorter of the two.</summary>
[Collection("render")]
public class CursorPreview
{
    // the real one, so a change to the cursor's size cannot leave the
    // preview measuring a bitmap the app never draws
    const int Size = Cursors.Size;

    static SKBitmap Draw(bool closed)
    {
        var bmp = new SKBitmap(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var path = Cursors.PathFor(closed);
        using var edge = new SKPaint { Color = new SKColor(0x10, 0x14, 0x1c, 230), Style = SKPaintStyle.Stroke, StrokeWidth = Cursors.EdgeWidth, StrokeJoin = SKStrokeJoin.Round, IsAntialias = true };
        using var fill = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawPath(path, edge);
        canvas.DrawPath(path, fill);
        return bmp;
    }

    static (int Ink, SKRectI Bounds) Measure(SKBitmap bmp)
    {
        int ink = 0, x0 = Size, y0 = Size, x1 = -1, y1 = -1;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (bmp.GetPixel(x, y).Alpha > 24)
                {
                    ink++;
                    x0 = Math.Min(x0, x); y0 = Math.Min(y0, y);
                    x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
                }
        return (ink, new SKRectI(x0, y0, x1, y1));
    }

    [Fact]
    public void BothHandsHaveInkWellInsideTheBitmap()
    {
        foreach (var closed in new[] { false, true })
        {
            using var bmp = Draw(closed);
            var (ink, bounds) = Measure(bmp);

            // a fist covers about a fifth of its bitmap and an open hand a
            // little more; well under this and the silhouette has collapsed
            // into a smudge
            Assert.True(ink > Size * Size / 6,
                $"{(closed ? "closed" : "open")} hand has only {ink} pixels");
            Assert.True(bounds.Left >= 1 && bounds.Top >= 1, "the outline is clipped at the top left");
            Assert.True(bounds.Right <= Size - 2 && bounds.Bottom <= Size - 2,
                "the outline is clipped at the bottom right");
        }
    }

    [Fact]
    public void TheClosedHandIsShorterThanTheOpenOne()
    {
        using var open = Draw(false);
        using var shut = Draw(true);

        var o = Measure(open).Bounds;
        var c = Measure(shut).Bounds;

        Assert.True(c.Height < o.Height, $"a fist ({c.Height}) should be shorter than an open hand ({o.Height})");
    }

    /// <summary>writes both to the scratch directory at 8x, to be looked at.</summary>
    [Fact]
    public void WritePreviews()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atlas_cursors");
        Directory.CreateDirectory(dir);

        foreach (var closed in new[] { false, true })
        {
            using var small = Draw(closed);
            using var big = small.Resize(new SKImageInfo(Size * 8, Size * 8), SKFilterQuality.None);
            using var image = SKImage.FromBitmap(big);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(dir, closed ? "closed.png" : "open.png"));
            data.SaveTo(file);
        }
        Assert.True(File.Exists(Path.Combine(dir, "open.png")));
    }
}

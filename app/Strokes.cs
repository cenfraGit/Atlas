using SkiaSharp;

namespace Atlas;

/// <summary>freehand strokes on a board.
///
/// A stroke is the first board item that is not a box, so the geometry lives
/// here rather than being smeared across the drawing and input code. Its
/// bounds are written back into X/Y/W/H every time the points change, which
/// is what lets the rubberband, moving and the context menu keep treating
/// every item on a board identically.</summary>
public static class Strokes
{
    public const float DefaultWeight = 3f;

    /// <summary>how far apart two samples must be before the second is kept.
    /// A mouse reports far more points than a curve needs, and every one of
    /// them is stored, drawn and hit-tested forever.</summary>
    public const float MinStep = 2.5f;

    public static bool Is(BoardItem it) => it.Kind == "stroke";

    public static int CountOf(BoardItem it) => (it.Points?.Count ?? 0) / 2;

    public static SKPoint PointAt(BoardItem it, int i) =>
        new(it.Points![i * 2], it.Points[i * 2 + 1]);

    /// <summary>add a sample, dropping it when it is too close to the last one
    /// to be worth keeping.</summary>
    public static bool Add(BoardItem it, float x, float y, float minStep = MinStep)
    {
        it.Points ??= [];
        int n = CountOf(it);
        if (n > 0)
        {
            float dx = x - it.Points[^2], dy = y - it.Points[^1];
            if (dx * dx + dy * dy < minStep * minStep) return false;
        }
        it.Points.Add(x);
        it.Points.Add(y);
        Reframe(it);
        return true;
    }

    /// <summary>bring X/Y/W/H back in step with the points. The pen has width,
    /// so the bounds are grown by half of it at each edge - otherwise the ends
    /// of a stroke fall outside its own box.</summary>
    public static void Reframe(BoardItem it)
    {
        int n = CountOf(it);
        if (n == 0) { it.W = it.H = 0; return; }

        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            float x = it.Points![i * 2], y = it.Points[i * 2 + 1];
            x0 = Math.Min(x0, x); y0 = Math.Min(y0, y);
            x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
        }
        float pad = Math.Max(it.Weight, DefaultWeight) / 2 + 1;
        it.X = x0 - pad;
        it.Y = y0 - pad;
        it.W = x1 - x0 + pad * 2;
        it.H = y1 - y0 + pad * 2;
    }

    /// <summary>shift every point, and the bounds with them.</summary>
    public static void Move(BoardItem it, float dx, float dy)
    {
        if (it.Points is not { Count: > 0 }) return;
        for (int i = 0; i < it.Points.Count; i += 2)
        {
            it.Points[i] += dx;
            it.Points[i + 1] += dy;
        }
        Reframe(it);
    }

    /// <summary>the path to draw, smoothed through the midpoints of each pair
    /// of samples so a slow hand does not read as a row of corners.</summary>
    public static SKPath PathOf(BoardItem it)
    {
        var path = new SKPath();
        int n = CountOf(it);
        if (n == 0) return path;

        var first = PointAt(it, 0);
        path.MoveTo(first);
        if (n == 1) { path.LineTo(first.X + 0.01f, first.Y); return path; }
        if (n == 2) { path.LineTo(PointAt(it, 1)); return path; }

        for (int i = 1; i < n - 1; i++)
        {
            var a = PointAt(it, i);
            var b = PointAt(it, i + 1);
            path.QuadTo(a, new SKPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2));
        }
        path.LineTo(PointAt(it, n - 1));
        return path;
    }

    /// <summary>distance from a point to the stroke, or float.MaxValue when
    /// there is nothing to measure against.</summary>
    public static float DistanceTo(BoardItem it, float x, float y)
    {
        int n = CountOf(it);
        if (n == 0) return float.MaxValue;
        if (n == 1)
        {
            var p = PointAt(it, 0);
            return MathF.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));
        }

        float best = float.MaxValue;
        for (int i = 0; i < n - 1; i++)
        {
            var a = PointAt(it, i);
            var b = PointAt(it, i + 1);
            best = Math.Min(best, ToSegment(x, y, a, b));
            if (best == 0) break;
        }
        return best;
    }

    static float ToSegment(float x, float y, SKPoint a, SKPoint b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len2 = dx * dx + dy * dy;
        float t = len2 < 1e-6f ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / len2, 0, 1);
        float px = a.X + dx * t, py = a.Y + dy * t;
        return MathF.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
    }

    /// <summary>rub a hole in a stroke and hand back what is left.
    ///
    /// The whole-stroke eraser is the right default - it is what you want
    /// nine times out of ten and it cannot surprise you - but a long line
    /// drawn in one gesture is a single stroke, and taking the lot because
    /// you touched the end of it is not erasing, it is undo.
    ///
    /// Returns the surviving pieces, each a stroke in its own right: none
    /// when the rub covered everything, one when it took an end off, two when
    /// it bit out the middle. A piece of a single point is dropped, because a
    /// dot left behind where a line used to be is litter.</summary>
    public static List<BoardItem> Erase(BoardItem it, float x, float y, float radius)
    {
        var pieces = new List<BoardItem>();
        int n = CountOf(it);
        if (n == 0) return pieces;

        float reach = radius + Math.Max(it.Weight, DefaultWeight) / 2;
        var run = new List<float>();

        for (int i = 0; i < n; i++)
        {
            var p = PointAt(it, i);
            bool rubbed = (x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y) <= reach * reach;

            if (rubbed)
            {
                Flush(it, run, pieces);
                continue;
            }
            run.Add(p.X);
            run.Add(p.Y);
        }
        Flush(it, run, pieces);
        return pieces;
    }

    static void Flush(BoardItem from, List<float> run, List<BoardItem> into)
    {
        // two points is the shortest thing that is still a line
        if (run.Count >= 4)
        {
            var piece = new BoardItem
            {
                Id = BookmarkStore.NewId(),
                Kind = "stroke",
                Color = from.Color,
                Weight = from.Weight,
                Points = [.. run],
            };
            Reframe(piece);
            into.Add(piece);
        }
        run.Clear();
    }

    /// <summary>true when a point is near enough to count as touching. The
    /// pen's own width counts: a fat stroke is easier to hit, as it looks.</summary>
    public static bool Touches(BoardItem it, float x, float y, float tolerance)
    {
        // the bounds are the cheap rejection; most strokes fail here
        if (x < it.X - tolerance || x > it.X + it.W + tolerance ||
            y < it.Y - tolerance || y > it.Y + it.H + tolerance) return false;

        return DistanceTo(it, x, y) <= tolerance + Math.Max(it.Weight, DefaultWeight) / 2;
    }
}

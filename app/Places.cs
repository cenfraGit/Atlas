namespace Atlas;

/// <summary>a place to look at: a range of lines in a file, or a free camera
/// position. Not stored anywhere - it is how search, annotations and "add
/// this view to a board" describe where they want the camera, and
/// <see cref="Places.Resolve"/> turns that into a camera for the current
/// layout.</summary>
public sealed class Place
{
    public string Name { get; set; } = "";

    /// <summary>repo-relative path. null means a free camera position.</summary>
    public string? File { get; set; }

    /// <summary>content fingerprint of that file, so a renamed file is still
    /// found.</summary>
    public string? Key { get; set; }

    /// <summary>first line of the region. -1 means the whole file.</summary>
    public int Line { get; set; } = -1;

    /// <summary>last line of the region, inclusive. -1 means no region.</summary>
    public int EndLine { get; set; } = -1;

    // fallback camera, used for free positions and for missing files
    public float X { get; set; }
    public float Y { get; set; }
    public float S { get; set; }
}

public readonly record struct Target(float X, float Y, float S, bool Orphaned);

public static class Places
{
    /// <summary>centre the card when it fits, otherwise pin its left edge:
    /// code starts on the left, so that is the half worth showing.</summary>
    static float CameraX(FileRec f, float vw, float s) =>
        Math.Min(f.X + f.W / 2, f.X + vw / (2 * s) - 12 / s);

    /// <summary>where the camera should end up for a place. a place in a
    /// file is resolved against the current layout, so it still lands
    /// correctly after files move, are added or are removed.</summary>
    public static Target Resolve(Scene scene, Place b, float vw, float vh)
    {
        if (b.File is null) return new Target(b.X, b.Y, b.S, false);

        int i = scene.ResolveFile(b.File, b.Key);
        if (i < 0) return new Target(b.X, b.Y, b.S, true);

        var f = scene.Data.Files[i];
        float lineH = scene.Data.LineH, headerH = scene.Data.HeaderH;

        // a region: frame exactly those lines, so a tour can point at one
        // method rather than at whichever file happens to contain it
        if (b.EndLine >= b.Line && b.Line >= 0)
        {
            int count = b.EndLine - b.Line + 1;
            float s = vh * 0.82f / (count * lineH);
            // do not zoom past the point where code runs off the side. most
            // lines use well under the full card width, so allow the card to
            // overflow a little rather than framing a short region from afar
            s = Math.Min(s, vw * 0.9f / (f.W * 0.6f));
            s = Math.Clamp(s, 0.02f, 12f);
            float cy = f.Y + headerH + (b.Line + count / 2f) * lineH;
            return new Target(CameraX(f, vw, s), cy, s, false);
        }

        float whole = vw * 0.7f / f.W;
        float visibleH = vh / whole;
        float y = b.Line >= 0
            ? f.Y + headerH + b.Line * lineH
            : f.Y + Math.Min(f.H, visibleH) / 2;

        // keep the card on screen even when the anchor is near either end
        float lo = f.Y + Math.Min(f.H, visibleH) / 2;
        float hi = f.Y + f.H - Math.Min(f.H, visibleH) / 2;
        if (hi < lo) hi = lo;
        y = Math.Clamp(y, lo, hi);

        return new Target(CameraX(f, vw, whole), y, whole, false);
    }

    /// <summary>the current view. if a file sits under the centre of the
    /// screen the place is in it, and covers the lines on screen.</summary>
    public static Place Capture(Scene scene, float vw, float vh)
    {
        var b = new Place
        {
            X = scene.CamX,
            Y = scene.CamY,
            S = scene.CamS,
        };

        if (scene.Tier < 2) return b;
        int i = scene.FileAt(scene.CamX, scene.CamY);
        if (i < 0) return b;

        var f = scene.Data.Files[i];
        b.Key = scene.KeyFor(f.P);
        float lineH = scene.Data.LineH, headerH = scene.Data.HeaderH;
        float halfH = vh / 2 / scene.CamS;

        // the region is whatever is on screen right now. zoom onto one method
        // and the place is that method, with no extra selection step
        int top = (int)((scene.CamY - halfH - f.Y - headerH) / lineH);
        int bottom = (int)((scene.CamY + halfH - f.Y - headerH) / lineH);
        int last = Math.Max(0, f.N - 1);

        b.File = f.P;
        b.Line = Math.Clamp(top, 0, last);
        b.EndLine = Math.Clamp(bottom, b.Line, last);
        return b;
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atlas;

public sealed class Bookmark
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("note")] public string? Note { get; set; }

    /// <summary>repo-relative path. null means the bookmark is a free camera
    /// position (a wide view of the map) rather than a place in a file.</summary>
    [JsonPropertyName("file")] public string? File { get; set; }

    /// <summary>content fingerprint of that file, so the bookmark survives it
    /// being renamed or moved. A path alone made every bookmark and every tour
    /// stop in a file orphans the moment somebody renamed it.</summary>
    [JsonPropertyName("key")] public string? Key { get; set; }
    /// <summary>first line of the region. -1 means the whole file.</summary>
    [JsonPropertyName("line")] public int Line { get; set; } = -1;

    /// <summary>last line of the region, inclusive. -1 means no region, just a
    /// point. this is where a resolved symbol range will land later.</summary>
    [JsonPropertyName("endLine")] public int EndLine { get; set; } = -1;

    // fallback camera, used for free positions and for orphans
    [JsonPropertyName("x")] public float X { get; set; }
    [JsonPropertyName("y")] public float Y { get; set; }
    [JsonPropertyName("s")] public float S { get; set; }
}

public sealed class Tour
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("stops")] public List<string> Stops { get; set; } = [];
}

/// <summary>bookmarks and tours for one scanned repo, stored in that repo as
/// .atlas/bookmarks.json so they travel with it through git.</summary>
public sealed class BookmarkStore
{
    [JsonPropertyName("bookmarks")] public List<Bookmark> Bookmarks { get; set; } = [];
    [JsonPropertyName("tours")] public List<Tour> Tours { get; set; } = [];

    [JsonIgnore] public string Path { get; private set; } = "";

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // LF, not this machine's newline. Everything in .atlas/ is committed,
        // and a file written with CRLF on windows shows up as modified in git
        // the moment the app saves it, however little changed
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string PathFor(string repoRoot) =>
        System.IO.Path.Combine(repoRoot, ".atlas", "bookmarks.json");

    public static BookmarkStore Load(string repoRoot)
    {
        var path = PathFor(repoRoot);
        BookmarkStore store;
        try
        {
            store = File.Exists(path)
                ? JsonSerializer.Deserialize<BookmarkStore>(File.ReadAllText(path), Options) ?? new BookmarkStore()
                : new BookmarkStore();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"could not read {path}: {ex.Message}");
            store = new BookmarkStore();
        }
        store.Path = path;
        return store;
    }

    public void Save()
    {
        if (Path.Length == 0) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"could not write {Path}: {ex.Message}");
        }
    }

    public Bookmark? ById(string id) => Bookmarks.FirstOrDefault(b => b.Id == id);

    public Tour? TourContaining(string bookmarkId) =>
        Tours.FirstOrDefault(t => t.Stops.Contains(bookmarkId));

    public static string NewId() => Guid.NewGuid().ToString("n")[..8];
}

public readonly record struct Target(float X, float Y, float S, bool Orphaned);

public static class BookmarkTargets
{
    /// <summary>centre the card when it fits, otherwise pin its left edge:
    /// code starts on the left, so that is the half worth showing.</summary>
    static float CameraX(FileRec f, float vw, float s) =>
        Math.Min(f.X + f.W / 2, f.X + vw / (2 * s) - 12 / s);

    /// <summary>where the camera should end up for a bookmark. anchored
    /// bookmarks are resolved against the current layout, so they still land
    /// correctly after files move, are added or are removed.</summary>
    public static Target Resolve(Scene scene, Bookmark b, float vw, float vh)
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

    /// <summary>capture the current view. if a file sits under the centre of the
    /// screen the bookmark anchors to it, and to the line under the centre.</summary>
    public static Bookmark Capture(Scene scene, string name, float vw, float vh)
    {
        var b = new Bookmark
        {
            Id = BookmarkStore.NewId(),
            Name = name,
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
        // and the bookmark captures that method, with no extra selection step
        int top = (int)((scene.CamY - halfH - f.Y - headerH) / lineH);
        int bottom = (int)((scene.CamY + halfH - f.Y - headerH) / lineH);
        int last = Math.Max(0, f.N - 1);

        b.File = f.P;
        b.Line = Math.Clamp(top, 0, last);
        b.EndLine = Math.Clamp(bottom, b.Line, last);
        return b;
    }
}

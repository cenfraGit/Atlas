using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atlas;

public sealed class BoardItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    /// <summary>"file" (a window onto real source), "note", "shape", "arrow"
    /// or "image" (File names a file in .atlas/images).</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "file";

    [JsonPropertyName("file")] public string? File { get; set; }

    /// <summary>content fingerprint, so the window survives a rename.</summary>
    [JsonPropertyName("key")] public string? Key { get; set; }
    [JsonPropertyName("line")] public int Line { get; set; }
    [JsonPropertyName("endLine")] public int EndLine { get; set; } = -1;
    [JsonPropertyName("text")] public string? Text { get; set; }

    [JsonPropertyName("x")] public float X { get; set; }
    [JsonPropertyName("y")] public float Y { get; set; }
    [JsonPropertyName("w")] public float W { get; set; } = 620;

    /// <summary>shapes and notes set this; a file window derives it.</summary>
    [JsonPropertyName("h")] public float H { get; set; }

    /// <summary>"#rrggbb", or null for the default.</summary>
    [JsonPropertyName("color")] public string? Color { get; set; }

    /// <summary>arrows only: the far end. the near end is X,Y. drawn freely
    /// rather than latched to two items, so an arrow can point at anything.</summary>
    [JsonPropertyName("x2")] public float X2 { get; set; }
    [JsonPropertyName("y2")] public float Y2 { get; set; }

    /// <summary>ids of the items an arrow is tied to, either end. Null means
    /// that end is loose and X/Y (or X2/Y2) is where it is.
    ///
    /// A tied end has no stored position: it is wherever the edge of that
    /// item is now, worked out afresh every frame, which is why dragging a
    /// box updates nothing. The coordinates underneath are whatever they were
    /// when the tie was made and are stale from that moment on - ask
    /// <see cref="Scene.ArrowEnds"/>, never X/Y, for where an arrow is.
    ///
    /// Deleting the item at one end cuts the tie and writes the end's current
    /// position back (<see cref="Scene.Remove"/>), so the arrow stays where it
    /// was drawn instead of snapping back to where it started life.</summary>
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("to")] public string? To { get; set; }

    /// <summary>which of the item's four sides the end is tied to: 0 top,
    /// 1 right, 2 bottom, 3 left. Anything else means one was never chosen,
    /// and the end takes whichever side faces what it points at.</summary>
    [JsonPropertyName("fromSide")] public int FromSide { get; set; } = -1;
    [JsonPropertyName("toSide")] public int ToSide { get; set; } = -1;

    /// <summary>a freehand stroke, as x,y,x,y... in board coordinates. Flat
    /// rather than a list of points because it is the one thing on a board
    /// there can be thousands of, and a pair of floats per point costs three
    /// times as much JSON as two numbers do.
    ///
    /// This is the first item that is not a box. X/Y/W/H are kept in step as
    /// the stroke's bounds, so picking, moving and the rubberband go on
    /// treating every item the same way.</summary>
    [JsonPropertyName("points")] public List<float>? Points { get; set; }

    /// <summary>line width, in board units: the pen for a stroke, the border
    /// for a shape, the shaft for an arrow. One field, because they are one
    /// idea - how thick is the line - and a shape with a separate "border
    /// width" would need [ and ] to mean two different things depending on
    /// what is picked. 0 means the default.</summary>
    [JsonPropertyName("weight")] public float Weight { get; set; }

    /// <summary>a shape's interior. Null and <see cref="NoFill"/> - the
    /// string "none", as SVG spells it - are both empty: one was never
    /// chosen and the other was chosen, and they draw the same.
    /// <see cref="BorderFill"/> is a wash of the border's colour.
    ///
    /// One field rather than a colour plus a "transparent" flag, because the
    /// two could disagree and only one of them could win.</summary>
    [JsonPropertyName("fill")] public string? Fill { get; set; }

    public const string NoFill = "none";

    /// <summary>a wash of the border's own colour. Used to be what null
    /// meant; now null means empty, because a shape is usually drawn round
    /// something and the wash was one more thing between you and it.</summary>
    public const string BorderFill = "border";

    /// <summary>the window this item is pinned over, if it is sitting on
    /// one. A rectangle drawn across a file window is about the code under
    /// it, so it belongs to that code rather than to a spot on the board -
    /// insert lines inside the window and the code moves out from under an
    /// unpinned drawing.
    ///
    /// Null for a window itself, and for anything on bare canvas.</summary>
    [JsonPropertyName("host")] public string? Host { get; set; }

    /// <summary>how far below the anchored line the item sits, in board
    /// units. The line puts it on the right code; this keeps it where it
    /// was between two lines.</summary>
    [JsonPropertyName("dy")] public float Dy { get; set; }

    /// <summary>where the bottom edge sits relative to the *end* of the
    /// declaration the top is anchored to, in lines. Null when there is no
    /// second anchor. Only shapes carry one.
    ///
    /// Without it a rectangle drawn round a method keeps its height while
    /// the method grows, and stops being a rectangle round that method.
    ///
    /// Relative to the declaration's end rather than a fingerprint of the
    /// closing line, because that fingerprint is made of the line and its
    /// neighbours - and the line above a closing brace is exactly what
    /// changes when something is added inside. Roslyn knows where the
    /// declaration ends now; that is the thing to ask.</summary>
    [JsonPropertyName("endOffset")] public int? EndOffset { get; set; }

    /// <summary>the declaration <see cref="EndOffset"/> is measured from the
    /// end of (<see cref="Anchors.CaptureEnd"/>). Null on anything anchored
    /// before this existed, which measured from <see cref="Symbol"/>.
    ///
    /// A file window carries one too, for the last line of its range: a
    /// window cropped to one method used to keep its line count when the
    /// method grew, so the end of the method slid out of the bottom.</summary>
    [JsonPropertyName("endSymbol")] public string? EndSymbol { get; set; }

    /// <summary>and the remainder, so a box that stopped halfway through a
    /// line goes back to halfway through it.</summary>
    [JsonPropertyName("endDy")] public float EndDy { get; set; }

    /// <summary>an anchor into a file: the declaration the line sits in, how
    /// far down that declaration it is, and a fingerprint of the line and
    /// its neighbours. The same three an annotation carries, and resolved by
    /// the same ladder.
    ///
    /// For a file window this is the line its range *starts* at. For
    /// anything pinned over one it is the line the item sits on.
    ///
    /// <c>Line</c> and <c>EndLine</c> are line *numbers*, and a line number
    /// stops meaning the same thing the moment somebody inserts above it -
    /// the window then shows different code in the same place, and anything
    /// drawn over it is pointing at the wrong thing. The anchor is what puts
    /// the range back on the code it was opened on.</summary>
    [JsonPropertyName("symbol")] public string? Symbol { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
    [JsonPropertyName("context")] public string? Context { get; set; }

    /// <summary>a window that carries on from the one above it: the change
    /// view cuts a file where lines were removed, and every piece after the
    /// first is drawn without a header, so the cut reads as the red block
    /// between two runs of one file rather than as a new window. Never
    /// stored, so a piece copied onto a board of your own gets its header.</summary>
    [JsonIgnore] public bool Continued { get; set; }

    /// <summary>type size for a text label, in board units. 0 means the
    /// default. A label is the one thing on a board whose size is the point
    /// of it - a heading over a group of windows is a heading because it is
    /// large - so it is set per item rather than fixed.</summary>
    [JsonPropertyName("size")] public float Size { get; set; }
}

/// <summary>a saved view of a board: one slide of its tour.
///
/// The region that was on screen rather than a zoom level, so playing it
/// in a smaller window frames the same things instead of cropping them.</summary>
public sealed class Stop
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>centre of the region, in board units.</summary>
    [JsonPropertyName("x")] public float X { get; set; }
    [JsonPropertyName("y")] public float Y { get; set; }

    /// <summary>its size, in board units.</summary>
    [JsonPropertyName("w")] public float W { get; set; }
    [JsonPropertyName("h")] public float H { get; set; }

    /// <summary>the view as it is now.</summary>
    public static Stop Of(float camX, float camY, float camS, float vw, float vh) =>
        new() { X = camX, Y = camY, W = vw / camS, H = vh / camS };

    /// <summary>the zoom that fits the whole region into a viewport.</summary>
    public float ScaleFor(float vw, float vh) =>
        Math.Min(vw / Math.Max(1f, W), vh / Math.Max(1f, H));
}

/// <summary>a hand-arranged canvas that references files rather than owning
/// them, so the auto-laid-out map never has to move.</summary>
public sealed class Board
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("items")] public List<BoardItem> Items { get; set; } = [];

    /// <summary>empty means ungrouped. a group exists by being named.</summary>
    [JsonPropertyName("group")] public string Group { get; set; } = "";

    /// <summary>position within its group.</summary>
    [JsonPropertyName("order")] public int Order { get; set; }

    /// <summary>the board's tour, in order. Kept in the board's own file so a
    /// tour travels with the board it walks through.</summary>
    [JsonPropertyName("stops")] public List<Stop> Stops { get; set; } = [];

    [JsonIgnore] public string Path { get; set; } = "";
}

public sealed class BoardStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // LF, not this machine's newline. Everything in .atlas/ is committed,
        // and a file written with CRLF on windows shows up as modified in git
        // the moment the app saves it, however little changed
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public List<Board> Boards { get; } = [];
    public string Dir { get; private set; } = "";

    /// <summary>the order groups are listed in, by name. A group is only a
    /// name on its boards, so the order has to live somewhere of its own:
    /// <c>.atlas/groups.json</c>, beside the boards folder rather than in it,
    /// where it would be read as a board. A group not in the list comes
    /// after the ones that are - ungrouped first, then by name, which is how
    /// every group was ordered before this existed.</summary>
    public List<string> GroupOrder { get; } = [];

    string GroupsPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Dir)!, "groups.json");

    /// <summary>every group that has a board in it, in display order.</summary>
    public List<string> Groups() => Boards.Select(b => b.Group).Distinct()
        .OrderBy(g => GroupOrder.IndexOf(g) is var i && i >= 0 ? i : int.MaxValue)
        .ThenBy(g => g.Length == 0 ? "" : "1" + g, StringComparer.Ordinal)
        .ToList();

    public void SaveGroups()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(GroupsPath)!);
            File.WriteAllText(GroupsPath, JsonSerializer.Serialize(GroupOrder, Options));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"could not write {GroupsPath}: {ex.Message}");
        }
    }

    public static string DirFor(string repoRoot) =>
        System.IO.Path.Combine(repoRoot, ".atlas", "boards");

    public static BoardStore Load(string repoRoot)
    {
        var store = new BoardStore { Dir = DirFor(repoRoot) };
        try
        {
            if (File.Exists(store.GroupsPath) &&
                JsonSerializer.Deserialize<List<string>>(File.ReadAllText(store.GroupsPath), Options) is { } order)
                store.GroupOrder.AddRange(order);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"could not read {store.GroupsPath}: {ex.Message}");
        }
        if (!Directory.Exists(store.Dir)) return store;

        foreach (var path in Directory.EnumerateFiles(store.Dir, "*.json").OrderBy(p => p))
        {
            try
            {
                var b = JsonSerializer.Deserialize<Board>(File.ReadAllText(path), Options);
                if (b is null) continue;
                b.Path = path;
                store.Boards.Add(b);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"could not read {path}: {ex.Message}");
            }
        }
        return store;
    }

    public Board Create(string name) => Create(name, NewId());

    /// <summary>a short random id, for boards and the items on them.</summary>
    public static string NewId() => Guid.NewGuid().ToString("n")[..8];

    /// <summary>create with an id of your own, for the sample boards, whose
    /// ids are fixed so that regenerating them rewrites the same files.
    ///
    /// Setting Id after Create left the path built from the random id the
    /// board no longer had, so every regeneration renamed all three files
    /// and git saw a pile of deletions next to a pile of additions.</summary>
    public Board Create(string name, string id)
    {
        var b = new Board { Id = id, Name = name };
        b.Path = System.IO.Path.Combine(Dir, FileNameFor(name, b.Id));
        Boards.Add(b);
        Save(b);
        return b;
    }

    public void Save(Board b)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(b.Path, JsonSerializer.Serialize(b, Options));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"could not write {b.Path}: {ex.Message}");
        }
    }

    public void Rename(Board b, string name)
    {
        var old = b.Path;
        b.Name = name;
        b.Path = System.IO.Path.Combine(Dir, FileNameFor(name, b.Id));
        Save(b);
        if (!string.Equals(old, b.Path, StringComparison.OrdinalIgnoreCase))
            try { File.Delete(old); } catch { }
    }

    public void Delete(Board b)
    {
        Boards.Remove(b);
        try { File.Delete(b.Path); } catch { }
    }

    /// <summary>every board holding a window onto this file. drives the hover
    /// popup on the map.</summary>
    public List<Board> Containing(string filePath) =>
        Boards.Where(b => b.Items.Any(i => i.Kind == "file" && i.File == filePath)).ToList();

    /// <summary>readable file names keep board diffs legible in git; the id is
    /// only appended when two boards would otherwise collide.</summary>
    static string FileNameFor(string name, string id)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length == 0) slug = "board";
        if (slug.Length > 48) slug = slug[..48].TrimEnd('-');
        return $"{slug}-{id}.json";
    }
}

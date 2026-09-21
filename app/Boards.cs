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

    /// <summary>a shape's interior. Null means a wash of the border colour,
    /// which is what a shape looked like before there was a choice.
    /// <see cref="NoFill"/> - the string "none", as SVG spells it - means
    /// genuinely empty.
    ///
    /// One field rather than a colour plus a "transparent" flag, because the
    /// two could disagree and only one of them could win.</summary>
    [JsonPropertyName("fill")] public string? Fill { get; set; }

    public const string NoFill = "none";

    /// <summary>type size for a text label, in board units. 0 means the
    /// default. A label is the one thing on a board whose size is the point
    /// of it - a heading over a group of windows is a heading because it is
    /// large - so it is set per item rather than fixed.</summary>
    [JsonPropertyName("size")] public float Size { get; set; }
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

    public static string DirFor(string repoRoot) =>
        System.IO.Path.Combine(repoRoot, ".atlas", "boards");

    public static BoardStore Load(string repoRoot)
    {
        var store = new BoardStore { Dir = DirFor(repoRoot) };
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

    public Board Create(string name) => Create(name, BookmarkStore.NewId());

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

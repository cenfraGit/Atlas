using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace Atlas;

public sealed class FileRec
{
    [JsonPropertyName("p")] public string P { get; set; } = "";
    [JsonPropertyName("n")] public int N { get; set; }
    [JsonPropertyName("d")] public int[] D { get; set; } = [];
    [JsonPropertyName("x")] public float X { get; set; }
    [JsonPropertyName("y")] public float Y { get; set; }
    [JsonPropertyName("w")] public float W { get; set; }
    [JsonPropertyName("h")] public float H { get; set; }
}

public sealed class Folder
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("x")] public float X { get; set; }
    [JsonPropertyName("y")] public float Y { get; set; }
    [JsonPropertyName("w")] public float W { get; set; }
    [JsonPropertyName("h")] public float H { get; set; }
}

public sealed class WorldSize
{
    [JsonPropertyName("w")] public float W { get; set; }
    [JsonPropertyName("h")] public float H { get; set; }
}

public sealed class Scan
{
    [JsonPropertyName("root")] public string Root { get; set; } = "";
    [JsonPropertyName("lineH")] public float LineH { get; set; }
    [JsonPropertyName("headerH")] public float HeaderH { get; set; }
    [JsonPropertyName("world")] public WorldSize World { get; set; } = new();
    [JsonPropertyName("folders")] public List<Folder> Folders { get; set; } = [];
    [JsonPropertyName("files")] public List<FileRec> Files { get; set; } = [];

    /// <summary>files the scan passed over: too large, or not text. Reported
    /// rather than swallowed - a map that quietly omits part of a repo is
    /// worse than one that shows something ugly.</summary>
    [JsonPropertyName("skipped")] public int Skipped { get; set; }

    [JsonPropertyName("showingHidden")] public bool ShowingHidden { get; set; }
}

/// <summary>what the scanner counts as part of the repo.</summary>
public sealed record ScanOptions(bool ShowHidden = false)
{
    /// <summary>true when the repo's own .gitignore says a path is not part
    /// of its source. Null when there is no repo to ask, which is a normal
    /// way to use Atlas. Bypassed entirely by ShowHidden - "show me
    /// everything" has to mean everything.</summary>
    public Func<string, bool>? Ignored { get; init; }

    public bool IsIgnored(string relPath) =>
        !ShowHidden && Ignored is not null && Ignored(relPath);

    public static readonly ScanOptions Default = new();
}

/// <summary>same level-of-detail rules and colours as the web prototype.</summary>
public sealed class Scene : IDisposable
{
    const float T_CARD = 0.055f, T_BARS = 0.45f, T_TEXT = 1.9f;

    /// <summary>which level of detail a zoom falls in: 0 folders, 1 cards,
    /// 2 bars, 3 text. Pulled out of the draw loop so the thresholds can be
    /// checked without a window.</summary>
    public static int TierFor(float camS) =>
        camS < T_CARD ? 0 : camS < T_BARS ? 1 : camS < T_TEXT ? 2 : 3;

    static readonly SKColor Bg = new(0x04, 0x07, 0x0f);
    // a board is a different place: indigo instead of the map's blue black
    static readonly SKColor BoardBg = new(0x16, 0x10, 0x28);
    static readonly SKColor CardBg = new(0x08, 0x13, 0x20);
    static readonly SKColor HeaderBg = new(0x0f, 0x23, 0x34);
    static readonly SKColor FolderBg = new(0x07, 0x12, 0x1c);
    static readonly SKColor FolderEdge = new(0x1b, 0x4c, 0x66);
    static readonly SKColor LabelCol = new(0x7f, 0xd8, 0xf0);
    static readonly SKColor CodeCol = new(0x9f, 0xd4, 0xea);

    static readonly SKColor[] KindColor =
    [
        SKColors.Transparent,
        new(0x2b, 0x44, 0x57, 191),
        new(0xff, 0xd1, 0x66, 255),
        new(0x3f, 0xb9, 0x8f, 217),
        new(0x3a, 0x7f, 0xa8, 178),
    ];

    public Scan Data;
    public float CamX, CamY, CamS = 0.05f;
    public int VisibleCards, BuiltThisFrame, Tier;
    public bool ShowFolders = true;

    /// <summary>lines to mark after arriving at a bookmarked region.</summary>
    public (int File, int From, int To)? Highlight;

    /// <summary>when set, the canvas shows this board instead of the map.</summary>
    public Board? ActiveBoard;

    /// <summary>items picked on a board. a set, because miro-style editing
    /// needs more than one thing at a time.</summary>
    public readonly HashSet<string> Picked = [];

    /// <summary>the rubberband being dragged, in board coordinates, and how
    /// faded it is on the way out.</summary>
    public SKRect? Rubberband;
    public float RubberbandFade = 1f;

    /// <summary>grid spacing while editing, 0 for no grid.</summary>
    public float Grid;

    /// <summary>the active board is generated and cannot be edited: it belongs
    /// to a commit, not to the repo. Set for the gathered change view.</summary>
    public bool BoardReadOnly;

    /// <summary>files picked on the map. editing there is selection only: the
    /// map is never rearranged, so the only thing a pick can do is go on a
    /// board.</summary>
    public readonly HashSet<int> PickedFiles = [];

    /// <summary>annotations for the scanned repo, re-anchored per file as its
    /// text loads.</summary>
    public AnnotationStore? Notes;

    /// <summary>when reviewing a commit, files come from its tree, not disk.</summary>
    public Func<string, string[]?>? TextSource;

    /// <summary>true while the map shows a commit rather than the working tree.</summary>
    public bool OnSnapshot { get; private set; }

    Scan? _liveData;

    /// <summary>when set, the map is showing a pull request or a commit.</summary>
    public ChangeSet? Review;

    /// <summary>line under the cursor while reading code.</summary>
    public (int File, int Line)? HoverLine;

    /// <summary>lines picked with the mouse; what the context menu acts on.</summary>
    public (int File, int From, int To)? Selection;
    readonly ConcurrentDictionary<string, List<(Annotation A, Anchor R)>> _anchored = new();

    int[] _folderOf = [];
    readonly Dictionary<string, int> _pathIndex = new(StringComparer.Ordinal);

    // timing lives here because the draw happens on avalonia's render thread
    /// <summary>called when work finished off-frame (a file loaded, chunks still
    /// queued) and the view must draw again. a flag is not enough: nothing is
    /// watching one between frames.</summary>
    public Action? RequestRedraw;
    public long Frames;
    public bool Recording;
    public readonly List<double> Samples = [];

    readonly SKTypeface _mono = Mono();

    /// <summary>the first fixed width face this machine actually has.</summary>
    static SKTypeface Mono()
    {
        foreach (var name in new[]
                 {
                     "Consolas", "Cascadia Mono", "DejaVu Sans Mono", "Menlo",
                     "Liberation Mono", "Courier New", "monospace",
                 })
            if (SKTypeface.FromFamilyName(name) is { } found &&
                found.FamilyName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return found;
        return SKTypeface.Default;
    }
    readonly SKPaint _hover = new() { Color = new SKColor(0x7f, 0xd8, 0xf0, 28), IsAntialias = false };
    readonly SKPaint _pick = new() { Color = new SKColor(0x5f, 0xd3, 0xf3, 52), IsAntialias = false };
    readonly SKPaint _pickEdge = new() { Color = new SKColor(0x5f, 0xd3, 0xf3), IsAntialias = false };
    readonly SKPaint _veil = new() { Color = new SKColor(0x04, 0x07, 0x0f, 165), IsAntialias = false };
    readonly SKPaint _dim = new() { Color = new SKColor(0x04, 0x07, 0x0f, 190), IsAntialias = false };
    readonly SKPaint _band = new() { Color = new SKColor(0xff, 0xd1, 0x66, 28), IsAntialias = false };
    readonly SKPaint _bandEdge = new() { Color = new SKColor(0xff, 0xd1, 0x66, 220), IsAntialias = false };
    readonly Dictionary<int, SKPicture> _bars = [];
    readonly ConcurrentDictionary<string, string[]> _text = [];
    readonly ConcurrentDictionary<string, Run[][]?> _runs = [];
    readonly Highlighter _hl = new(CodeCol);
    float _charW;
    readonly HashSet<string> _loading = [];
    readonly List<int> _queue = [];
    SKPicture? _folderPic, _cardPic;

    public Scene(Scan data)
    {
        Data = data;
        Rebuild();
    }

    public int ChunksBuilt => _bars.Count;

    static SKColor FolderHue(int i, byte sat, byte light) =>
        SKColor.FromHsl((float)(i * 0.6180339887 % 1.0 * 360.0), sat, light);

    /// <summary>index of a file by repo-relative path, or -1.</summary>
    public int IndexOfPath(string path) => _pathIndex.GetValueOrDefault(path, -1);

    /// <summary>find a referenced file even after it was renamed or moved.
    /// the path is tried first, then a unique file of the same name, then the
    /// content fingerprint. returns -1 and leaves the caller to report it.</summary>
    public int ResolveFile(string path, string? key)
    {
        int direct = IndexOfPath(path);
        if (direct >= 0) return direct;

        var name = path[(path.LastIndexOf('/') + 1)..];
        var sameName = new List<int>();
        for (int i = 0; i < Data.Files.Count; i++)
        {
            var p = Data.Files[i].P;
            if (p.EndsWith(name, StringComparison.Ordinal) &&
                (p.Length == name.Length || p[^(name.Length + 1)] == '/'))
                sameName.Add(i);
        }
        if (sameName.Count == 1) return sameName[0];

        if (string.IsNullOrEmpty(key)) return sameName.Count > 0 ? sameName[0] : -1;

        // a rename: look for the same content. only files that could plausibly
        // be it are opened, so a miss costs a handful of reads, not 1600
        var ext = System.IO.Path.GetExtension(name);
        foreach (var i in sameName.Concat(Candidates(ext, path)))
        {
            var lines = LinesOf(Data.Files[i].P);
            if (lines is null)
            {
                var full = System.IO.Path.Combine(Data.Root,
                    Data.Files[i].P.Replace('/', System.IO.Path.DirectorySeparatorChar));
                var computed = TextSource is not null
                    ? TextSource(Data.Files[i].P) is { } fromTree ? FileKeys.Of(fromTree) : null
                    : FileKeys.OfFile(full);
                if (computed == key) return i;
                continue;
            }
            if (FileKeys.Of(lines) == key) return i;
        }
        return -1;
    }

    IEnumerable<int> Candidates(string ext, string path)
    {
        for (int i = 0; i < Data.Files.Count; i++)
        {
            var f = Data.Files[i];
            if (f.P == path) continue;
            if (!f.P.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) continue;
            yield return i;
        }
    }

    void MapFilesToFolders()
    {
        _pathIndex.Clear();
        for (int i = 0; i < Data.Files.Count; i++) _pathIndex[Data.Files[i].P] = i;

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Data.Folders.Count; i++) index[Data.Folders[i].Name] = i;

        _folderOf = new int[Data.Files.Count];
        for (int i = 0; i < Data.Files.Count; i++)
        {
            var p = Data.Files[i].P;
            int cut = p.LastIndexOf('/');
            var dir = cut < 0 ? "." : p[..cut];
            _folderOf[i] = index.TryGetValue(dir, out var d) ? d : 0;
        }
    }

    /// <summary>show the repo as it was at a commit. annotations are left alone:
    /// they belong to the working tree, not to somebody else's branch.</summary>
    public void ShowSnapshot(Scan data, Func<string, string[]?> textSource)
    {
        _liveData ??= Data;
        Data = data;
        TextSource = textSource;
        OnSnapshot = true;
        DropCaches();
        Rebuild();
    }

    /// <summary>swap the working-tree scan for another of the same repo, after
    /// a rescan. Everything cached is keyed by file index or path, and both
    /// have just changed, so all of it goes.</summary>
    public void ShowScan(Scan data)
    {
        if (OnSnapshot) return;     // a commit's tree is not ours to replace
        Data = data;
        Selection = null;
        HoverLine = null;
        Highlight = null;
        PickedFiles.Clear();
        DropCaches();
        Rebuild();
        EnsureAllAnchored();
    }

    public void ShowLive()
    {
        if (_liveData is null) return;
        Data = _liveData;
        _liveData = null;
        TextSource = null;
        OnSnapshot = false;
        DropCaches();
        Rebuild();
    }

    void DropCaches()
    {
        foreach (var p in _bars.Values) p.Dispose();
        _bars.Clear();
        _text.Clear();
        _runs.Clear();
        _anchored.Clear();
        _loading.Clear();
        Selection = null;
        HoverLine = null;
        Highlight = null;
    }

    public void Rebuild()
    {
        MapFilesToFolders();
        foreach (var p in _bars.Values) p.Dispose();
        _bars.Clear();
        _folderPic?.Dispose();
        _cardPic?.Dispose();

        var bounds = new SKRect(0, 0, Data.World.W, Data.World.H);

        var rec = new SKPictureRecorder();
        var c = rec.BeginRecording(bounds);
        using (var fill = new SKPaint { Color = FolderBg, IsAntialias = false })
        using (var edge = new SKPaint { IsStroke = true, StrokeWidth = 0, IsAntialias = false })
        using (var lab = new SKPaint { Typeface = _mono, IsAntialias = true })
        {
            for (int i = 0; i < Data.Folders.Count; i++)
            {
                var d = Data.Folders[i];
                var r = new SKRect(d.X - 10, d.Y - 26, d.X + d.W + 10, d.Y + d.H + 10);
                c.DrawRect(r, fill);
                edge.Color = FolderHue(i, 55, 40);
                c.DrawRect(r, edge);
            }

        }
        _folderPic = rec.EndRecording();

        rec = new SKPictureRecorder();
        c = rec.BeginRecording(bounds);
        using (var body = new SKPaint { Color = CardBg, IsAntialias = false })
        using (var head = new SKPaint { IsAntialias = false })
        {
            foreach (var f in Data.Files) c.DrawRect(f.X, f.Y, f.W, f.H, body);
            for (int i = 0; i < Data.Files.Count; i++)
            {
                var f = Data.Files[i];
                head.Color = FolderHue(_folderOf[i], 40, 20);
                c.DrawRect(f.X, f.Y, f.W, Data.HeaderH, head);
            }
        }
        _cardPic = rec.EndRecording();
    }

    SKPicture BuildBars(int i)
    {
        var f = Data.Files[i];
        var rec = new SKPictureRecorder();
        var c = rec.BeginRecording(new SKRect(0, 0, f.W, f.H));
        int maxLines = Math.Min(f.N, (int)((f.H - Data.HeaderH) / Data.LineH));
        float innerW = f.W - 12;

        for (int k = 1; k < KindColor.Length; k++)
        {
            using var paint = new SKPaint { Color = KindColor[k], IsAntialias = false };
            for (int li = 0; li < maxLines; li++)
            {
                if (f.D[li * 3 + 2] != k) continue;
                int indent = f.D[li * 3], len = f.D[li * 3 + 1];
                if (len == 0) continue;
                float x = 6 + indent / 120f * innerW;
                float w = Math.Max(0.8f, len / 120f * innerW);
                c.DrawRect(x, Data.HeaderH + li * Data.LineH, Math.Min(w, innerW - (x - 6)), Data.LineH - 0.9f, paint);
            }
        }
        var pic = rec.EndRecording();
        _bars[i] = pic;
        return pic;
    }

    string[]? TextFor(FileRec f)
    {
        if (_text.TryGetValue(f.P, out var t)) return t;
        lock (_loading)
        {
            if (!_loading.Add(f.P)) return null;
        }
        var path = Path.Combine(Data.Root, f.P.Replace('/', Path.DirectorySeparatorChar));
        var source = TextSource;
        Task.Run(() =>
        {
            string[] src;
            if (source is not null)
            {
                var fromCommit = source(f.P);
                if (fromCommit is null) { _text[f.P] = []; return; }
                src = fromCommit;
            }
            else
            {
                try { src = File.ReadAllLines(path); }
                catch { _text[f.P] = []; return; }
            }
            // expand tabs before tokenising so run columns match what we draw
            for (int i = 0; i < src.Length; i++)
                if (src[i].IndexOf('	') >= 0) src[i] = src[i].Replace("	", "    ");
            try { _runs[f.P] = _hl.Tokenize(src, Path.GetExtension(f.P)); }
            catch { _runs[f.P] = null; }
            _text[f.P] = src;
            Reanchor(f.P, path, src);
            RequestRedraw?.Invoke();
        });
        return null;
    }

    public void Reanchor(string relPath, string fullPath, string[] lines)
    {
        if (Notes is null) return;
        var list = new List<(Annotation, Anchor)>();
        foreach (var a in Notes.For(relPath))
            list.Add((a, Anchors.Resolve(a, fullPath, lines)));
        list.Sort((x, y) => x.Item2.Line.CompareTo(y.Item2.Line));
        _anchored[relPath] = list;
    }

    /// <summary>read a file now, if needed, so its anchors are known. the
    /// annotation list is meaningless for files that were never opened: every
    /// one of them would look healthy.</summary>
    public void EnsureAnchored(string relPath)
    {
        if (_anchored.ContainsKey(relPath)) return;
        if (IndexOfPath(relPath) < 0 && Notes is not null)
        {
            // the file moved or was renamed: repoint the notes at it
            var first = Notes.For(relPath).FirstOrDefault();
            int moved = first is null ? -1 : ResolveFile(relPath, first.Key);
            if (moved >= 0 && Data.Files[moved].P != relPath)
            {
                var to = Data.Files[moved].P;
                foreach (var a in Notes.For(relPath).ToList()) a.File = to;
                Notes.Save();
                EnsureAnchored(to);
                return;
            }
        }
        var full = Path.Combine(Data.Root, relPath.Replace('/', Path.DirectorySeparatorChar));
        string[] lines;
        if (_text.TryGetValue(relPath, out var cached)) lines = cached;
        else
        {
            try { lines = File.ReadAllLines(full); }
            catch { return; }
            var tab = TabChar.ToString();
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].IndexOf(TabChar) >= 0) lines[i] = lines[i].Replace(tab, "    ");
            _text[relPath] = lines;
        }
        Reanchor(relPath, full, lines);
    }

    /// <summary>every file that has an annotation.</summary>
    public void EnsureAllAnchored()
    {
        if (Notes is null) return;
        foreach (var file in Notes.Annotations.Select(a => a.File).Distinct())
            EnsureAnchored(file);
    }

    /// <summary>re-anchor from the copy of the file we already hold.</summary>
    public void Reanchor(string relPath)
    {
        if (!_text.TryGetValue(relPath, out var lines)) return;
        Reanchor(relPath, Path.Combine(Data.Root, relPath.Replace('/', Path.DirectorySeparatorChar)), lines);
    }

    /// <summary>the file's text, if it has been read yet.</summary>
    public string[]? LinesOf(string relPath) => _text.GetValueOrDefault(relPath);

    /// <summary>the annotations to draw on this file, here.
    ///
    /// Global ones everywhere; a board's own only on that board. Filtered on
    /// the way out rather than at anchor time, so changing a note's scope
    /// costs nothing and needs no re-anchoring - where a note is attached and
    /// where it is shown are different questions.</summary>
    public IReadOnlyList<(Annotation A, Anchor R)> AnchorsFor(string relPath)
    {
        var all = AllAnchorsFor(relPath);
        if (all.Count == 0) return all;

        var board = ActiveBoard?.Id;
        // nothing is local to a generated view, and the map is global only
        if (board is null || BoardReadOnly)
            return all.All(x => x.A.Global) ? all : all.Where(x => x.A.Global).ToList();

        return all.All(x => x.A.Global || x.A.Board == board)
            ? all
            : all.Where(x => x.A.Global || x.A.Board == board).ToList();
    }

    /// <summary>every annotation on the file, whatever its scope. What the
    /// list panel works from: you cannot repair a note you cannot see.</summary>
    public IReadOnlyList<(Annotation A, Anchor R)> AllAnchorsFor(string relPath) =>
        _anchored.TryGetValue(relPath, out var list) ? list : [];

    public static SKColor ColorFor(AnchorKind kind) => kind switch
    {
        AnchorKind.Symbol => new SKColor(0xff, 0xd1, 0x66),
        AnchorKind.Context => new SKColor(0x6f, 0xc3, 0xe8),
        AnchorKind.Line => new SKColor(0x9f, 0xd4, 0xea),
        AnchorKind.Drifted => new SKColor(0xff, 0x9a, 0x52),
        _ => new SKColor(0xe0, 0x5b, 0x5b),
    };

    public void Draw(SKCanvas canvas, float vw, float vh)
    {
        var sw = Stopwatch.StartNew();
        canvas.Clear(ActiveBoard is not null ? BoardBg : Bg);

        if (ActiveBoard is not null)
        {
            DrawBoard(canvas, vw, vh);
            canvas.Flush();
            Frames++;
            lock (Samples) { if (Samples.Count < 400) Samples.Add(sw.Elapsed.TotalMilliseconds); }
            return;
        }

        Tier = TierFor(CamS);

        canvas.Save();
        canvas.Translate(vw / 2 - CamX * CamS, vh / 2 - CamY * CamS);
        canvas.Scale(CamS);

        float hw = vw / 2 / CamS, hh = vh / 2 / CamS;
        float x0 = CamX - hw, y0 = CamY - hh, x1 = CamX + hw, y1 = CamY + hh;

        DrawGrid(canvas, x0, y0, x1, y1);
        if (ShowFolders) canvas.DrawPicture(_folderPic);
        if (ShowFolders) DrawFolderLabels(canvas, vw, x0, y0, x1, y1);
        if (Tier >= 1) canvas.DrawPicture(_cardPic);
        if (Review is not null && Tier < 2)
        {
            canvas.DrawRect(0, 0, Data.World.W, Data.World.H, _veil);
            DrawReviewBlocks(canvas);
        }

        VisibleCards = 0;
        BuiltThisFrame = 0;
        _queue.Clear();

        if (Tier >= 2)
        {
            using var label = new SKPaint { Color = LabelCol, Typeface = _mono, TextSize = 11, IsAntialias = true };
            using var code = new SKPaint { Color = CodeCol, Typeface = _mono, TextSize = Data.LineH * 0.78f, IsAntialias = true };
            if (_charW == 0) _charW = code.MeasureText("0");

            var files = Data.Files;
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (f.X > x1 || f.X + f.W < x0 || f.Y > y1 || f.Y + f.H < y0) continue;
                VisibleCards++;

                canvas.Save();
                canvas.Translate(f.X, f.Y);

                if (Highlight is { } hl && hl.File == i)
                {
                    float bandY = Data.HeaderH + hl.From * Data.LineH;
                    float bandH = (hl.To - hl.From + 1) * Data.LineH;
                    canvas.DrawRect(0, bandY, f.W, bandH, _band);
                    canvas.DrawRect(0, bandY, 2.5f, bandH, _bandEdge);
                }

                // a card owns its rectangle: nothing it draws may leave it.
                // the bars picture clamps itself to the card height, but the
                // text tier did not, so a file taller than its card wrote code
                // over its neighbours all the way down the column
                canvas.Save();
                canvas.ClipRect(new SKRect(0, 0, f.W, f.H));

                if (Tier == 3)
                {
                    int from = Math.Max(0, (int)((y0 - f.Y - Data.HeaderH) / Data.LineH) - 2);
                    int to = (int)((y1 - f.Y - Data.HeaderH) / Data.LineH) + 2;
                    if (!DrawCode(canvas, i, from, to, code) && _bars.TryGetValue(i, out var bp))
                        canvas.DrawPicture(bp);
                }
                else
                {
                    if (_bars.TryGetValue(i, out var pic)) canvas.DrawPicture(pic);
                    else _queue.Add(i);
                }

                canvas.Restore();

                if (Review is not null)
                {
                    if (Review.ByPath.ContainsKey(f.P)) DrawReviewLines(canvas, f);
                    else canvas.DrawRect(0, 0, f.W, f.H, _dim);
                }

                DrawPicks(canvas, f, i);
                DrawAnnotations(canvas, f, vw, y0, y1);

                var name = f.P[(f.P.LastIndexOf('/') + 1)..];
                // close in, the file name is a label, not a billboard
                label.TextSize = Tier == 3 ? Data.LineH * 1.3f : 11;
                canvas.DrawText(name, 6, Tier == 3 ? Data.HeaderH - 4 : 15, label);
                canvas.Restore();
            }

            // amortise construction so a fast zoom never blocks a frame
            for (int q = 0; q < _queue.Count && BuiltThisFrame < 14; q++, BuiltThisFrame++)
                BuildBars(_queue[q]);
            if (BuiltThisFrame > 0) RequestRedraw?.Invoke();
        }

        // outlines go on top of the code, never under it
        if (Review is not null && Tier >= 2) DrawReviewOutlines(canvas, x0, y0, x1, y1);
        DrawPickedFiles(canvas, x0, y0, x1, y1);
        DrawRubberband(canvas);

        canvas.Restore();
        canvas.Flush();
        Frames++;
        lock (Samples) { if (Samples.Count < 400) Samples.Add(sw.Elapsed.TotalMilliseconds); }
    }

    /// <summary>file and line under a world point, or null if not over code.</summary>
    public (int File, int Line)? LineAt(float wx, float wy)
    {
        int i = FileAt(wx, wy);
        if (i < 0) return null;
        var f = Data.Files[i];
        int line = (int)((wy - f.Y - Data.HeaderH) / Data.LineH);
        if (line < 0) return null;                       // the header, not a line
        return (i, Math.Clamp(line, 0, Math.Max(0, f.N - 1)));
    }

    /// <summary>index of the file under a world point, or -1.</summary>
    public int FileAt(float wx, float wy)
    {
        var files = Data.Files;
        for (int i = 0; i < files.Count; i++)
        {
            var f = files[i];
            if (wx >= f.X && wx <= f.X + f.W && wy >= f.Y && wy <= f.Y + f.H) return i;
        }
        return -1;
    }

    /// <summary>draws source lines [from, to) of a file at the current canvas
    /// origin, in card-local coordinates. false when the text is not loaded yet.
    /// shared by the map and by a board's file windows.</summary>
    bool DrawCode(SKCanvas canvas, int i, int from, int to, SKPaint code)
    {
        var f = Data.Files[i];
        var lines = TextFor(f);
        if (lines is not { Length: > 0 }) return false;

        _runs.TryGetValue(f.P, out var runs);
        from = Math.Max(0, from);
        to = Math.Min(lines.Length, to);

        for (int li = from; li < to; li++)
        {
            var s = lines[li];
            if (s.Length == 0) continue;
            float baseline = Data.HeaderH + (li + 1) * Data.LineH - 0.6f;
            var lineRuns = runs?[li];
            if (lineRuns is null || lineRuns.Length == 0)
            {
                code.Color = CodeCol;
                canvas.DrawText(s.Length > 160 ? s[..160] : s, 6, baseline, code);
                continue;
            }
            foreach (var r in lineRuns)
            {
                if (r.Start >= 160) break;
                int runEnd = Math.Min(r.End, 160);
                code.Color = r.Color;
                canvas.DrawText(s[r.Start..runEnd], 6 + r.Start * _charW, baseline, code);
            }
        }
        return true;
    }

    /// <summary>folder names, sized in screen terms so they stay readable at any
    /// zoom, and skipped when the folder is too small to hold the text.</summary>
    void DrawFolderLabels(SKCanvas canvas, float vw, float x0, float y0, float x1, float y1)
    {
        float sc = 1f / CamS;
        float size = Math.Clamp(vw / 90f, 13f, 22f) * sc;
        using var lab = new SKPaint { Typeface = _mono, TextSize = size, IsAntialias = true };

        // folders are shelf packed, so a label may spill into the gap beside
        // it. keep the right edge per row and drop whatever would collide -
        // a readable subset beats a complete but unreadable one
        var rowRight = new Dictionary<int, float>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < Data.Folders.Count; i++)
        {
            var d = Data.Folders[i];
            if (d.X > x1 || d.X + d.W < x0 || d.Y > y1 || d.Y + d.H < y0) continue;

            // "Forms" is useless: every project has one. show as much of the
            // path as fits, dropping segments from the left
            var shown = Shorten(d.Name, lab, d.W * 2.2f);
            if (shown is null) continue;

            // three folders all reading "Pages" name nothing. a repeat is
            // allowed to run wider so it can say which Pages it is
            if (!used.Add(shown) && Shorten(d.Name, lab, d.W * 6f) is { } longer)
            {
                shown = longer;
                used.Add(shown);
            }

            float wide = lab.MeasureText(shown);
            int row = (int)(d.Y / 64);
            if (rowRight.TryGetValue(row, out var right) && d.X < right + 26 * sc) continue;
            rowRight[row] = d.X + wide;

            // a label belongs above its folder, except when that would put
            // it off the top of the window, where it is no label at all
            float ty = Math.Max(d.Y - 30 * sc, y0 + size * 1.2f);
            using (var chip = new SKPaint { Color = new SKColor(0x04, 0x07, 0x0f, 226), IsAntialias = false })
                canvas.DrawRect(d.X - 6 * sc, ty - size * 0.95f, wide + 12 * sc, size * 1.35f, chip);

            lab.Color = Review is not null
                ? new SKColor(0x4a, 0x5a, 0x66)          // reviewing: stay out of the way
                : FolderHue(i, 55, 68);
            canvas.DrawText(shown, d.X, ty, lab);
        }
    }

    /// <summary>longest tail of the path that fits, or null.</summary>
    static string? Shorten(string path, SKPaint paint, float max)
    {
        var parts = path.Split('/');
        int skip = parts[0] is "src" or "source" or "lib" ? 1 : 0;
        for (int from = skip; from < parts.Length; from++)
        {
            var text = string.Join("/", parts.Skip(from));
            if (paint.MeasureText(text) <= max) return text;
        }
        return null;
    }

    static readonly SKColor AddCol = new(0x3f, 0xb9, 0x6a);
    static readonly SKColor DelCol = new(0xd9, 0x5c, 0x5c);

    static SKColor ChurnColor(FileChange c)
    {
        if (c.Removed == 0) return AddCol;
        if (c.Added == 0) return DelCol;
        float share = c.Added / (float)(c.Added + c.Removed);
        return new SKColor(
            (byte)(DelCol.Red + (AddCol.Red - DelCol.Red) * share),
            (byte)(DelCol.Green + (AddCol.Green - DelCol.Green) * share),
            (byte)(DelCol.Blue + (AddCol.Blue - DelCol.Blue) * share));
    }

    /// <summary>runs of changed lines, so two hundred lines changed in one
    /// place is one band rather than two hundred rectangles - and so a band
    /// can be given a floor without a hundred floors overlapping into a
    /// solid block.
    ///
    /// Lines a couple apart are one run: a diff that changed every other
    /// line of a method changed that method, and drawing the gaps is noise
    /// at any zoom where you cannot read them anyway.</summary>
    public static List<(int From, int To)> Runs(IEnumerable<int> lines, int gap = 2)
    {
        var runs = new List<(int, int)>();
        var sorted = lines.Distinct().Order().ToList();
        if (sorted.Count == 0) return runs;

        int from = sorted[0], prev = sorted[0];
        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] - prev <= gap + 1) { prev = sorted[i]; continue; }
            runs.Add((from, prev));
            from = prev = sorted[i];
        }
        runs.Add((from, prev));
        return runs;
    }

    /// <summary>everything of a file's content went, so the whole card is
    /// the change. A file git actually deleted has no card to draw on - the
    /// scan is of what is there now - so this is the emptied case.</summary>
    static bool WhollyRemoved(FileChange change, FileRec f) =>
        change.Added == 0 && change.Removed > 0 && change.Removed >= f.N;

    /// <summary>zoomed out, the changed lines of a file glow and the rest of
    /// it does not.
    ///
    /// This used to paint the whole card in one colour, which said "a lot
    /// changed here" about a two line fix in a long file - the file was
    /// large, so the block was large, and size on the map means size of
    /// file rather than size of change. Now the card is a dim silhouette
    /// saying "touched" and the light is only where the diff is.
    ///
    /// Everything is floored to a few pixels: at map zoom a line is a
    /// fraction of a pixel and a real change would flicker in and out as
    /// you move.</summary>
    void DrawReviewBlocks(SKCanvas canvas)
    {
        float min = 9f / CamS;
        float minRun = 3.5f / CamS;
        using var silhouette = new SKPaint { IsAntialias = false };
        using var fill = new SKPaint { IsAntialias = false };
        using var glow = new SKPaint
        {
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 7f / CamS),
        };

        foreach (var change in Review!.Files)
        {
            int i = IndexOfPath(change.Path);
            if (i < 0) continue;                  // changed a file the scan skipped
            var f = Data.Files[i];
            float w = Math.Max(f.W, min), h = Math.Max(f.H, min);
            float x = f.X + (f.W - w) / 2, y = f.Y + (f.H - h) / 2;
            var churn = ChurnColor(change);

            // the card, dim. It still has to be findable: "which files did
            // this commit touch" is the first question, and an unlit card
            // among hundreds is not an answer
            silhouette.Color = churn.WithAlpha(46);
            canvas.DrawRect(x, y, w, h, silhouette);

            if (WhollyRemoved(change, f))
            {
                glow.Color = DelCol.WithAlpha(120);
                canvas.DrawRect(x - min * 0.35f, y - min * 0.35f, w + min * 0.7f, h + min * 0.7f, glow);
                fill.Color = DelCol;
                canvas.DrawRect(x, y, w, h, fill);
                continue;
            }

            // a card forced up to the minimum size has to stretch its line
            // positions with it, or every band lands in the top corner
            float k = h / Math.Max(1f, f.H);
            float Top(int line) => y + (Data.HeaderH + line * Data.LineH) * k;

            void Bands(IEnumerable<int> lines, SKColor col, bool thin)
            {
                foreach (var (a, b) in Runs(lines))
                {
                    float top = Top(a);
                    float bottom = thin ? top : Top(b) + Data.LineH * k;
                    if (bottom - top < minRun) bottom = top + minRun;

                    glow.Color = col.WithAlpha(120);
                    canvas.DrawRect(x - min * 0.3f, top - minRun, w + min * 0.6f,
                        bottom - top + minRun * 2, glow);
                    fill.Color = col;
                    canvas.DrawRect(x, top, w, bottom - top, fill);
                }
            }

            // removals last: they are points rather than spans, and a
            // deletion inside a block of additions has to stay visible
            Bands(change.AddedLines, AddCol, thin: false);
            Bands(change.RemovedAt, DelCol, thin: true);
        }
    }

    /// <summary>close in the code is readable, so a changed file gets an outline
    /// rather than a wash of colour over it.</summary>
    void DrawReviewOutlines(SKCanvas canvas, float x0, float y0, float x1, float y1)
    {
        using var edge = new SKPaint { IsStroke = true, StrokeWidth = 0, IsAntialias = false };

        foreach (var change in Review!.Files)
        {
            int i = IndexOfPath(change.Path);
            if (i < 0) continue;
            var f = Data.Files[i];
            if (f.X > x1 || f.X + f.W < x0 || f.Y > y1 || f.Y + f.H < y0) continue;
            edge.Color = ChurnColor(change);
            canvas.DrawRect(f.X, f.Y, f.W, f.H, edge);
        }
    }

    /// <summary>added and removed lines inside one card.
    ///
    /// A tint plus a stripe in the gutter, not a wash. A file that is wholly
    /// new has every line marked, and at a strong alpha that is a solid
    /// block of colour over the code - which is unreadable exactly when the
    /// code has become readable, and is what the gathered change view looked
    /// like for any added file. The stripe is what stays visible when the
    /// tint is too faint to see; the tint is what tells you how far the
    /// change reaches.
    ///
    /// Both glow. A removal used to be a hairline with no light on it, which
    /// made a commit that deleted a hundred lines look like a commit that
    /// did nothing - the one case where the absence of code is the change.
    ///
    /// Drawn as runs rather than per line, so a block of two hundred added
    /// lines is one rectangle and one blur instead of two hundred of each.</summary>
    void DrawReviewLines(SKCanvas canvas, FileRec f)
    {
        if (Review is null || !Review.ByPath.TryGetValue(f.P, out var change)) return;

        float gutter = Math.Max(3f, f.W * 0.012f);
        using var fill = new SKPaint { IsAntialias = false };
        using var glow = new SKPaint
        {
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Data.LineH * 1.6f),
        };

        foreach (var (a, b) in Runs(change.AddedLines))
        {
            float y = Data.HeaderH + a * Data.LineH;
            float h = (b - a + 1) * Data.LineH;
            glow.Color = AddCol.WithAlpha(56);
            canvas.DrawRect(0, y, gutter * 2.5f, h, glow);
            fill.Color = AddCol.WithAlpha(TintAlpha);
            canvas.DrawRect(0, y, f.W, h, fill);
            fill.Color = AddCol;
            canvas.DrawRect(0, y, gutter, h, fill);
        }

        // a deletion is a point in the new file, not a span of it, so it
        // stays a line - but a lit one, with its own mark in the gutter
        foreach (var (a, b) in Runs(change.RemovedAt))
        {
            float y = Data.HeaderH + a * Data.LineH;
            float h = Math.Max(1.2f, (b - a) * Data.LineH);
            glow.Color = DelCol.WithAlpha(90);
            canvas.DrawRect(0, y - Data.LineH * 0.5f, f.W, h + Data.LineH, glow);
            fill.Color = DelCol;
            canvas.DrawRect(0, y - 0.6f, f.W, h, fill);
            canvas.DrawRect(0, y - Data.LineH * 0.4f, gutter, h + Data.LineH * 0.8f, fill);
        }
    }

    /// <summary>how strongly a changed line is washed. Low on purpose: this
    /// is laid over syntax-coloured source, and anything heavier turns a
    /// newly added file into a rectangle of one colour.</summary>
    const byte TintAlpha = 42;

    /// <summary>frame every file a change set touched.</summary>
    public void FitChanges(float vw, float vh)
    {
        if (Review is null) return;
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var change in Review.Files)
        {
            int i = IndexOfPath(change.Path);
            if (i < 0) continue;
            var f = Data.Files[i];
            x0 = Math.Min(x0, f.X); y0 = Math.Min(y0, f.Y);
            x1 = Math.Max(x1, f.X + f.W); y1 = Math.Max(y1, f.Y + f.H);
        }
        if (x0 > x1) { Fit(vw, vh); return; }
        CamX = (x0 + x1) / 2;
        CamY = (y0 + y1) / 2;
        CamS = Math.Min(vw / Math.Max(1, x1 - x0), vh / Math.Max(1, y1 - y0)) * 0.85f;
    }


    /// <summary>the hovered line and the picked range, in card-local space.</summary>
    void DrawPicks(SKCanvas canvas, FileRec f, int index)
    {
        if (HoverLine is { } hover && hover.File == index &&
            (Selection is not { } sel0 || sel0.File != index || hover.Line < sel0.From || hover.Line > sel0.To))
        {
            canvas.DrawRect(0, Data.HeaderH + hover.Line * Data.LineH, f.W, Data.LineH, _hover);
        }

        if (Selection is { } sel && sel.File == index)
        {
            float y = Data.HeaderH + sel.From * Data.LineH;
            float h = (sel.To - sel.From + 1) * Data.LineH;
            canvas.DrawRect(0, y, f.W, h, _pick);
            canvas.DrawRect(-3, y, 3, h, _pickEdge);
        }
    }

    /// <summary>a marker at the annotated line, and the text itself once you
    /// are close enough to read code. both are sized in screen terms: at 8x
    /// zoom a world-sized callout would be several screens wide, and a marker
    /// pinned to the card edge would be off screen entirely.</summary>
    void DrawAnnotations(SKCanvas canvas, FileRec f, float vw, float y0, float y1)
    {
        var list = AnchorsFor(f.P);
        if (list.Count == 0) return;

        float sc = 1f / CamS;                       // world units per screen pixel
        float left = CamX - vw / 2 * sc - f.X;      // card-local x of the screen edges
        float right = CamX + vw / 2 * sc - f.X;

        using var mark = new SKPaint { IsAntialias = false };
        using var band = new SKPaint { IsAntialias = false };

        foreach (var (a, r) in list)
        {
            float y = Data.HeaderH + r.Line * Data.LineH;
            float h = Math.Max(1, a.Span) * Data.LineH;
            if (f.Y + y + h < y0 || f.Y + y > y1) continue;

            mark.Color = ColorFor(r.Kind);
            // the whole annotated range stays tinted, so a note about a method
            // does not read as a note about its first line
            band.Color = mark.Color.WithAlpha(24);
            canvas.DrawRect(0, y, f.W, h, band);
            canvas.DrawRect(left + 4 * sc, y, 4 * sc, Math.Max(h, 2 * sc), mark);

            if (Tier < 3) continue;

            var body = r.Kind switch
            {
                AnchorKind.Drifted => a.Text + "   [the annotated lines are gone]",
                AnchorKind.Orphan => a.Text + "   [orphaned]",
                AnchorKind.Context => a.Text + "   [re-found by context]",
                _ => a.Text,
            };

            float cw = 300 * sc, pad = 10 * sc, lineH = 15 * sc;
            var wrapped = Wrap(body, cw - pad * 2, 12 * sc, out var paint);
            using (paint)
            {
                float boxH = pad * 2 + wrapped.Count * lineH;
                float x = right - cw - 14 * sc;
                using var bg = new SKPaint { Color = new SKColor(0x10, 0x16, 0x1e, 246) };
                canvas.DrawRect(x, y, cw, boxH, bg);
                canvas.DrawRect(x, y, 3 * sc, boxH, mark);
                paint.Color = new SKColor(0xdd, 0xe4, 0xea);
                for (int i = 0; i < wrapped.Count; i++)
                    canvas.DrawText(wrapped[i], x + pad, y + pad + (i + 1) * lineH - 3 * sc, paint);
            }
        }
    }

    // ---------- boards ----------

    // a note is read at the same distance as the code beside it, so it is set
    // at the same size as an annotation callout. 6.5pt was unreadable
    const float NotePad = 10, NoteFont = 11f, NoteLineH = 14;
    // a window's header is a fixed size on the board; only its code scales
    /// <summary>the strip at the top of a board's file window, with the
    /// file name in it. Public because ChangeBoard has to place things
    /// against it, and two copies of a number like this drift.</summary>
    public const float WinHeadH = 26;
    const char LF = (char)10;
    const char TabChar = (char)9;

    /// <summary>the line range a window shows, clamped to the file.</summary>
    public (int From, int To) RangeOf(BoardItem it, FileRec f)
    {
        int from = Math.Clamp(it.Line, 0, Math.Max(0, f.N - 1));
        int to = it.EndLine >= from ? Math.Min(it.EndLine, Math.Max(0, f.N - 1)) : Math.Max(0, f.N - 1);
        return (from, to);
    }

    /// <summary>how many times words have been measured with SkiaSharp.
    ///
    /// The tests watch this rather than watching a thread, because what goes
    /// wrong when two threads measure at once is not an exception: the
    /// process disappears. A counter says "nobody measured" in a way that can
    /// be asserted on.</summary>
    public int TextMeasures;

    readonly Dictionary<string, float> _measured = [];

    /// <summary>how tall an item was when it was last drawn, measuring
    /// nothing to answer.
    ///
    /// Only a note and a label get their height from wrapping their words,
    /// and wrapping measures text. The draw loop works that out every frame
    /// anyway, so everything off the draw loop - picking, the rubberband,
    /// resizing, fitting the view - reads what it left behind instead. See
    /// the threading note in CLAUDE.md: the UI thread measuring while the
    /// render thread draws takes the process down with nothing to catch.</summary>
    public float LastHeight(BoardItem it)
    {
        if (it.Kind is not ("note" or "text")) return ItemHeight(it);
        if (_measured.TryGetValue(it.Id, out var h)) return h;

        // never drawn: one line's worth, the closest guess that measures
        // nothing. The next frame replaces it with the truth
        float one = LineStep(SizeOf(it)) + (it.Kind == "note" ? NotePad * 2 : 0);
        return Math.Max(it.H, one);
    }

    public float ItemHeight(BoardItem it)
    {
        if (it.Kind == "arrow") return 0;
        // a stroke's box is its ink's bounds, kept in step by Strokes.Reframe
        if (Strokes.Is(it)) return it.H;
        if (it.Kind == "text") return LabelHeight(it);
        // a shape is exactly as tall as it was made. There used to be a floor
        // of 40 and a default of 240, which between them made a deliberately
        // short rectangle impossible - a shape's proportions are the user's
        // business and the clamp only ever got in the way
        if (IsShape(it.Kind)) return it.H > 0 ? it.H : 40;
        if (it.Kind == "image") return Math.Max(20, it.H > 0 ? it.H : it.W * 0.6f);
        if (it.Kind == "note")
            return Math.Max(it.H > 0 ? it.H : 0,
                NotePad * 2 + Math.Max(1, WrapNote(it).Count) * LineStep(SizeOf(it)));

        int i = it.File is null ? -1 : ResolveFile(it.File, it.Key);
        if (i < 0) return 64;
        var f = Data.Files[i];
        var (from, to) = RangeOf(it, f);
        return WinHeadH + (to - from + 1) * Data.LineH * (it.W / f.W);
    }

    /// <summary>the outlined shapes. They share everything but the path they
    /// trace: same box, same fill, same picking, same resize grip, so adding
    /// one is a case in a switch rather than a new kind of thing.</summary>
    public static bool IsShape(string kind) => kind is "shape" or "ellipse" or "diamond";

    /// <summary>kinds whose words are the user's, and so can be typed into
    /// and set at a size. A file window has text too, but it is the file's
    /// text and its size is the zoom's business.</summary>
    public static bool HasText(string kind) => kind is "note" or "text" || IsShape(kind);

    /// <summary>what to paint a shape's interior with.
    ///
    /// No fill means no fill: the shape is an outline you can see through,
    /// which is what makes one usable as a frame drawn round other things.
    /// It is still solid to the mouse - see ItemAt - because a frame you
    /// cannot pick up is a frame you cannot move.
    ///
    /// A chosen colour is laid on at a low alpha rather than flat, so a
    /// shape stays something you read *through* on a board full of code.</summary>
    /// <summary>how thick a shape's border or an arrow's shaft is, in board
    /// units, so it scales with the drawing rather than staying a hairline
    /// however far you zoom in.
    ///
    /// These were a hairline - stroke width zero, one screen pixel at any
    /// zoom - which is the one width that cannot be part of a drawing,
    /// because it says nothing about the thing it outlines. The default is
    /// what a hairline looked like at the zoom a board is usually read at.</summary>
    public const float DefaultBorder = 2f;

    public static float LineWidth(BoardItem it, float fallback = DefaultBorder) =>
        it.Weight > 0 ? it.Weight : fallback;

    public static SKColor FillOf(BoardItem it, SKColor border)
    {
        if (it.Fill is null) return border.WithAlpha(16);
        if (it.Fill == BoardItem.NoFill) return SKColors.Transparent;
        return ParseColor(it.Fill, border).WithAlpha(52);
    }

    /// <summary>"shape" is the rectangle, and stays that name because boards
    /// on disk already say it.</summary>
    static void DrawShape(SKCanvas canvas, string kind, SKRect box, SKPaint fill, SKPaint edge)
    {
        switch (kind)
        {
            case "ellipse":
                canvas.DrawOval(box, fill);
                canvas.DrawOval(box, edge);
                break;

            case "diamond":
                using (var path = Diamond(box))
                {
                    canvas.DrawPath(path, fill);
                    canvas.DrawPath(path, edge);
                }
                break;

            default:
                canvas.DrawRect(box, fill);
                canvas.DrawRect(box, edge);
                break;
        }
    }

    /// <summary>words inside a shape: a box with a label in it is what most
    /// of a flowchart is, and writing one meant putting a separate label on
    /// top and then moving the two together for ever afterwards.
    ///
    /// Centred both ways, because a shape's text belongs to the shape rather
    /// than starting at a corner of it, and wrapped to the box less a margin
    /// so a diamond's corners do not cut through the first word.</summary>
    void DrawShapeText(SKCanvas canvas, BoardItem it, SKRect box)
    {
        if (string.IsNullOrWhiteSpace(it.Text) || it.Id == EditingItem) return;

        float size = SizeOf(it), step = LineStep(size);
        // a diamond holds about half the words a rectangle of the same box
        // does, and the ones near the top and bottom are outside it
        float inset = it.Kind == "diamond" ? box.Width * 0.26f : 10f;
        var lines = Wrap(it.Text, Math.Max(16f, box.Width - inset * 2), size, out var paint);

        using (paint)
        {
            paint.Color = ParseColor(it.Color, LabelCol);
            paint.TextAlign = SKTextAlign.Center;
            float block = lines.Count * step;
            float top = box.MidY - block / 2;
            for (int i = 0; i < lines.Count; i++)
                canvas.DrawText(lines[i], box.MidX, top + (i + 1) * step - size * 0.35f, paint);
        }
    }

    static SKPath Diamond(SKRect b)
    {
        var path = new SKPath();
        path.MoveTo(b.MidX, b.Top);
        path.LineTo(b.Right, b.MidY);
        path.LineTo(b.MidX, b.Bottom);
        path.LineTo(b.Left, b.MidY);
        path.Close();
        return path;
    }

    /// <summary>type size when the item has not been given one. A label is
    /// large because being large is what makes it a heading; a note is small
    /// because it is an aside; words inside a shape sit between the two.
    ///
    /// Every one of them is only a default: `Size` on the item wins, and the
    /// "Text size" menu sets it, so anything with words in it can be made
    /// any size. Zero means "whatever this kind is normally", which is the
    /// one value that cannot be mistaken for a real size.</summary>
    public const float LabelSize = 34f;
    public const float ShapeFont = 18f;

    public static float DefaultSize(string kind) => kind switch
    {
        "text" => LabelSize,
        "note" => NoteFont,
        _ => ShapeFont,
    };

    public static float SizeOf(BoardItem it) => it.Size > 0 ? it.Size : DefaultSize(it.Kind);

    /// <summary>the baseline step for a block of text at this size. Wrapped
    /// lines need room between them, and the note's old fixed 14 stopped
    /// being room at all once a note could be set in 34 point.</summary>
    public static float LineStep(float size) => size * 1.28f;

    /// <summary>a standalone label: words on the board with no box round them.
    /// A note is a note *about* something and looks like a sticker; a label
    /// is a heading, and a heading with a panel behind it is a note.</summary>
    void DrawLabel(SKCanvas canvas, BoardItem it)
    {
        if (it.Id == EditingItem) return;
        float size = SizeOf(it);
        float step = LineStep(size);
        var lines = Wrap(it.Text ?? "", it.W, size, out var paint);
        using (paint)
        {
            paint.Color = ParseColor(it.Color, LabelCol);
            for (int i = 0; i < lines.Count; i++)
                canvas.DrawText(lines[i], it.X, it.Y + (i + 1) * step - size * 0.28f, paint);
        }
    }

    float LabelHeight(BoardItem it)
    {
        var lines = Wrap(it.Text ?? "", it.W, SizeOf(it), out var paint);
        paint.Dispose();
        return Math.Max(1, lines.Count) * LineStep(SizeOf(it));
    }

    List<string> WrapNote(BoardItem it)
    {
        var wrapped = Wrap(it.Text ?? "", it.W - NotePad * 2, SizeOf(it), out var paint);
        paint.Dispose();
        return wrapped;
    }

    /// <summary>word wrap. the caller owns the returned paint.</summary>
    List<string> Wrap(string text, float max, float size, out SKPaint paint)
    {
        var outLines = new List<string>();
        TextMeasures++;
        paint = new SKPaint { Typeface = _mono, TextSize = size, IsAntialias = true };

        foreach (var para in text.Split(LF))
        {
            var words = para.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) { outLines.Add(""); continue; }
            var line = words[0];
            for (int w = 1; w < words.Length; w++)
            {
                var next = line + " " + words[w];
                if (paint.MeasureText(next) <= max) line = next;
                else { outLines.Add(line); line = words[w]; }
            }
            outLines.Add(line);
        }
        return outLines;
    }

    /// <summary>take items off a board, cutting any arrow tied to one of
    /// them loose where it currently is.
    ///
    /// Every deletion goes through here. A tie left pointing at an item that
    /// is gone falls back to the arrow's stored coordinates, and those are
    /// from wherever the tie was first made - so deleting a box used to fling
    /// its connectors back across the board.</summary>
    public int Remove(Board board, IEnumerable<BoardItem> going)
    {
        var ids = going.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0) return 0;

        foreach (var it in board.Items)
        {
            if (it.Kind != "arrow") continue;
            bool cutFrom = it.From is { } f && ids.Contains(f);
            bool cutTo = it.To is { } t && ids.Contains(t);
            if (!cutFrom && !cutTo) continue;

            var (a, b) = ArrowEnds(it);
            if (cutFrom) { it.X = a.X; it.Y = a.Y; it.From = null; it.FromSide = -1; }
            if (cutTo) { it.X2 = b.X; it.Y2 = b.Y; it.To = null; it.ToSide = -1; }
        }

        int went = board.Items.RemoveAll(i => ids.Contains(i.Id));
        foreach (var id in ids) Picked.Remove(id);
        return went;
    }

    /// <summary>topmost item under a board-space point, or null.</summary>
    public BoardItem? ItemAt(float wx, float wy)
    {
        if (ActiveBoard is null) return null;
        for (int i = ActiveBoard.Items.Count - 1; i >= 0; i--)
        {
            var it = ActiveBoard.Items[i];
            if (it.Kind == "arrow" || Strokes.Is(it)) continue;
            // the whole box counts, plus a little slack, so a click near an
            // edge still lands on the thing you were aiming at
            float pad = 3f / CamS;
            if (wx >= it.X - pad && wx <= it.X + it.W + pad &&
                wy >= it.Y - pad && wy <= it.Y + LastHeight(it) + pad) return it;
        }
        return null;
    }

    /// <summary>every item the rubberband touches.</summary>
    public IEnumerable<BoardItem> ItemsIn(SKRect r)
    {
        if (ActiveBoard is null) yield break;
        foreach (var it in ActiveBoard.Items)
        {
            float h = LastHeight(it);
            if (it.X < r.Right && it.X + it.W > r.Left && it.Y < r.Bottom && it.Y + h > r.Top)
                yield return it;
        }
    }

    public static SKColor ParseColor(string? hex, SKColor fallback) =>
        hex is not null && SKColor.TryParse(hex, out var c) ? c : fallback;

    /// <summary>the grid cell to draw at this zoom. A single fixed cell either
    /// turns to mush when you zoom out or leaves nothing to align to when you
    /// zoom in, so the cell steps through a 1-2-5 ladder - the same one a
    /// ruler uses - to keep it near a readable size on screen.</summary>
    static readonly float[] GridLadder =
        [0.0625f, 0.125f, 0.25f, 0.5f, 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000];

    public static float GridStepFor(float cell, float camS, float minPx = 14f)
    {
        if (cell <= 0 || camS <= 0) return 0;
        foreach (var m in GridLadder)
            if (cell * m * camS >= minPx) return cell * m;
        return cell * GridLadder[^1];
    }

    /// <summary>the cell things snap to: whatever the grid is actually showing,
    /// so what you snap to is what you can see.</summary>
    public float SnapStep(float fallback) =>
        Grid > 0 ? GridStepFor(Grid, CamS) : fallback;

    void DrawGrid(SKCanvas canvas, float x0, float y0, float x1, float y1)
    {
        if (Grid <= 0) return;

        float minor = GridStepFor(Grid, CamS);
        if (minor <= 0) return;
        float major = minor * 5;

        // the minor grid fades in as it earns its place; the major one stays
        // legible so there is always something to read the scale against
        byte minorAlpha = (byte)Math.Clamp((minor * CamS - 6) * 5, 0, 30);

        float hair = 1f / CamS;
        using var fine = new SKPaint { Color = new SKColor(0x7f, 0xd8, 0xf0, minorAlpha), IsAntialias = false };
        using var coarse = new SKPaint { Color = new SKColor(0x7f, 0xd8, 0xf0, 54), IsAntialias = false };

        if (minorAlpha > 0)
        {
            for (float x = MathF.Floor(x0 / minor) * minor; x < x1; x += minor)
                canvas.DrawRect(x, y0, hair, y1 - y0, fine);
            for (float y = MathF.Floor(y0 / minor) * minor; y < y1; y += minor)
                canvas.DrawRect(x0, y, x1 - x0, hair, fine);
        }

        for (float x = MathF.Floor(x0 / major) * major; x < x1; x += major)
            canvas.DrawRect(x, y0, hair, y1 - y0, coarse);
        for (float y = MathF.Floor(y0 / major) * major; y < y1; y += major)
            canvas.DrawRect(x0, y, x1 - x0, hair, coarse);
    }

    void DrawBoard(SKCanvas canvas, float vw, float vh)
    {
        var board = ActiveBoard!;
        Tier = 3;
        VisibleCards = board.Items.Count;
        BuiltThisFrame = 0;

        canvas.Save();
        canvas.Translate(vw / 2 - CamX * CamS, vh / 2 - CamY * CamS);
        canvas.Scale(CamS);

        float bx = vw / 2 / CamS, by = vh / 2 / CamS;
        DrawGrid(canvas, CamX - bx, CamY - by, CamX + bx, CamY + by);

        using var body = new SKPaint { Color = CardBg, IsAntialias = false };
        using var head = new SKPaint { IsAntialias = false };
        using var label = new SKPaint { Color = LabelCol, Typeface = _mono, TextSize = 11, IsAntialias = true };
        using var code = new SKPaint { Typeface = _mono, TextSize = Data.LineH * 0.78f, IsAntialias = true };
        using var noteBg = new SKPaint { Color = new SKColor(0x15, 0x1b, 0x12), IsAntialias = false };
        using var noteEdge = new SKPaint
        {
            Color = new SKColor(0xff, 0xd1, 0x66, 200),
            IsStroke = true, StrokeWidth = DefaultBorder, IsAntialias = true,
        };
        using var noteText = new SKPaint { Color = new SKColor(0xe8, 0xd8, 0xa8), Typeface = _mono, TextSize = NoteFont, IsAntialias = true };
        using var missing = new SKPaint { Color = new SKColor(0x6a, 0x2b, 0x2b), IsAntialias = false };
        if (_charW == 0) _charW = code.MeasureText("0");

        using var pixels = new SKPaint { FilterQuality = SKFilterQuality.Medium, IsAntialias = true };
        using var shapeFill = new SKPaint { Color = new SKColor(0x5f, 0xd3, 0xf3, 16), IsAntialias = false };
        // antialiased now that the width is the user's to choose: a hairline
        // is crisp aliased, a five unit border is a staircase
        using var shapeEdge = new SKPaint { Color = new SKColor(0x5f, 0xd3, 0xf3, 120), IsStroke = true, StrokeWidth = DefaultBorder, IsAntialias = true };

        foreach (var it in board.Items)
        {
            if (it.Kind == "arrow" || Strokes.Is(it)) continue;   // drawn after, on top
            // the one place a note or a label is measured. Everything that is
            // not the draw loop reads the answer back with LastHeight
            if (it.Kind is "note" or "text") _measured[it.Id] = ItemHeight(it);
            if (it.Id == EditingItem) EditingHeight = LastHeight(it);
            if (IsShape(it.Kind))
            {
                var col = ParseColor(it.Color, new SKColor(0x5f, 0xd3, 0xf3));
                var box = new SKRect(it.X, it.Y, it.X + it.W, it.Y + ItemHeight(it));
                shapeFill.Color = FillOf(it, col);
                shapeEdge.Color = col.WithAlpha(150);
                shapeEdge.StrokeWidth = LineWidth(it);
                DrawShape(canvas, it.Kind, box, shapeFill, shapeEdge);
                DrawShapeText(canvas, it, box);
                continue;
            }
            if (it.Kind == "text")
            {
                DrawLabel(canvas, it);
                continue;
            }
            if (it.Kind == "image")
            {
                float ih = ItemHeight(it);
                var box = new SKRect(it.X, it.Y, it.X + it.W, it.Y + ih);
                var img = it.File is null ? null : ImageStore.Load(Data.Root, it.File);
                if (img is null)
                {
                    canvas.DrawRect(box, missing);
                    canvas.DrawText("missing image", it.X + 8, it.Y + 26, label);
                }
                else canvas.DrawImage(img, box, pixels);
                continue;
            }
            if (it.Kind == "note")
            {
                var wrapped = it.Id == EditingItem ? [] : WrapNote(it);
                float h = LastHeight(it);
                float size = SizeOf(it), step = LineStep(size);
                var accent = ParseColor(it.Color, new SKColor(0xff, 0xd1, 0x66));
                noteEdge.Color = accent;
                noteEdge.StrokeWidth = LineWidth(it);
                noteText.Color = accent.WithAlpha(235);
                noteText.TextSize = size;
                canvas.Save();
                canvas.Translate(it.X, it.Y);
                canvas.DrawRect(0, 0, it.W, h, noteBg);
                // all the way round. It used to be a 3 unit bar down the left
                // only, which reads as a quote in a document rather than as a
                // card on a canvas - and the other three sides of a note are
                // where it meets whatever it overlaps
                canvas.DrawRect(new SKRect(0, 0, it.W, h), noteEdge);
                for (int li = 0; li < wrapped.Count; li++)
                    canvas.DrawText(wrapped[li], NotePad, NotePad + (li + 1) * step - size * 0.3f, noteText);
                canvas.Restore();
                continue;
            }

            int i = it.File is null ? -1 : ResolveFile(it.File, it.Key);
            if (i >= 0 && Data.Files[i].P != it.File) it.File = Data.Files[i].P;   // it moved
            if (i < 0)
            {
                canvas.Save();
                canvas.Translate(it.X, it.Y);
                canvas.DrawRect(0, 0, it.W, 64, missing);
                canvas.DrawText($"missing: {it.File}", 8, 26, label);
                canvas.Restore();
                continue;
            }

            var f = Data.Files[i];
            var (from, to) = RangeOf(it, f);
            int count = to - from + 1;
            float k = it.W / f.W;
            float bodyH = count * Data.LineH * k;

            canvas.Save();
            canvas.Translate(it.X, it.Y);

            head.Color = FolderHue(_folderOf[i], 40, 22);
            canvas.DrawRect(0, 0, it.W, WinHeadH, head);
            canvas.DrawRect(0, WinHeadH, it.W, bodyH, body);
            var name = f.P[(f.P.LastIndexOf('/') + 1)..];
            label.TextSize = 14;
            canvas.DrawText($"{name}:{from + 1}", 8, 18, label);

            canvas.Save();
            canvas.Translate(0, WinHeadH);
            canvas.Scale(k);
            canvas.ClipRect(new SKRect(0, 0, f.W, count * Data.LineH));
            canvas.Translate(0, -(Data.HeaderH + from * Data.LineH));

            // the window reuses the map's own card drawing, just clipped
            bool drewText = CamS * k >= T_TEXT && DrawCode(canvas, i, from, to + 1, code);
            if (!drewText)
            {
                if (!_bars.TryGetValue(i, out var pic)) { pic = BuildBars(i); BuiltThisFrame++; }
                canvas.DrawPicture(pic);
            }
            // the same glow the map uses, in the same card-local coordinates.
            // Without it the gathered change view shows the right code and no
            // indication of what about it changed, which is most of the point
            if (Review is not null && Review.ByPath.ContainsKey(f.P)) DrawReviewLines(canvas, f);
            DrawBoardPicks(canvas, f, i);
            canvas.Restore();
            canvas.Restore();

            DrawBoardNoteText(canvas, it, f, from, to, k);
        }

        DrawStrokes(canvas, board);
        DrawShapeDraft(canvas);
        DrawAnchors(canvas, board);
        DrawArrows(canvas, board);
        DrawPickedItems(canvas, board);
        DrawRubberband(canvas);

        canvas.Restore();
    }

    /// <summary>an arrow being dragged out, in board coordinates.</summary>
    public (SKPoint A, SKPoint B)? ArrowDraft;

    /// <summary>where an arrow's ends actually are.
    ///
    /// A loose end is where it was put. A tied end is on the edge of the item
    /// it is tied to, worked out fresh every frame - which is the whole trick:
    /// nothing updates a connector when you drag a box, because there is
    /// nothing stored to update.
    ///
    /// The edge is found by aiming at the other end, so an arrow between two
    /// boxes meets both of them square on rather than reaching into a corner.</summary>
    public (SKPoint A, SKPoint B) ArrowEnds(BoardItem it)
    {
        var from = Bound(it.From);
        var to = Bound(it.To);

        var a = new SKPoint(it.X, it.Y);
        var b = new SKPoint(it.X2, it.Y2);

        // the far end is judged from the other item's centre, so which side
        // gets used does not depend on where the line currently happens to be
        if (from is not null)
            a = AnchorOf(from, Side(from, it.FromSide, to is null ? b : Centre(to)));
        if (to is not null)
            b = AnchorOf(to, Side(to, it.ToSide, from is null ? a : Centre(from)));
        return (a, b);
    }

    /// <summary>the four places a connector may meet an element: top, right,
    /// bottom, left. Anywhere on an edge, or worse a diagonal, means a line
    /// whose endpoint slides about as either box moves - which is what made
    /// the first attempt unreadable. Four points do not slide.</summary>
    public const int Top = 0, Right = 1, Bottom = 2, Left = 3;

    public SKPoint AnchorOf(BoardItem it, int side)
    {
        float h = LastHeight(it);
        float cx = it.X + it.W / 2, cy = it.Y + h / 2;
        return side switch
        {
            Top => new SKPoint(cx, it.Y),
            Right => new SKPoint(it.X + it.W, cy),
            Bottom => new SKPoint(cx, it.Y + h),
            _ => new SKPoint(it.X, cy),
        };
    }

    /// <summary>a side that was chosen stays chosen. One that was never set -
    /// an older tie, or one made from the menu - faces whatever it points at.</summary>
    int Side(BoardItem it, int stored, SKPoint toward) =>
        stored is >= Top and <= Left ? stored : FacingSide(it, toward);

    int FacingSide(BoardItem it, SKPoint toward)
    {
        var c = Centre(it);
        float dx = toward.X - c.X, dy = toward.Y - c.Y;
        return Math.Abs(dx) > Math.Abs(dy)
            ? dx >= 0 ? Right : Left
            : dy >= 0 ? Bottom : Top;
    }

    /// <summary>the anchor under a point, or null.
    ///
    /// Anywhere inside an element is not an anchor. Landing a line in the
    /// middle of a box and having it attach itself takes a decision away from
    /// you - sometimes a line crossing a box is just a line crossing a box -
    /// and the four nodes are drawn precisely so there is somewhere to aim.
    /// Aim at one to connect; miss, and the end stays where you put it.</summary>
    public (BoardItem Item, int Side)? AnchorAt(float wx, float wy)
    {
        if (ActiveBoard is null) return null;
        float reach = 13f / CamS;
        float nearest = reach * reach;
        (BoardItem, int)? best = null;

        for (int i = ActiveBoard.Items.Count - 1; i >= 0; i--)
        {
            var it = ActiveBoard.Items[i];
            if (it.Kind == "arrow" || Strokes.Is(it)) continue;

            for (int side = Top; side <= Left; side++)
            {
                var p = AnchorOf(it, side);
                float d = (wx - p.X) * (wx - p.X) + (wy - p.Y) * (wy - p.Y);
                if (d > nearest) continue;
                nearest = d;
                best = (it, side);
            }
        }
        return best;
    }

    /// <summary>which anchor a point is nearest, for attaching to one.</summary>
    public int NearestSide(BoardItem it, float wx, float wy)
    {
        int best = Top;
        float nearest = float.MaxValue;
        for (int side = Top; side <= Left; side++)
        {
            var p = AnchorOf(it, side);
            float d = (wx - p.X) * (wx - p.X) + (wy - p.Y) * (wy - p.Y);
            if (d < nearest) { nearest = d; best = side; }
        }
        return best;
    }

    /// <summary>true while the anchors should be visible: an arrow is being
    /// drawn or an end dragged, and you need to see where it can land.</summary>
    public bool ShowAnchors;

    void DrawAnchors(SKCanvas canvas, Board board)
    {
        if (!ShowAnchors) return;
        float r = 4f / CamS;

        using var dot = new SKPaint { Color = new SKColor(0x5f, 0xd3, 0xf3, 210), IsAntialias = true };
        foreach (var it in board.Items)
        {
            if (it.Kind == "arrow" || Strokes.Is(it)) continue;
            for (int side = Top; side <= Left; side++)
                canvas.DrawCircle(AnchorOf(it, side), r, dot);
        }
    }

    BoardItem? Bound(string? id)
    {
        if (id is null || ActiveBoard is null) return null;
        foreach (var it in ActiveBoard.Items) if (it.Id == id) return it;
        return null;   // tied to something that is gone: the end falls loose
    }

    SKPoint Centre(BoardItem it) => new(it.X + it.W / 2, it.Y + LastHeight(it) / 2);

    void DrawArrows(SKCanvas canvas, Board board)
    {
        const float ArrowShaft = 2.5f;
        using var line = new SKPaint { IsStroke = true, StrokeWidth = ArrowShaft, IsAntialias = true };
        foreach (var it in board.Items)
        {
            if (it.Kind != "arrow") continue;

            line.Color = ParseColor(it.Color, new SKColor(0xff, 0xd1, 0x66));
            line.StrokeWidth = LineWidth(it, ArrowShaft);
            var (p1, p2) = ArrowEnds(it);
            canvas.DrawLine(p1, p2, line);

            // the head grows with the shaft, or a thick arrow ends in a tick
            float ang = MathF.Atan2(p2.Y - p1.Y, p2.X - p1.X);
            float head = 11f * (line.StrokeWidth / ArrowShaft);
            canvas.DrawLine(p2, new SKPoint(
                p2.X - head * MathF.Cos(ang - 0.4f), p2.Y - head * MathF.Sin(ang - 0.4f)), line);
            canvas.DrawLine(p2, new SKPoint(
                p2.X - head * MathF.Cos(ang + 0.4f), p2.Y - head * MathF.Sin(ang + 0.4f)), line);

            // ends are grabbable once the arrow is picked. A tied end is drawn
            // hollow: there is no point dragging it, and the ring says which
            // arrows will follow a box when you move it
            if (!Picked.Contains(it.Id)) continue;
            float sc0 = 1f / CamS;
            using var knob = new SKPaint { Color = new SKColor(0x5f, 0xd3, 0xf3), IsAntialias = true };
            using var ring = new SKPaint
            {
                Color = new SKColor(0x5f, 0xd3, 0xf3), IsAntialias = true,
                IsStroke = true, StrokeWidth = 1.6f * sc0,
            };
            canvas.DrawCircle(p1, 5 * sc0, it.From is null ? knob : ring);
            canvas.DrawCircle(p2, 5 * sc0, it.To is null ? knob : ring);
        }

        if (ArrowDraft is { } draft)
        {
            line.Color = new SKColor(0x5f, 0xd3, 0xf3, 200);
            line.StrokeWidth = ArrowShaft;
            canvas.DrawLine(draft.A, draft.B, line);
        }
    }

    /// <summary>which end of a picked arrow is under the point, if any.
    /// 1 is the start, 2 the finish.</summary>
    public (BoardItem Arrow, int End)? ArrowEndAt(float wx, float wy)
    {
        if (ActiveBoard is null) return null;
        float r = 8f / CamS;
        foreach (var it in ActiveBoard.Items)
        {
            if (it.Kind != "arrow" || !Picked.Contains(it.Id)) continue;
            var (a, b) = ArrowEnds(it);
            if (Math.Abs(wx - a.X) < r && Math.Abs(wy - a.Y) < r) return (it, 1);
            if (Math.Abs(wx - b.X) < r && Math.Abs(wy - b.Y) < r) return (it, 2);
        }
        return null;
    }

    /// <summary>the item whose words are being typed into an editor laid
    /// over it. Its own text is left undrawn while that is up, or the same
    /// words appear twice, slightly out of register.</summary>
    public string? EditingItem;

    /// <summary>how tall that item was, last time it was drawn.
    ///
    /// The editor has to be the size of the item, and asking for it with
    /// ItemHeight is not allowed: a note's height comes from wrapping its
    /// words, wrapping measures them with SkiaSharp, and the caller is the
    /// UI thread while the render thread is inside Draw measuring text with
    /// the same typeface. Two threads in Skia's text path at once does not
    /// throw - it takes the process down where no handler can see it.
    ///
    /// So the draw loop leaves the number here and the UI thread reads it. A
    /// float is written and read whole, and a frame of staleness in the size
    /// of a text box is not worth a lock.</summary>
    public float EditingHeight;

    /// <summary>the stroke being drawn right now, before it is committed.</summary>
    public BoardItem? StrokeDraft;

    /// <summary>the shape being dragged out, before it is placed.</summary>
    public (string Kind, SKRect Box)? ShapeDraft;

    void DrawShapeDraft(SKCanvas canvas)
    {
        if (ShapeDraft is not { } draft) return;

        var col = ParseColor(null, new SKColor(0x5f, 0xd3, 0xf3));
        using var fill = new SKPaint { Color = col.WithAlpha(16), IsAntialias = true };
        using var edge = new SKPaint
        {
            Color = col.WithAlpha(200), IsStroke = true,
            StrokeWidth = 1.5f / CamS, IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash([6f / CamS, 5f / CamS], 0),
        };

        if (draft.Kind == "text")
        {
            // a label has no height of its own until it has words, so the
            // draft shows the width it will wrap to and nothing more
            canvas.DrawLine(draft.Box.Left, draft.Box.Top, draft.Box.Right, draft.Box.Top, edge);
            return;
        }
        DrawShape(canvas, draft.Kind, draft.Box, fill, edge);
    }

    SKPicture? _strokePic;
    long _strokeSig;

    /// <summary>how many times the ink has been re-recorded. The point of the
    /// cache is that this stays still while nothing is being drawn, and a
    /// count is a steadier thing to assert on than a stopwatch.</summary>
    public int StrokeRebuilds { get; private set; }

    static SKPaint Pen() => new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
    };

    /// <summary>ink is recorded once and replayed, the way the map does its
    /// bars. Measured at a thousand strokes: rebuilding every path every
    /// frame cost 37ms, which is past the 60fps budget on its own, and a
    /// drawing surface is somewhere people make thousands.
    ///
    /// The stroke being drawn right now is not in the recording - it changes
    /// every frame by definition - so it goes on top, live.</summary>
    void DrawStrokes(SKCanvas canvas, Board board)
    {
        long sig = StrokeSignature(board);
        if (_strokePic is null || sig != _strokeSig)
        {
            _strokePic?.Dispose();
            _strokePic = RecordStrokes(board);
            _strokeSig = sig;
            StrokeRebuilds++;
        }
        if (_strokePic is not null) canvas.DrawPicture(_strokePic);

        if (StrokeDraft is { } draft)
        {
            using var pen = Pen();
            DrawStroke(canvas, draft, pen);
        }
    }

    SKPicture? RecordStrokes(Board board)
    {
        var bounds = SKRect.Empty;
        bool any = false;
        foreach (var it in board.Items)
        {
            if (!Strokes.Is(it)) continue;
            var box = new SKRect(it.X, it.Y, it.X + it.W, it.Y + it.H);
            bounds = any ? SKRect.Union(bounds, box) : box;
            any = true;
        }
        if (!any) return null;

        var rec = new SKPictureRecorder();
        var c = rec.BeginRecording(bounds);
        using (var pen = Pen())
            foreach (var it in board.Items)
                if (Strokes.Is(it)) DrawStroke(c, it, pen);
        return rec.EndRecording();
    }

    /// <summary>cheap enough to compute every frame, and changes whenever the
    /// ink does. Bounds rather than points, so it stays O(strokes) rather
    /// than O(samples): every edit that changes a stroke's shape moves its
    /// bounds, its sample count, its weight or its colour.</summary>
    public static long StrokeSignature(Board board)
    {
        long sig = 17;
        foreach (var it in board.Items)
        {
            if (!Strokes.Is(it)) continue;
            sig = sig * 31 + it.Id.GetHashCode();
            sig = sig * 31 + (it.Points?.Count ?? 0);
            sig = sig * 31 + BitConverter.SingleToInt32Bits(it.X);
            sig = sig * 31 + BitConverter.SingleToInt32Bits(it.Y);
            sig = sig * 31 + BitConverter.SingleToInt32Bits(it.W);
            sig = sig * 31 + BitConverter.SingleToInt32Bits(it.H);
            sig = sig * 31 + BitConverter.SingleToInt32Bits(it.Weight);
            sig = sig * 31 + (it.Color?.GetHashCode() ?? 0);
        }
        return sig;
    }

    void DrawStroke(SKCanvas canvas, BoardItem it, SKPaint pen)
    {
        if (Strokes.CountOf(it) == 0) return;
        pen.Color = ParseColor(it.Color, LabelCol);
        pen.StrokeWidth = it.Weight > 0 ? it.Weight : Strokes.DefaultWeight;
        using var path = Strokes.PathOf(it);
        canvas.DrawPath(path, pen);
    }

    /// <summary>the topmost stroke under a point, or null. Separate from
    /// ItemAt for the same reason arrows are: a stroke's box is mostly empty,
    /// and a click inside it that misses the ink belongs to the canvas.</summary>
    public BoardItem? StrokeAt(float wx, float wy)
    {
        if (ActiveBoard is null) return null;
        float tol = 7f / CamS;
        for (int i = ActiveBoard.Items.Count - 1; i >= 0; i--)
        {
            var it = ActiveBoard.Items[i];
            if (Strokes.Is(it) && Strokes.Touches(it, wx, wy, tol)) return it;
        }
        return null;
    }

    /// <summary>every stroke the eraser is touching.</summary>
    public List<BoardItem> StrokesNear(float wx, float wy, float radius)
    {
        var hit = new List<BoardItem>();
        if (ActiveBoard is null) return hit;
        foreach (var it in ActiveBoard.Items)
            if (Strokes.Is(it) && Strokes.Touches(it, wx, wy, radius)) hit.Add(it);
        return hit;
    }

    /// <summary>everything the eraser is touching, of any kind. A stroke is
    /// judged by its ink and everything else by its box, which is the same
    /// rule clicking follows - what looks touched is touched.</summary>
    public List<BoardItem> ItemsNear(float wx, float wy, float radius)
    {
        var hit = new List<BoardItem>();
        if (ActiveBoard is null) return hit;

        var box = new SKRect(wx - radius, wy - radius, wx + radius, wy + radius);
        foreach (var it in ActiveBoard.Items)
        {
            if (Strokes.Is(it)) { if (Strokes.Touches(it, wx, wy, radius)) hit.Add(it); continue; }

            if (it.Kind == "arrow")
            {
                var (a, b) = ArrowEnds(it);
                if (NearSegment(a, b, wx, wy, radius)) hit.Add(it);
                continue;
            }

            float h = LastHeight(it);
            if (it.X < box.Right && it.X + it.W > box.Left &&
                it.Y < box.Bottom && it.Y + h > box.Top) hit.Add(it);
        }
        return hit;
    }

    /// <summary>is the point within tol of the segment a-b. Takes the ends
    /// rather than the arrow, because the ends of a tied arrow are not the
    /// ones stored on it - reading X/Y here is what made the eraser work on
    /// where an arrow used to be.</summary>
    static bool NearSegment(SKPoint a, SKPoint b, float wx, float wy, float tol)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len2 = dx * dx + dy * dy;
        if (len2 < 0.01f) return false;
        float t = Math.Clamp(((wx - a.X) * dx + (wy - a.Y) * dy) / len2, 0, 1);
        float px = a.X + dx * t, py = a.Y + dy * t;
        return (wx - px) * (wx - px) + (wy - py) * (wy - py) <= tol * tol;
    }

    /// <summary>an arrow near the point, for picking one without a box.</summary>
    public BoardItem? ArrowAt(float wx, float wy)
    {
        if (ActiveBoard is null) return null;
        float tol = 7f / CamS;
        foreach (var it in ActiveBoard.Items)
        {
            if (it.Kind != "arrow") continue;
            var (a, b) = ArrowEnds(it);
            if (NearSegment(a, b, wx, wy, tol)) return it;
        }
        return null;
    }

    void DrawPickedItems(SKCanvas canvas, Board board)
    {
        if (Picked.Count == 0) return;
        float sc = 1f / CamS;
        using var edge = new SKPaint
        {
            Color = new SKColor(0x5f, 0xd3, 0xf3), IsStroke = true, StrokeWidth = 0, IsAntialias = false,
        };
        using var grip = new SKPaint { Color = new SKColor(0x5f, 0xd3, 0xf3), IsAntialias = false };

        foreach (var it in board.Items)
        {
            if (!Picked.Contains(it.Id) || it.Kind == "arrow") continue;
            float h = ItemHeight(it);
            canvas.DrawRect(it.X - 3 * sc, it.Y - 3 * sc, it.W + 6 * sc, h + 6 * sc, edge);
            // a grip on each corner, so a box can be pulled from whichever
            // side is nearest rather than only from the bottom right. They
            // resize the box and leave the font alone; a file window has none,
            // because its size comes from the range of lines it shows
            if (!Resizable(it)) continue;
            for (int corner = 0; corner < 4; corner++)
            {
                float gx = (corner & GripLeft) != 0 ? it.X : it.X + it.W;
                float gy = (corner & GripTop) != 0 ? it.Y : it.Y + h;
                canvas.DrawRect(gx - 5 * sc, gy - 5 * sc, 10 * sc, 10 * sc, grip);
            }
        }
    }

    void DrawPickedFiles(SKCanvas canvas, float x0, float y0, float x1, float y1)
    {
        if (PickedFiles.Count == 0) return;
        float sc = 1f / CamS;
        using var edge = new SKPaint
        {
            Color = new SKColor(0x5f, 0xd3, 0xf3), IsStroke = true, StrokeWidth = 0, IsAntialias = false,
        };
        using var wash = new SKPaint { Color = new SKColor(0x5f, 0xd3, 0xf3, 34), IsAntialias = false };

        foreach (var i in PickedFiles)
        {
            if (i < 0 || i >= Data.Files.Count) continue;
            var f = Data.Files[i];
            if (f.X > x1 || f.X + f.W < x0 || f.Y > y1 || f.Y + f.H < y0) continue;
            canvas.DrawRect(f.X, f.Y, f.W, f.H, wash);
            canvas.DrawRect(f.X - 2 * sc, f.Y - 2 * sc, f.W + 4 * sc, f.H + 4 * sc, edge);
        }
    }

    /// <summary>every file the rubberband touches.</summary>
    public IEnumerable<int> FilesIn(SKRect r)
    {
        for (int i = 0; i < Data.Files.Count; i++)
        {
            var f = Data.Files[i];
            if (f.X < r.Right && f.X + f.W > r.Left && f.Y < r.Bottom && f.Y + f.H > r.Top)
                yield return i;
        }
    }

    void DrawRubberband(SKCanvas canvas)
    {
        if (Rubberband is not { } r || RubberbandFade <= 0.01f) return;
        using var fill = new SKPaint
        {
            Color = new SKColor(0x5f, 0xd3, 0xf3, (byte)(70 * RubberbandFade)), IsAntialias = false,
        };
        using var edge = new SKPaint
        {
            Color = new SKColor(0x5f, 0xd3, 0xf3, (byte)(200 * RubberbandFade)),
            IsStroke = true, StrokeWidth = 0, IsAntialias = false,
        };
        canvas.DrawRect(r, fill);
        canvas.DrawRect(r, edge);
    }

    /// <summary>which source line a point falls on inside a board's file
    /// window. notes are authored here now, so the window needs to resolve a
    /// point all the way down to a line of code.</summary>
    public (BoardItem Item, int File, int Line)? LineInWindowAt(float wx, float wy)
    {
        if (ActiveBoard is null) return null;
        // a rectangle laid over a window must take the click, not pass it down
        if (ItemAt(wx, wy) is { } top && top.Kind != "file") return null;

        for (int n = ActiveBoard.Items.Count - 1; n >= 0; n--)
        {
            var it = ActiveBoard.Items[n];
            if (it.Kind != "file" || it.File is null) continue;

            int i = ResolveFile(it.File, it.Key);
            if (i < 0) continue;
            var f = Data.Files[i];
            float h = LastHeight(it);
            if (wx < it.X || wx > it.X + it.W || wy < it.Y || wy > it.Y + h) continue;

            var (from, to) = RangeOf(it, f);
            float k = it.W / f.W;
            int line = from + (int)((wy - it.Y - WinHeadH) / (Data.LineH * k));
            return (it, i, Math.Clamp(line, from, to));
        }
        return null;
    }

    public static bool Resizable(BoardItem it) =>
        it.Kind is "note" or "image" or "text" || IsShape(it.Kind) || Strokes.Is(it);

    /// <summary>the resize grip of a picked item, if the point is on one.</summary>
    /// <summary>which corner: bit 1 is the left edge, bit 2 the top. So 0 is
    /// bottom right, 1 bottom left, 2 top right, 3 top left.</summary>
    public const int GripLeft = 1, GripTop = 2;

    public (BoardItem Item, int Corner)? GripAt(float wx, float wy)
    {
        if (ActiveBoard is null) return null;
        float sc = 1f / CamS;

        for (int i = ActiveBoard.Items.Count - 1; i >= 0; i--)
        {
            var it = ActiveBoard.Items[i];
            if (!Picked.Contains(it.Id) || !Resizable(it)) continue;

            float h = LastHeight(it);
            for (int corner = 0; corner < 4; corner++)
            {
                float gx = (corner & GripLeft) != 0 ? it.X : it.X + it.W;
                float gy = (corner & GripTop) != 0 ? it.Y : it.Y + h;
                if (Math.Abs(wx - gx) <= 7 * sc && Math.Abs(wy - gy) <= 7 * sc)
                    return (it, corner);
            }
        }
        return null;
    }

    /// <summary>drag a corner. The two edges that corner owns move to the
    /// pointer and the opposite two stay put - which for a top or left grip
    /// means the item's origin moves, not only its size.</summary>
    public void Resize(BoardItem it, int corner, float wx, float wy, float min = 8f)
    {
        float h = LastHeight(it);
        float left = it.X, top = it.Y, right = it.X + it.W, bottom = it.Y + h;

        if ((corner & GripLeft) != 0) left = Math.Min(wx, right - min);
        else right = Math.Max(wx, left + min);

        if ((corner & GripTop) != 0) top = Math.Min(wy, bottom - min);
        else bottom = Math.Max(wy, top + min);

        // a stroke has no box to stretch: its bounds are where the ink is, so
        // resizing one means moving every sample into the new box
        if (Strokes.Is(it))
        {
            Strokes.ScaleInto(it, new SKRect(left, top, right, bottom), min);
            return;
        }

        it.X = left;
        it.Y = top;
        it.W = right - left;
        // a label's height is its words, so only its width is dragged
        if (it.Kind != "text") it.H = bottom - top;
    }

    /// <summary>annotation tint inside a board's file window, in card space.</summary>
    void DrawBoardPicks(SKCanvas canvas, FileRec f, int index)
    {
        // the picked range, so a board can be annotated like the map
        if (Selection is { } sel && sel.File == index)
        {
            using var pick = new SKPaint { Color = new SKColor(0x5f, 0xd3, 0xf3, 52), IsAntialias = false };
            using var pickEdge = new SKPaint { Color = new SKColor(0x5f, 0xd3, 0xf3), IsAntialias = false };
            float py = Data.HeaderH + sel.From * Data.LineH;
            float ph = (sel.To - sel.From + 1) * Data.LineH;
            canvas.DrawRect(0, py, f.W, ph, pick);
            canvas.DrawRect(0, py, 2.5f, ph, pickEdge);
        }

        using var band = new SKPaint { IsAntialias = false };
        using var edge = new SKPaint { IsAntialias = false };
        foreach (var (a, r) in AnchorsFor(f.P))
        {
            var col = ColorFor(r.Kind);
            band.Color = col.WithAlpha(30);
            edge.Color = col;
            float y = Data.HeaderH + r.Line * Data.LineH;
            float h = Math.Max(1, a.Span) * Data.LineH;
            canvas.DrawRect(0, y, f.W, h, band);
            canvas.DrawRect(0, y, 2.5f, h, edge);
        }
    }

    /// <summary>the note text beside a window. drawn OUTSIDE the window's clip
    /// rect, which is what was cutting it away entirely.</summary>
    void DrawBoardNoteText(SKCanvas canvas, BoardItem it, FileRec f, int from, int to, float k)
    {
        var list = AnchorsFor(f.P);
        if (list.Count == 0) return;

        using var edge = new SKPaint { IsAntialias = false };
        float size = 11f;
        float lastBottom = float.MinValue;

        // in line order, so two notes a few lines apart stack instead of
        // landing on top of each other
        foreach (var (a, r) in list.OrderBy(x => x.R.Line))
        {
            if (r.Line < from || r.Line > to) continue;
            edge.Color = ColorFor(r.Kind);

            float at = it.Y + WinHeadH + (r.Line - from) * Data.LineH * k;
            float y = Math.Max(at, lastBottom + 6);
            var wrapped = Wrap(a.Text, 220, size, out var paint);
            using (paint)
            {
                float lh = size * 1.3f;
                float boxH = wrapped.Count * lh + 10;
                float bx = it.X + it.W + 14;
                using var bg = new SKPaint { Color = new SKColor(0x10, 0x16, 0x1e, 244) };
                canvas.DrawRect(bx, y, 236, boxH, bg);
                canvas.DrawRect(bx, y, 3, boxH, edge);
                // the leader still points at the line the note is about, even
                // when the box had to be pushed down to make room
                canvas.DrawLine(it.X + it.W, at + 6, bx, y + 6, edge);
                paint.Color = new SKColor(0xdd, 0xe4, 0xea);
                for (int i = 0; i < wrapped.Count; i++)
                    canvas.DrawText(wrapped[i], bx + 8, y + 5 + (i + 1) * lh - 3, paint);
                lastBottom = y + boxH;
            }
        }
    }

    /// <summary>frame everything on the board.</summary>
    public void FitBoard(float vw, float vh)
    {
        var board = ActiveBoard;
        if (board is null || board.Items.Count == 0)
        {
            CamX = 0; CamY = 0; CamS = 1;
            return;
        }
        float x0 = board.Items.Min(i => i.X), y0 = board.Items.Min(i => i.Y);
        float x1 = board.Items.Max(i => i.X + i.W);
        float y1 = board.Items.Max(i => i.Y + LastHeight(i));
        CamX = (x0 + x1) / 2;
        CamY = (y0 + y1) / 2;
        CamS = Math.Min(vw / Math.Max(1, x1 - x0), vh / Math.Max(1, y1 - y0)) * 0.88f;
    }

    public void Fit(float vw, float vh)
    {
        CamS = Math.Min(vw / Data.World.W, vh / Data.World.H) * 0.92f;
        CamX = Data.World.W / 2;
        CamY = Data.World.H / 2;
    }

    /// <summary>everything there is to look at, in world coordinates: the
    /// scanned world on the map, whatever is on the board otherwise. An empty
    /// board still gets an area, so a board you have not drawn on yet has
    /// somewhere to be.</summary>
    public SKRect ContentBounds()
    {
        var board = ActiveBoard;
        if (board is null)
            return new SKRect(0, 0, Math.Max(1, Data.World.W), Math.Max(1, Data.World.H));

        if (board.Items.Count == 0) return new SKRect(-EmptyBoard, -EmptyBoard, EmptyBoard, EmptyBoard);

        return new SKRect(
            board.Items.Min(i => Math.Min(i.X, i.Kind == "arrow" ? i.X2 : i.X)),
            board.Items.Min(i => Math.Min(i.Y, i.Kind == "arrow" ? i.Y2 : i.Y)),
            board.Items.Max(i => Math.Max(i.X + i.W, i.Kind == "arrow" ? i.X2 : i.X)),
            board.Items.Max(i => Math.Max(i.Y + LastHeight(i), i.Kind == "arrow" ? i.Y2 : i.Y)));
    }

    const float EmptyBoard = 1200;

    /// <summary>keep the camera near what there is to see. Without this the
    /// canvas pans into empty space forever and the only way back is F.
    ///
    /// The slack is a screen either side, so you can still pull content off
    /// centre to work beside it - you just cannot lose it.</summary>
    public void ClampCamera(float vw, float vh)
    {
        var b = ContentBounds();
        float slackX = vw / CamS, slackY = vh / CamS;

        float x0 = b.Left - slackX, x1 = b.Right + slackX;
        float y0 = b.Top - slackY, y1 = b.Bottom + slackY;

        // when the content is smaller than the slack the range can invert;
        // centring on it is the only sensible answer
        CamX = x1 < x0 ? (b.Left + b.Right) / 2 : Math.Clamp(CamX, x0, x1);
        CamY = y1 < y0 ? (b.Top + b.Bottom) / 2 : Math.Clamp(CamY, y0, y1);
    }

    /// <summary>the smallest zoom that still shows everything, less a little,
    /// so you cannot zoom out until the content is a speck.</summary>
    public float MinZoomFor(float vw, float vh)
    {
        var b = ContentBounds();
        float fit = Math.Min(vw / Math.Max(1, b.Width), vh / Math.Max(1, b.Height));
        return Math.Clamp(fit * 0.35f, 0.006f, 1f);
    }

    public void Stress()
    {
        var baseFiles = Data.Files;
        var baseFolders = Data.Folders;
        float W = Data.World.W + 400, H = Data.World.H + 400;
        var files = new List<FileRec>(baseFiles.Count * 9);
        var folders = new List<Folder>(baseFolders.Count * 9);
        for (int gy = 0; gy < 3; gy++)
        for (int gx = 0; gx < 3; gx++)
        {
            foreach (var f in baseFiles)
                files.Add(new FileRec { P = f.P, N = f.N, D = f.D, X = f.X + gx * W, Y = f.Y + gy * H, W = f.W, H = f.H });
            foreach (var d in baseFolders)
                folders.Add(new Folder { Name = d.Name, X = d.X + gx * W, Y = d.Y + gy * H, W = d.W, H = d.H });
        }
        Data = new Scan
        {
            Root = Data.Root, LineH = Data.LineH, HeaderH = Data.HeaderH,
            World = new WorldSize { W = W * 3, H = H * 3 },
            Folders = folders, Files = files,
        };
        Rebuild();
    }

    public void Dispose()
    {
        foreach (var p in _bars.Values) p.Dispose();
        _strokePic?.Dispose();
        _folderPic?.Dispose();
        _cardPic?.Dispose();
        _mono.Dispose();
        _band.Dispose();
        _dim.Dispose();
        _veil.Dispose();
        _hover.Dispose();
        _pick.Dispose();
        _pickEdge.Dispose();
        _bandEdge.Dispose();
    }
}

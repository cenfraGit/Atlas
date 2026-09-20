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

public sealed class District
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
    [JsonPropertyName("districts")] public List<District> Districts { get; set; } = [];
    [JsonPropertyName("files")] public List<FileRec> Files { get; set; } = [];
}

/// <summary>same level-of-detail rules and colours as the web prototype.</summary>
public sealed class Scene : IDisposable
{
    const float T_CARD = 0.055f, T_BARS = 0.45f, T_TEXT = 1.9f;

    /// <summary>which level of detail a zoom falls in: 0 districts, 1 cards,
    /// 2 bars, 3 text. Pulled out of the draw loop so the thresholds can be
    /// checked without a window.</summary>
    public static int TierFor(float camS) =>
        camS < T_CARD ? 0 : camS < T_BARS ? 1 : camS < T_TEXT ? 2 : 3;

    static readonly SKColor Bg = new(0x04, 0x07, 0x0f);
    // a board is a different place: indigo instead of the map's blue black
    static readonly SKColor BoardBg = new(0x16, 0x10, 0x28);
    static readonly SKColor CardBg = new(0x08, 0x13, 0x20);
    static readonly SKColor HeaderBg = new(0x0f, 0x23, 0x34);
    static readonly SKColor DistrictBg = new(0x07, 0x12, 0x1c);
    static readonly SKColor DistrictEdge = new(0x1b, 0x4c, 0x66);
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
    public bool ShowDistricts = true;

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

    int[] _districtOf = [];
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
    SKPicture? _districtPic, _cardPic;

    public Scene(Scan data)
    {
        Data = data;
        Rebuild();
    }

    public int ChunksBuilt => _bars.Count;

    static SKColor DistrictHue(int i, byte sat, byte light) =>
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

    void MapFilesToDistricts()
    {
        _pathIndex.Clear();
        for (int i = 0; i < Data.Files.Count; i++) _pathIndex[Data.Files[i].P] = i;

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Data.Districts.Count; i++) index[Data.Districts[i].Name] = i;

        _districtOf = new int[Data.Files.Count];
        for (int i = 0; i < Data.Files.Count; i++)
        {
            var p = Data.Files[i].P;
            int cut = p.LastIndexOf('/');
            var dir = cut < 0 ? "." : p[..cut];
            _districtOf[i] = index.TryGetValue(dir, out var d) ? d : 0;
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
        MapFilesToDistricts();
        foreach (var p in _bars.Values) p.Dispose();
        _bars.Clear();
        _districtPic?.Dispose();
        _cardPic?.Dispose();

        var bounds = new SKRect(0, 0, Data.World.W, Data.World.H);

        var rec = new SKPictureRecorder();
        var c = rec.BeginRecording(bounds);
        using (var fill = new SKPaint { Color = DistrictBg, IsAntialias = false })
        using (var edge = new SKPaint { IsStroke = true, StrokeWidth = 0, IsAntialias = false })
        using (var lab = new SKPaint { Typeface = _mono, IsAntialias = true })
        {
            for (int i = 0; i < Data.Districts.Count; i++)
            {
                var d = Data.Districts[i];
                var r = new SKRect(d.X - 10, d.Y - 26, d.X + d.W + 10, d.Y + d.H + 10);
                c.DrawRect(r, fill);
                edge.Color = DistrictHue(i, 55, 40);
                c.DrawRect(r, edge);
            }

        }
        _districtPic = rec.EndRecording();

        rec = new SKPictureRecorder();
        c = rec.BeginRecording(bounds);
        using (var body = new SKPaint { Color = CardBg, IsAntialias = false })
        using (var head = new SKPaint { IsAntialias = false })
        {
            foreach (var f in Data.Files) c.DrawRect(f.X, f.Y, f.W, f.H, body);
            for (int i = 0; i < Data.Files.Count; i++)
            {
                var f = Data.Files[i];
                head.Color = DistrictHue(_districtOf[i], 40, 20);
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

    public IReadOnlyList<(Annotation A, Anchor R)> AnchorsFor(string relPath) =>
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
        if (ShowDistricts) canvas.DrawPicture(_districtPic);
        if (ShowDistricts) DrawDistrictLabels(canvas, vw, x0, y0, x1, y1);
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
    /// zoom, and skipped when the district is too small to hold the text.</summary>
    void DrawDistrictLabels(SKCanvas canvas, float vw, float x0, float y0, float x1, float y1)
    {
        float sc = 1f / CamS;
        float size = Math.Clamp(vw / 90f, 13f, 22f) * sc;
        using var lab = new SKPaint { Typeface = _mono, TextSize = size, IsAntialias = true };

        // districts are shelf packed, so a label may spill into the gap beside
        // it. keep the right edge per row and drop whatever would collide -
        // a readable subset beats a complete but unreadable one
        var rowRight = new Dictionary<int, float>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < Data.Districts.Count; i++)
        {
            var d = Data.Districts[i];
            if (d.X > x1 || d.X + d.W < x0 || d.Y > y1 || d.Y + d.H < y0) continue;

            // "Forms" is useless: every project has one. show as much of the
            // path as fits, dropping segments from the left
            var shown = Shorten(d.Name, lab, d.W * 2.2f);
            if (shown is null) continue;

            // three districts all reading "Pages" name nothing. a repeat is
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

            // a label belongs above its district, except when that would put
            // it off the top of the window, where it is no label at all
            float ty = Math.Max(d.Y - 30 * sc, y0 + size * 1.2f);
            using (var chip = new SKPaint { Color = new SKColor(0x04, 0x07, 0x0f, 226), IsAntialias = false })
                canvas.DrawRect(d.X - 6 * sc, ty - size * 0.95f, wide + 12 * sc, size * 1.35f, chip);

            lab.Color = Review is not null
                ? new SKColor(0x4a, 0x5a, 0x66)          // reviewing: stay out of the way
                : DistrictHue(i, 55, 68);
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

    /// <summary>zoomed out, a changed file is a solid block. it is forced to at
    /// least a few pixels: at map zoom a card is thinner than a pixel and would
    /// flicker in and out as you move.</summary>
    void DrawReviewBlocks(SKCanvas canvas)
    {
        float min = 9f / CamS;
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
            var col = ChurnColor(change);
            glow.Color = col.WithAlpha(120);
            canvas.DrawRect(x - min * 0.35f, y - min * 0.35f, w + min * 0.7f, h + min * 0.7f, glow);
            fill.Color = col;
            canvas.DrawRect(x, y, w, h, fill);
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

    /// <summary>added and removed lines inside one card.</summary>
    void DrawReviewLines(SKCanvas canvas, FileRec f)
    {
        if (Review is null || !Review.ByPath.TryGetValue(f.P, out var change)) return;
        using var add = new SKPaint { Color = AddCol.WithAlpha(120), IsAntialias = false };
        using var del = new SKPaint { Color = DelCol, IsAntialias = false };
        using var addGlow = new SKPaint
        {
            Color = AddCol.WithAlpha(70), IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Data.LineH * 1.6f),
        };

        foreach (var line in change.AddedLines)
        {
            float y = Data.HeaderH + line * Data.LineH;
            canvas.DrawRect(0, y, f.W, Data.LineH, addGlow);
            canvas.DrawRect(0, y, f.W, Data.LineH, add);
        }
        foreach (var line in change.RemovedAt)
            canvas.DrawRect(0, Data.HeaderH + line * Data.LineH - 0.6f, f.W, 1.2f, del);
    }

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
    const float WinHeadH = 26;
    const char LF = (char)10;
    const char TabChar = (char)9;

    /// <summary>the line range a window shows, clamped to the file.</summary>
    public (int From, int To) RangeOf(BoardItem it, FileRec f)
    {
        int from = Math.Clamp(it.Line, 0, Math.Max(0, f.N - 1));
        int to = it.EndLine >= from ? Math.Min(it.EndLine, Math.Max(0, f.N - 1)) : Math.Max(0, f.N - 1);
        return (from, to);
    }

    public float ItemHeight(BoardItem it)
    {
        if (it.Kind == "arrow") return 0;
        if (it.Kind == "shape") return Math.Max(40, it.H > 0 ? it.H : 240);
        if (it.Kind == "image") return Math.Max(20, it.H > 0 ? it.H : it.W * 0.6f);
        if (it.Kind == "note")
            return Math.Max(it.H > 0 ? it.H : 0,
                NotePad * 2 + Math.Max(1, WrapNote(it).Count) * NoteLineH);

        int i = it.File is null ? -1 : ResolveFile(it.File, it.Key);
        if (i < 0) return 64;
        var f = Data.Files[i];
        var (from, to) = RangeOf(it, f);
        return WinHeadH + (to - from + 1) * Data.LineH * (it.W / f.W);
    }

    List<string> WrapNote(BoardItem it)
    {
        var wrapped = Wrap(it.Text ?? "", it.W - NotePad * 2, NoteFont, out var paint);
        paint.Dispose();
        return wrapped;
    }

    /// <summary>word wrap. the caller owns the returned paint.</summary>
    List<string> Wrap(string text, float max, float size, out SKPaint paint)
    {
        var outLines = new List<string>();
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

    /// <summary>topmost item under a board-space point, or null.</summary>
    public BoardItem? ItemAt(float wx, float wy)
    {
        if (ActiveBoard is null) return null;
        for (int i = ActiveBoard.Items.Count - 1; i >= 0; i--)
        {
            var it = ActiveBoard.Items[i];
            if (it.Kind == "arrow") continue;
            // the whole box counts, plus a little slack, so a click near an
            // edge still lands on the thing you were aiming at
            float pad = 3f / CamS;
            if (wx >= it.X - pad && wx <= it.X + it.W + pad &&
                wy >= it.Y - pad && wy <= it.Y + ItemHeight(it) + pad) return it;
        }
        return null;
    }

    /// <summary>every item the rubberband touches.</summary>
    public IEnumerable<BoardItem> ItemsIn(SKRect r)
    {
        if (ActiveBoard is null) yield break;
        foreach (var it in ActiveBoard.Items)
        {
            float h = ItemHeight(it);
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
        using var noteEdge = new SKPaint { Color = new SKColor(0xff, 0xd1, 0x66, 200), IsAntialias = false };
        using var noteText = new SKPaint { Color = new SKColor(0xe8, 0xd8, 0xa8), Typeface = _mono, TextSize = NoteFont, IsAntialias = true };
        using var missing = new SKPaint { Color = new SKColor(0x6a, 0x2b, 0x2b), IsAntialias = false };
        if (_charW == 0) _charW = code.MeasureText("0");

        using var pixels = new SKPaint { FilterQuality = SKFilterQuality.Medium, IsAntialias = true };
        using var shapeFill = new SKPaint { Color = new SKColor(0x5f, 0xd3, 0xf3, 16), IsAntialias = false };
        using var shapeEdge = new SKPaint { Color = new SKColor(0x5f, 0xd3, 0xf3, 120), IsStroke = true, StrokeWidth = 0, IsAntialias = false };

        foreach (var it in board.Items)
        {
            if (it.Kind == "arrow") continue;          // drawn after, on top
            if (it.Kind == "shape")
            {
                var col = ParseColor(it.Color, new SKColor(0x5f, 0xd3, 0xf3));
                float sh = ItemHeight(it);
                shapeFill.Color = col.WithAlpha(16);
                shapeEdge.Color = col.WithAlpha(150);
                canvas.DrawRect(it.X, it.Y, it.W, sh, shapeFill);
                canvas.DrawRect(it.X, it.Y, it.W, sh, shapeEdge);
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
                var wrapped = WrapNote(it);
                float h = ItemHeight(it);
                var accent = ParseColor(it.Color, new SKColor(0xff, 0xd1, 0x66));
                noteEdge.Color = accent;
                noteText.Color = accent.WithAlpha(235);
                canvas.Save();
                canvas.Translate(it.X, it.Y);
                canvas.DrawRect(0, 0, it.W, h, noteBg);
                canvas.DrawRect(0, 0, 3, h, noteEdge);
                for (int li = 0; li < wrapped.Count; li++)
                    canvas.DrawText(wrapped[li], NotePad, NotePad + (li + 1) * NoteLineH - 4, noteText);
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

            head.Color = DistrictHue(_districtOf[i], 40, 22);
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
            DrawBoardPicks(canvas, f, i);
            canvas.Restore();
            canvas.Restore();

            DrawBoardNoteText(canvas, it, f, from, to, k);
        }

        DrawArrows(canvas, board);
        DrawPickedItems(canvas, board);
        DrawRubberband(canvas);

        canvas.Restore();
    }

    /// <summary>an arrow being dragged out, in board coordinates.</summary>
    public (SKPoint A, SKPoint B)? ArrowDraft;

    void DrawArrows(SKCanvas canvas, Board board)
    {
        using var line = new SKPaint { IsStroke = true, StrokeWidth = 2.5f, IsAntialias = true };
        foreach (var it in board.Items)
        {
            if (it.Kind != "arrow") continue;

            line.Color = ParseColor(it.Color, new SKColor(0xff, 0xd1, 0x66));
            var p1 = new SKPoint(it.X, it.Y);
            var p2 = new SKPoint(it.X2, it.Y2);
            canvas.DrawLine(p1, p2, line);

            // a small head, turned to face the direction of travel
            float ang = MathF.Atan2(p2.Y - p1.Y, p2.X - p1.X);
            const float head = 11f;
            canvas.DrawLine(p2, new SKPoint(
                p2.X - head * MathF.Cos(ang - 0.4f), p2.Y - head * MathF.Sin(ang - 0.4f)), line);
            canvas.DrawLine(p2, new SKPoint(
                p2.X - head * MathF.Cos(ang + 0.4f), p2.Y - head * MathF.Sin(ang + 0.4f)), line);

            // ends are grabbable once the arrow is picked
            if (!Picked.Contains(it.Id)) continue;
            float sc0 = 1f / CamS;
            using var knob = new SKPaint { Color = new SKColor(0x5f, 0xd3, 0xf3), IsAntialias = true };
            canvas.DrawCircle(p1, 5 * sc0, knob);
            canvas.DrawCircle(p2, 5 * sc0, knob);
        }

        if (ArrowDraft is { } draft)
        {
            line.Color = new SKColor(0x5f, 0xd3, 0xf3, 200);
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
            if (Math.Abs(wx - it.X) < r && Math.Abs(wy - it.Y) < r) return (it, 1);
            if (Math.Abs(wx - it.X2) < r && Math.Abs(wy - it.Y2) < r) return (it, 2);
        }
        return null;
    }

    /// <summary>an arrow near the point, for picking one without a box.</summary>
    public BoardItem? ArrowAt(float wx, float wy)
    {
        if (ActiveBoard is null) return null;
        float tol = 7f / CamS;
        foreach (var it in ActiveBoard.Items)
        {
            if (it.Kind != "arrow") continue;
            float dx = it.X2 - it.X, dy = it.Y2 - it.Y;
            float len2 = dx * dx + dy * dy;
            if (len2 < 0.01f) continue;
            float t = Math.Clamp(((wx - it.X) * dx + (wy - it.Y) * dy) / len2, 0, 1);
            float px = it.X + dx * t, py = it.Y + dy * t;
            if (Math.Abs(wx - px) < tol && Math.Abs(wy - py) < tol) return it;
        }
        return null;
    }

    /// <summary>where a line between two items meets the first one's box.</summary>
    SKPoint Edge(BoardItem from, BoardItem to)
    {
        float fh = ItemHeight(from), th = ItemHeight(to);
        float cx = from.X + from.W / 2, cy = from.Y + fh / 2;
        float tx = to.X + to.W / 2, ty = to.Y + th / 2;
        float dx = tx - cx, dy = ty - cy;
        if (Math.Abs(dx) < 0.01f && Math.Abs(dy) < 0.01f) return new SKPoint(cx, cy);

        float sx = dx == 0 ? float.MaxValue : from.W / 2 / Math.Abs(dx);
        float sy = dy == 0 ? float.MaxValue : fh / 2 / Math.Abs(dy);
        float t = Math.Min(sx, sy);
        return new SKPoint(cx + dx * t, cy + dy * t);
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
            // the grip resizes the box and leaves the font alone. a file window
            // has no grip: its size comes from the range of lines it shows
            if (Resizable(it))
                canvas.DrawRect(it.X + it.W - 5 * sc, it.Y + h - 5 * sc, 10 * sc, 10 * sc, grip);
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
            float h = ItemHeight(it);
            if (wx < it.X || wx > it.X + it.W || wy < it.Y || wy > it.Y + h) continue;

            var (from, to) = RangeOf(it, f);
            float k = it.W / f.W;
            int line = from + (int)((wy - it.Y - WinHeadH) / (Data.LineH * k));
            return (it, i, Math.Clamp(line, from, to));
        }
        return null;
    }

    public static bool Resizable(BoardItem it) => it.Kind is "note" or "shape" or "image";

    /// <summary>the resize grip of a picked item, if the point is on one.</summary>
    public BoardItem? GripAt(float wx, float wy)
    {
        if (ActiveBoard is null) return null;
        float sc = 1f / CamS;
        for (int i = ActiveBoard.Items.Count - 1; i >= 0; i--)
        {
            var it = ActiveBoard.Items[i];
            if (!Picked.Contains(it.Id) || !Resizable(it)) continue;
            float h = ItemHeight(it);
            if (wx >= it.X + it.W - 9 * sc && wx <= it.X + it.W + 5 * sc &&
                wy >= it.Y + h - 9 * sc && wy <= it.Y + h + 5 * sc) return it;
        }
        return null;
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
        float y1 = board.Items.Max(i => i.Y + ItemHeight(i));
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
            board.Items.Max(i => Math.Max(i.Y + ItemHeight(i), i.Kind == "arrow" ? i.Y2 : i.Y)));
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
        var baseDistricts = Data.Districts;
        float W = Data.World.W + 400, H = Data.World.H + 400;
        var files = new List<FileRec>(baseFiles.Count * 9);
        var districts = new List<District>(baseDistricts.Count * 9);
        for (int gy = 0; gy < 3; gy++)
        for (int gx = 0; gx < 3; gx++)
        {
            foreach (var f in baseFiles)
                files.Add(new FileRec { P = f.P, N = f.N, D = f.D, X = f.X + gx * W, Y = f.Y + gy * H, W = f.W, H = f.H });
            foreach (var d in baseDistricts)
                districts.Add(new District { Name = d.Name, X = d.X + gx * W, Y = d.Y + gy * H, W = d.W, H = d.H });
        }
        Data = new Scan
        {
            Root = Data.Root, LineH = Data.LineH, HeaderH = Data.HeaderH,
            World = new WorldSize { W = W * 3, H = H * 3 },
            Districts = districts, Files = files,
        };
        Rebuild();
    }

    public void Dispose()
    {
        foreach (var p in _bars.Values) p.Dispose();
        _districtPic?.Dispose();
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

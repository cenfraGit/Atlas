using System.Text.Json;

namespace Atlas;

/// <summary>walks a repo and produces the canvas model: per-line metadata plus
/// the layout. layout lives here rather than in the view so a scan is
/// reproducible and diffable.</summary>
public static class Scanner
{
    // line kinds, matched by the colours in Scene
    public const int Blank = 0, Comment = 1, Decl = 2, Str = 3, Code = 4;

    const float CardW = 240, LineH = 3, HeaderH = 22, Pad = 16;
    const float GroupPad = 64, ShelfW = 26000;

    static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", "dist", "build", ".vs", ".vscode",
        ".idea", "packages", "target", "venv", "__pycache__", ".next", "out",
        "artifacts", "TestResults", "coverage",
    };

    static readonly HashSet<string> Ext = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".go", ".rs", ".java", ".kt",
        ".c", ".h", ".cpp", ".hpp", ".rb", ".php", ".swift", ".scala", ".sql",
        ".xaml", ".html", ".css", ".scss", ".json", ".yaml", ".yml", ".md", ".sh",
    };

    /// <summary>files worth reading that have no extension, or whose name is
    /// all extension. A repo's configuration is part of how it works, and a
    /// commit that adds .gitignore had nothing on the map to light up.</summary>
    static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gitignore", ".gitattributes", ".editorconfig", ".dockerignore",
        ".npmrc", ".nvmrc", ".prettierrc", ".eslintrc",
        "Dockerfile", "Makefile", "CMakeLists.txt", "Directory.Build.props",
    };

    static bool WantedName(string name) =>
        Names.Contains(name) || Ext.Contains(Path.GetExtension(name));

    static readonly string[] DeclWords =
    [
        "class", "interface", "struct", "record", "enum", "namespace", "function",
        "def", "func", "fn", "impl", "trait", "module", "type",
    ];

    static int Classify(ReadOnlySpan<char> line)
    {
        var t = line.Trim();
        if (t.Length == 0) return Blank;
        if (t.StartsWith("//") || t.StartsWith("#") || t.StartsWith("/*") ||
            t.StartsWith("*") || t.StartsWith("--") || t.StartsWith("<!--")) return Comment;

        foreach (var w in DeclWords)
        {
            int at = t.IndexOf(w, StringComparison.Ordinal);
            if (at < 0) continue;
            // whole word only, so "classic" does not read as a declaration
            bool leftOk = at == 0 || !char.IsLetterOrDigit(t[at - 1]) && t[at - 1] != '_';
            int end = at + w.Length;
            bool rightOk = end >= t.Length || !char.IsLetterOrDigit(t[end]) && t[end] != '_';
            if (leftOk && rightOk) return Decl;
        }
        if (t.IndexOfAny(['"', '\'', '`']) >= 0) return Str;
        return Code;
    }

    /// <summary>true when the scanner would include this repo-relative path.
    /// used to filter a commit's tree the same way a folder walk is filtered.</summary>
    public static bool Wanted(string relPath)
    {
        var parts = relPath.Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (Skip.Contains(parts[i])) return false;
            if (parts[i].StartsWith('.') && parts[i] != ".github") return false;
        }
        var name = parts[^1];
        if (name.StartsWith('.') && !Names.Contains(name)) return false;
        return WantedName(name);
    }

    /// <summary>build a map from files that are not on disk - a commit's tree.</summary>
    public static Scan BuildFrom(string root, IReadOnlyDictionary<string, string[]> files)
    {
        var recs = new List<FileRec>(files.Count);
        foreach (var (path, lines) in files)
        {
            var d = new int[lines.Length * 3];
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].AsSpan();
                var trimmed = line.TrimStart();
                d[i * 3] = Math.Min(line.Length - trimmed.Length, 60);
                d[i * 3 + 1] = Math.Min(trimmed.TrimEnd().Length, 200);
                d[i * 3 + 2] = Classify(line);
            }
            recs.Add(new FileRec { P = path, N = lines.Length, D = d });
        }
        recs.Sort((a, b) => string.CompareOrdinal(a.P, b.P));

        var districts = Layout(recs);
        return new Scan
        {
            Root = root,
            LineH = LineH,
            HeaderH = HeaderH,
            World = new WorldSize
            {
                W = districts.Count == 0 ? 1 : districts.Max(x => x.X + x.W),
                H = districts.Count == 0 ? 1 : districts.Max(x => x.Y + x.H),
            },
            Districts = districts,
            Files = recs,
        };
    }

    public static Scan Build(string root)
    {
        root = Path.GetFullPath(root);
        var files = new List<FileRec>();
        Walk(new DirectoryInfo(root), root, files);
        files.Sort((a, b) => string.CompareOrdinal(a.P, b.P));

        var districts = Layout(files);
        return new Scan
        {
            Root = root,
            LineH = LineH,
            HeaderH = HeaderH,
            World = new WorldSize
            {
                W = districts.Count == 0 ? 1 : districts.Max(d => d.X + d.W),
                H = districts.Count == 0 ? 1 : districts.Max(d => d.Y + d.H),
            },
            Districts = districts,
            Files = files,
        };
    }

    static void Walk(DirectoryInfo dir, string root, List<FileRec> into)
    {
        FileSystemInfo[] entries;
        try { entries = dir.GetFileSystemInfos(); }
        catch { return; }

        foreach (var e in entries)
        {
            if (e is DirectoryInfo sub)
            {
                if (sub.Name.StartsWith('.') && sub.Name != ".github") continue;
                if (Skip.Contains(sub.Name)) continue;
                Walk(sub, root, into);
                continue;
            }
            var file = (FileInfo)e;
            // Wanted() filters a commit's tree the same way; the two must agree
            if (!WantedName(file.Name) || file.Length > 2_000_000) continue;

            string[] lines;
            try { lines = File.ReadAllLines(file.FullName); }
            catch { continue; }

            var d = new int[lines.Length * 3];
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].AsSpan();
                var trimmed = line.TrimStart();
                d[i * 3] = Math.Min(line.Length - trimmed.Length, 60);
                d[i * 3 + 1] = Math.Min(trimmed.TrimEnd().Length, 200);
                d[i * 3 + 2] = Classify(line);
            }
            into.Add(new FileRec
            {
                P = Path.GetRelativePath(root, file.FullName).Replace('\\', '/'),
                N = lines.Length,
                D = d,
            });
        }
    }

    /// <summary>a district is one directory. files are gridded inside it and
    /// districts are shelf-packed left to right.</summary>
    static List<District> Layout(List<FileRec> files)
    {
        var groups = new SortedDictionary<string, List<FileRec>>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            int cut = f.P.LastIndexOf('/');
            var dir = cut < 0 ? "." : f.P[..cut];
            if (!groups.TryGetValue(dir, out var list)) groups[dir] = list = [];
            list.Add(f);
        }

        var districts = new List<District>(groups.Count);
        float shelfX = 0, shelfY = 0, shelfH = 0;

        foreach (var (dir, items) in groups)
        {
            int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(items.Count)));
            var colH = new float[cols];

            foreach (var f in items)
            {
                // a card is as tall as its file. It used to be capped, which
                // meant a long file's card was shorter than its own contents:
                // the bars stopped early and the text ran out of the box
                float h = HeaderH + f.N * LineH;
                int c = 0;
                for (int i = 1; i < cols; i++) if (colH[i] < colH[c]) c = i;
                f.X = c * (CardW + Pad);
                f.Y = colH[c];
                f.W = CardW;
                f.H = h;
                colH[c] += h + Pad;
            }

            float gw = cols * (CardW + Pad) - Pad;
            float gh = colH.Max() - Pad;

            if (shelfX > 0 && shelfX + gw > ShelfW)
            {
                shelfY += shelfH + GroupPad;
                shelfX = 0;
                shelfH = 0;
            }
            foreach (var f in items) { f.X += shelfX; f.Y += shelfY; }

            districts.Add(new District { Name = dir, X = shelfX, Y = shelfY, W = gw, H = gh });
            shelfX += gw + GroupPad;
            shelfH = Math.Max(shelfH, gh);
        }
        return districts;
    }

    public static void Save(Scan scan, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        JsonSerializer.Serialize(fs, scan);
    }
}

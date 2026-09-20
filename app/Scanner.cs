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

    /// <summary>never shown, at any setting. .git is machinery, and .atlas is
    /// your own notes about this repo - reading them as cards about your notes
    /// is a hall of mirrors.</summary>
    static readonly HashSet<string> Never = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".atlas",
    };

    /// <summary>build output and vendored dependencies: real files, but not
    /// this repo's code. Hidden by default, shown with the toggle.</summary>
    static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "dist", "build", "packages", "target",
        "venv", ".venv", "__pycache__", ".next", "out", "artifacts",
        "TestResults", "coverage", "vendor", "Pods", "DerivedData",
        ".vs", ".vscode", ".idea", ".gradle", ".cargo", ".terraform",
    };

    /// <summary>secrets, which it would be rude to paint on a wall, and the
    /// litter operating systems leave lying about. Hidden by default rather
    /// than never, because sometimes you really are editing one.</summary>
    static readonly HashSet<string> HiddenFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env", "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519", ".netrc", ".htpasswd",
        ".DS_Store", "Thumbs.db", "desktop.ini",
    };

    static readonly HashSet<string> SecretExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pem", ".key", ".p12", ".pfx", ".jks", ".keystore",
    };

    /// <summary>not text, and no amount of sniffing will make it so. The sniff
    /// catches everything this list misses; the list is here to save opening
    /// a 40MB video to find that out.</summary>
    static readonly HashSet<string> Binary = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib", ".a", ".lib", ".o", ".obj", ".pdb",
        ".class", ".jar", ".war", ".pyc", ".pyo", ".wasm", ".node",
        ".zip", ".gz", ".tgz", ".bz2", ".xz", ".7z", ".rar", ".tar", ".nupkg",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tiff", ".psd",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".mp3", ".mp4", ".wav", ".ogg", ".flac", ".avi", ".mov", ".mkv", ".webm",
        ".ttf", ".otf", ".woff", ".woff2", ".eot",
        ".db", ".sqlite", ".sqlite3", ".mdb", ".bin", ".dat", ".iso", ".dmg",
    };

    /// <summary>true when the scanner would include this repo-relative path.
    ///
    /// Path only. Whether a file with an unremarkable name turns out to be
    /// binary is a question about its contents, and the two callers answer it
    /// differently: the folder walk sniffs the bytes, and a commit's tree asks
    /// libgit2. What they must agree on is this, the part decided by the name.</summary>
    public static bool Wanted(string relPath, ScanOptions? options = null)
    {
        var opts = options ?? ScanOptions.Default;
        var parts = relPath.Split('/');

        // ask about each directory on the way down, the way the walk does, so
        // a rule like `build/` prunes a whole branch rather than being tested
        // against every leaf under it
        int at = 0;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!WantedDir(parts[i], opts)) return false;
            at += parts[i].Length + 1;
            if (opts.IsIgnored(relPath[..at])) return false;
        }

        return WantedFile(parts[^1], opts) && !opts.IsIgnored(relPath);
    }

    static bool WantedDir(string name, ScanOptions opts)
    {
        if (Never.Contains(name)) return false;
        if (opts.ShowHidden) return true;
        if (Noise.Contains(name)) return false;
        // .github holds real work; other dot directories are tooling
        return !name.StartsWith('.') || name.Equals(".github", StringComparison.OrdinalIgnoreCase);
    }

    static bool WantedFile(string name, ScanOptions opts)
    {
        if (Never.Contains(name)) return false;
        if (Binary.Contains(Path.GetExtension(name))) return false;

        if (!opts.ShowHidden && IsHidden(name)) return false;

        // everything else is assumed to be text until its bytes say otherwise.
        // An allowlist of extensions left .org, .el, .nix and anything else a
        // little unusual off the map, and off it silently
        return true;
    }

    static bool IsHidden(string name) =>
        HiddenFiles.Contains(name) ||
        SecretExt.Contains(Path.GetExtension(name)) ||
        name.StartsWith(".env", StringComparison.OrdinalIgnoreCase);

    /// <summary>a NUL byte in the first few kilobytes. It is what git uses,
    /// and it is right about everything an extension list is wrong about.</summary>
    public static bool LooksBinary(ReadOnlySpan<byte> head)
    {
        foreach (var b in head) if (b == 0) return true;
        return false;
    }

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

    public static Scan Build(string root, ScanOptions? options = null)
    {
        var opts = options ?? ScanOptions.Default;
        root = Path.GetFullPath(root);
        var files = new List<FileRec>();
        int skipped = 0;
        Walk(new DirectoryInfo(root), root, "", files, opts, ref skipped);
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
            Skipped = skipped,
            ShowingHidden = opts.ShowHidden,
        };
    }

    const long TooBig = 2_000_000;

    /// <summary>prefix is the repo-relative path of dir, with a trailing
    /// slash, so .gitignore can be asked about a whole directory before its
    /// contents are read at all.</summary>
    static void Walk(DirectoryInfo dir, string root, string prefix,
        List<FileRec> into, ScanOptions opts, ref int skipped)
    {
        FileSystemInfo[] entries;
        try { entries = dir.GetFileSystemInfos(); }
        catch { return; }

        foreach (var e in entries)
        {
            if (e is DirectoryInfo sub)
            {
                if (!WantedDir(sub.Name, opts)) continue;
                var here = prefix + sub.Name + "/";
                if (opts.IsIgnored(here)) continue;
                Walk(sub, root, here, into, opts, ref skipped);
                continue;
            }

            var file = (FileInfo)e;
            // Wanted() filters a commit's tree by the same rule; the two have
            // to agree on everything decided by the name
            if (!WantedFile(file.Name, opts)) continue;
            if (opts.IsIgnored(prefix + file.Name)) continue;
            if (file.Length > TooBig) { skipped++; continue; }

            var lines = ReadText(file.FullName);
            if (lines is null) { skipped++; continue; }

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

    /// <summary>a file's lines, or null when it is not text.
    ///
    /// The bytes are read once and sniffed before being decoded, so an
    /// unknown extension does not have to be guessed at - and a binary is
    /// never handed to the tokeniser as mojibake. Lines are split exactly as
    /// File.ReadAllLines splits them, because a card's height is its line
    /// count and everything stored in .atlas points at line numbers.</summary>
    static string[]? ReadText(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (LooksBinary(bytes.AsSpan(0, Math.Min(bytes.Length, 8192)))) return null;

            var lines = new List<string>();
            using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
            while (reader.ReadLine() is { } line) lines.Add(line);
            return lines.ToArray();
        }
        catch { return null; }
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

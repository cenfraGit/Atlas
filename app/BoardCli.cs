using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace Atlas;

/// <summary>`atlas board`: making and editing boards from a command line,
/// for an agent documenting a codebase.
///
/// A board's json is not something to write by hand. A window carries an
/// anchor - symbol, offset, a fingerprint of the lines round it, a file key -
/// and a box round a method needs the exact height of that method's lines at
/// that window's scale. An agent writing the file would guess both, and the
/// board would drift the first time the code moved. So the commands talk in
/// ids, files, symbols and line numbers, and the real anchoring code works
/// out the rest.</summary>
public static class BoardCli
{
    public const string Help = """
        atlas board - make and edit Atlas boards from the command line

        usage
          atlas boards [--repo DIR]                       list the boards
          atlas board "<board>" <command> [args]          one command
          atlas board "<board>" < script.txt              one command per line, all or nothing
          atlas board help

          --repo defaults to the current folder. <board> is a board's name or id.
          Lines starting with # in a script are comments. Quote anything with spaces;
          \n inside text is a line break.

        board
          new [--group G] [--replace]        make the board (--replace empties an existing one)
          show                               outline: every item, where it is, warnings
          render out.png [--stop N | --frame a,b] [--px 1600]
                                             draw the board (or one stop) to an image - look at it

        items - every item has an id you choose; later commands refer to it
          window <id> <file> [<symbol>] [--lines 40-72] [--width 620]
              a window onto real code. <file> is a path or a unique end of one
              (Auth.cs, src/Auth.cs). <symbol> is a declaration: Login,
              AuthService.Login, or Login(2) for the overload with two
              parameters. No symbol and no --lines shows the whole file. Line
              numbers are 1-based, as in an editor. Code reads at its natural
              size at width 620; wider scales it up.
          note <id> "<text>" [--on <window>:<where>] [--width 360] [--text-size 8]
              a card of text. --on puts it beside that window at that code.
          label <id> "<text>" [--text-size 24] [--width W]
              a heading: words with no card, as wide as its words unless --width.
          box <id> [--around <window>:<where>] [--shape rect|ellipse|diamond]
                   [--text T] [--text-size S] [--size 300x140] [--fill none|border|<colour>]
              a shape. --around frames lines of a window and stays on that code
              as it changes.
          arrow <id> <from> <to> [--from-side top|right|bottom|left] [--to-side ...]
              a connector tied to two items; it follows them when they move.

          <where> is a symbol in that window's file, or lines: 51 or 51-60.

        text size is in board units: code is 6, and so is any text not given one.
        Notes read well at 8-11, headings at 18-40.

        placing - on window, note, label and box (default: right of the last item)
          --right-of X   --left-of X   --below X   --above X   [--gap 60]
          --row X        same top as X, right of everything already in X's row
          --at x,y       raw board coordinates, if you really need them
          Anything placed this way is nudged down until it overlaps nothing.

        editing
          set <id> [--color C] [--fill F] [--text T] [--width W] [--text-size S]
          move <id> <placing>                a window takes what is drawn on it along
          rm <id>

        tours
          stop "<title>" [--frame a,b,...] [--at N] [--pad 80]
              a tour stop framing those items (the whole board without --frame).
              It follows them: move or resize them and the stop reframes.
              Stops play in order; --at N inserts at position N (1-based).
          unstop N

        colours: amber cyan green red violet slate, or #rrggbb.

        A good board: a label as a title, windows onto the few methods that
        matter (a symbol, not a whole file), a box --around the lines a note
        talks about with the note --on the same code, arrows for what calls
        what, then a stop per step of the story. Render it and look before
        calling it done.
        """;

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int pid);

    /// <summary>runs `board` or `boards` and returns the exit code.</summary>
    public static int Run(string[] args)
    {
        // a WinExe has no console of its own: output to a pipe arrives
        // anyway, but typed at a prompt it would go nowhere
        if (OperatingSystem.IsWindows() && !Console.IsOutputRedirected) AttachConsole(-1);

        var rest = args.Skip(1).ToList();
        string repo = Directory.GetCurrentDirectory();
        int r = rest.IndexOf("--repo");
        if (r >= 0)
        {
            if (r + 1 >= rest.Count) return Fail("--repo needs a folder");
            repo = App.RepoFrom([rest[r + 1]]) ?? "";
            if (repo.Length == 0) return Fail($"not a folder: {rest[r + 1]}");
            rest.RemoveRange(r, 2);
        }

        if (args[0] == "boards")
        {
            var store = BoardStore.Load(repo);
            if (store.Boards.Count == 0) Console.WriteLine($"no boards in {repo}");
            foreach (var g in store.Groups())
                foreach (var b in store.Boards.Where(b => b.Group == g).OrderBy(b => b.Order))
                    Console.WriteLine($"{(g.Length == 0 ? "" : g + " / ")}{b.Name}  [{b.Id}]  {b.Items.Count} items, {b.Stops.Count} stops");
            return 0;
        }
        if (rest.Count == 0 || rest[0] is "help" or "--help" or "-h") { Console.WriteLine(Help); return 0; }

        var session = new Session(repo, rest[0]);
        try
        {
            if (rest.Count > 1)
            {
                session.Do(rest.Skip(1).ToList());
            }
            else
            {
                int n = 0;
                string? line;
                while ((line = Console.In.ReadLine()) is not null)
                {
                    n++;
                    var words = Split(line);
                    if (words.Count == 0 || words[0].StartsWith('#')) continue;
                    try { session.Do(words); }
                    catch (CliError e) { throw new CliError($"line {n}: {e.Message}\n  {line.Trim()}"); }
                }
            }
            session.Commit();
            return 0;
        }
        catch (CliError e)
        {
            session.Abandon();
            return Fail(e.Message + (rest.Count > 1 ? "" : "\nnothing was saved"));
        }
        finally
        {
            session.Dispose();
        }
    }

    static int Fail(string message)
    {
        Console.Error.WriteLine("error: " + message);
        return 1;
    }

    sealed class CliError(string message) : Exception(message);

    /// <summary>a command line split into words: spaces separate, double or
    /// single quotes group, a backslash before a quote keeps it, and \n is a
    /// line break - a script has one command per line, so a note of several
    /// lines had no other way in.</summary>
    public static List<string> Split(string line)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        char quote = '\0';
        bool any = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length && line[i + 1] is '"' or '\'') { sb.Append(line[++i]); any = true; }
            else if (c == '\\' && i + 1 < line.Length && line[i + 1] == 'n') { sb.Append('\n'); i++; any = true; }
            else if (quote != '\0') { if (c == quote) quote = '\0'; else sb.Append(c); }
            else if (c is '"' or '\'') { quote = c; any = true; }
            else if (char.IsWhiteSpace(c)) { if (any) words.Add(sb.ToString()); sb.Clear(); any = false; }
            else { sb.Append(c); any = true; }
        }
        if (any) words.Add(sb.ToString());
        return words;
    }

    /// <summary>one board, open for a run of commands. Nothing is written
    /// until <see cref="Commit"/>, so a script that fails half way leaves the
    /// board as it was and can simply be run again.</summary>
    sealed class Session : IDisposable
    {
        const float Gap = 60;

        readonly string _repo;
        readonly string _name;
        readonly BoardStore _store;
        Scene? _scene;
        Board? _board;
        bool _made;
        BoardItem? _last;

        public Session(string repo, string name)
        {
            _repo = repo;
            _name = name;
            _store = BoardStore.Load(repo);
            _board = _store.Boards.FirstOrDefault(b => b.Id == name)
                     ?? _store.Boards.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>the scan is only taken once something needs a file, so
        /// listing and removing stay instant on a big repo.</summary>
        Scene Scene
        {
            get
            {
                if (_scene is not null) return _scene;
                using var ignore = GitIgnore.For(_repo);
                _scene = new Scene(Scanner.Build(_repo, new ScanOptions { Ignored = ignore is null ? null : ignore.Ignored }));
                _scene.ActiveBoard = _board;
                return _scene;
            }
        }

        Board Board => _board ?? throw new CliError(
            $"no board \"{_name}\" in {_repo} - start with: new" +
            (_store.Boards.Count > 0 ? "\n  boards here: " + string.Join(", ", _store.Boards.Select(b => b.Name)) : ""));

        public void Commit()
        {
            if (_board is null) return;
            foreach (var stop in _board.Stops) Reframe(stop);
            // refused when the board changed on disk while this ran - the
            // app saving it, most likely - rather than saved over
            if (!_store.Save(_board))
                throw new CliError($"\"{_board.Name}\" changed on disk while this ran, or could not be written - run it again");
        }

        /// <summary>a stop made by framing items is put round them again,
        /// wherever they are now. One whose items are all gone keeps the
        /// region it had.</summary>
        void Reframe(Stop stop)
        {
            if (stop.Items is null) return;
            var items = stop.Items.Count == 0
                ? _board!.Items
                : _board!.Items.Where(i => stop.Items.Contains(i.Id)).ToList();
            if (items.Count == 0) return;
            var r = Around(items, stop.Pad);
            (stop.X, stop.Y, stop.W, stop.H) = (r.MidX, r.MidY, r.Width, r.Height);
        }

        public void Abandon()
        {
            if (_made && _board is not null) _store.Delete(_board);
        }

        public void Dispose() => _scene?.Dispose();

        public void Do(List<string> words)
        {
            var (pos, opt) = Parse(words.Skip(1));
            switch (words[0])
            {
                case "new": New(opt); break;
                case "show": Show(); break;
                case "render": Render(pos, opt); break;
                case "window": Window(pos, opt); break;
                case "note": Words("note", pos, opt, 360); break;
                case "label": Words("text", pos, opt, 520); break;
                case "box": Box(pos, opt); break;
                case "arrow": Arrow(pos, opt); break;
                case "set": Set(pos, opt); break;
                case "move": Move(pos, opt); break;
                case "rm": Remove(pos); break;
                case "stop": Stop(pos, opt); break;
                case "unstop": Unstop(pos); break;
                case "help": Console.WriteLine(Help); break;
                default: throw new CliError($"unknown command \"{words[0]}\" - see: atlas board help");
            }
        }

        static (List<string> Pos, Dictionary<string, string> Opt) Parse(IEnumerable<string> words)
        {
            var pos = new List<string>();
            var opt = new Dictionary<string, string>();
            var list = words.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].StartsWith("--") || list[i].Length == 2) { pos.Add(list[i]); continue; }
                var key = list[i][2..];
                if (key is "replace") { opt[key] = ""; continue; }
                if (i + 1 >= list.Count) throw new CliError($"--{key} needs a value");
                opt[key] = list[++i];
            }
            return (pos, opt);
        }

        // ---- board ----

        void New(Dictionary<string, string> opt)
        {
            if (_board is not null)
            {
                if (!opt.ContainsKey("replace"))
                    throw new CliError($"board \"{_board.Name}\" already exists - add --replace to empty it, or edit it as it is");
                Console.WriteLine($"emptied: {_board.Items.Count} items and {_board.Stops.Count} stops removed");
                _board.Items.Clear();
                _board.Stops.Clear();
            }
            else
            {
                _board = _store.Create(_name);
                _made = true;
            }
            if (opt.TryGetValue("group", out var g)) _board.Group = g;
            if (_scene is not null) _scene.ActiveBoard = _board;
            Console.WriteLine($"board \"{_board.Name}\" [{_board.Id}]");
        }

        void Show()
        {
            var b = Board;
            Console.WriteLine($"board \"{b.Name}\" [{b.Id}]{(b.Group.Length > 0 ? " in " + b.Group : "")}");
            foreach (var it in b.Items) Console.WriteLine("  " + Describe(it));
            if (b.Stops.Count > 0) Console.WriteLine("tour");
            for (int i = 0; i < b.Stops.Count; i++)
            {
                var s = b.Stops[i];
                Console.WriteLine($"  {i + 1}. {s.Name}  centre {F(s.X)},{F(s.Y)}  {F(s.W)}x{F(s.H)}");
            }
            var warnings = Warnings().ToList();
            foreach (var w in warnings) Console.WriteLine("warning: " + w);
            if (warnings.Count == 0) Console.WriteLine("no warnings (checked: overlaps, arrows crossing items, loose ties)");
        }

        string Describe(BoardItem it)
        {
            var r = BoxOf(it);
            string where = $"at {F(r.Left)},{F(r.Top)} {F(r.Width)}x{F(r.Height)}";
            string colour = it.Color is null ? "" : $" {it.Color}";
            switch (it.Kind)
            {
                case "file":
                {
                    int i = it.File is null ? -1 : Scene.ResolveFile(it.File, it.Key);
                    if (i < 0) return $"window {it.Id}  missing: {it.File}  {where}";
                    var (from, to) = Scene.RangeOf(it, Scene.Data.Files[i]);
                    var sym = it.Symbol is null ? "" : $" ({Clean(it.Symbol)})";
                    return $"window {it.Id}  {Scene.Data.Files[i].P}:{from + 1}-{to + 1}{sym}  {where}";
                }
                case "arrow":
                    return $"arrow {it.Id}  {it.From ?? "(loose)"} -> {it.To ?? "(loose)"}{colour}";
                case "note" or "text":
                    return $"{(it.Kind == "text" ? "label" : "note")} {it.Id}  \"{Short(it.Text)}\"  {where}{Pinned(it)}{colour}";
                default:
                    var text = string.IsNullOrEmpty(it.Text) ? "" : $" \"{Short(it.Text)}\"";
                    return $"{(it.Kind == "shape" ? "box" : it.Kind)} {it.Id}{text}  {where}{Pinned(it)}{colour}";
            }
        }

        string Pinned(BoardItem it)
        {
            if (it.Host is null) return "";
            var host = Board.Items.FirstOrDefault(w => w.Id == it.Host);
            if (host?.File is null) return $"  on {it.Host}";
            int i = Scene.ResolveFile(host.File, host.Key);
            if (i < 0) return $"  on {it.Host}";
            var (from, _) = Scene.RangeOf(host, Scene.Data.Files[i]);
            float step = Scene.LineStepIn(host, Scene.Data.Files[i]);
            int line = from + (int)MathF.Floor((it.Y - host.Y - Scene.WinHeadH) / step);
            return $"  on {it.Host} line {line + 1}";
        }

        /// <summary>overlaps that are probably mistakes: a drawing on the
        /// window it is pinned to is the point, and a frame round other things
        /// is too, so only solid things covering each other count.</summary>
        IEnumerable<string> Warnings()
        {
            var solid = Board.Items.Where(i => i.Kind is "file" or "note" or "text" or "image").ToList();
            for (int a = 0; a < solid.Count; a++)
                for (int b = a + 1; b < solid.Count; b++)
                {
                    var (x, y) = (solid[a], solid[b]);
                    if (x.Host == y.Id || y.Host == x.Id) continue;
                    var (rx, ry) = (BoxOf(x), BoxOf(y));
                    if (rx.IntersectsWith(ry)) yield return $"{x.Id} overlaps {y.Id}";
                }
            foreach (var it in Board.Items.Where(i => i.Kind == "arrow"))
            {
                foreach (var end in new[] { it.From, it.To })
                    if (end is not null && Board.Items.All(i => i.Id != end))
                        yield return $"arrow {it.Id} is tied to {end}, which is not on the board";

                // sampled rather than solved: a few dozen points along the
                // shaft, against each box shrunk a little so an end resting
                // on its own item's edge does not count
                var (a, b) = Scene.ArrowEnds(it);
                foreach (var o in solid)
                {
                    if (o.Id == it.From || o.Id == it.To) continue;
                    var r = BoxOf(o);
                    r.Inflate(-4, -4);
                    for (int k = 1; k < 40; k++)
                        if (r.Contains(a.X + (b.X - a.X) * k / 40f, a.Y + (b.Y - a.Y) * k / 40f))
                        {
                            yield return $"arrow {it.Id} crosses {o.Id}";
                            break;
                        }
                }
            }
        }

        void Render(List<string> pos, Dictionary<string, string> opt)
        {
            if (pos.Count == 0) throw new CliError("render needs a file to write: render out.png");
            var b = Board;
            var scene = Scene;
            scene.ActiveBoard = b;

            SKRect region;
            if (opt.TryGetValue("stop", out var n))
            {
                var s = StopAt(n);
                region = new SKRect(s.X - s.W / 2, s.Y - s.H / 2, s.X + s.W / 2, s.Y + s.H / 2);
            }
            else
            {
                var items = opt.TryGetValue("frame", out var ids) ? Items(ids) : b.Items;
                if (items.Count == 0) throw new CliError("the board is empty - nothing to render");
                region = Around(items, 80);
            }

            int px = opt.TryGetValue("px", out var p) ? Int(p, "--px") : 1600;
            px = Math.Clamp(px, 200, 6000);
            int py = (int)Math.Clamp(px * region.Height / Math.Max(1, region.Width), 200, 6000);
            scene.CamS = Math.Min(px / region.Width, py / region.Height);
            scene.CamX = region.MidX;
            scene.CamY = region.MidY;

            using var bmp = new SKBitmap(px, py);
            using var canvas = new SKCanvas(bmp);
            // file text loads off this thread; draw until every window has
            // its code, or the image shows empty windows
            var files = b.Items.Where(i => i.Kind == "file" && i.File is not null)
                .Select(i => scene.ResolveFile(i.File!, i.Key)).Where(i => i >= 0)
                .Select(i => scene.Data.Files[i].P).ToList();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            do
            {
                scene.Draw(canvas, px, py);
                if (files.All(f => scene.LinesOf(f) is not null)) break;
                Thread.Sleep(20);
            } while (DateTime.UtcNow < deadline);
            scene.Draw(canvas, px, py);

            var path = Path.GetFullPath(pos[0]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var image = SKImage.FromBitmap(bmp))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 90))
            using (var file = File.Create(path))
                data.SaveTo(file);
            Console.WriteLine($"rendered {px}x{py} at zoom {scene.CamS:0.##} -> {path}" +
                              (scene.CamS < 0.9f ? "  (code is unreadable below zoom ~0.9: render a stop or --frame one window to read it)" : ""));
        }

        // ---- items ----

        BoardItem NewItem(List<string> pos, string kind)
        {
            if (pos.Count == 0) throw new CliError("give the item an id first, e.g. " + kind + " login ...");
            var id = pos[0];
            if (id.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_')))
                throw new CliError($"id \"{id}\" should be letters, digits, - and _ only");
            if (Board.Items.Any(i => i.Id == id))
                throw new CliError($"there is already an item \"{id}\" - use set or move to change it, or rm it first");
            return new BoardItem { Id = id, Kind = kind };
        }

        void Window(List<string> pos, Dictionary<string, string> opt)
        {
            var it = NewItem(pos, "file");
            if (pos.Count < 2) throw new CliError("window needs a file: window <id> <file> [<symbol>]");
            var f = FileFor(pos[1]);
            var full = FullPath(f);
            int last = Math.Max(0, f.N - 1);

            int from = 0, to = -1;
            if (opt.TryGetValue("lines", out var lines)) (from, to) = LinesFrom(lines, last);
            else if (pos.Count > 2) (from, to) = SymbolIn(full, f.P, pos[2]);

            it.File = f.P;
            it.Key = Scene.KeyFor(f.P);
            it.Line = from;
            it.EndLine = to;
            it.W = opt.TryGetValue("width", out var w) ? Float(w, "--width") : 620;
            Place(it, opt);
            Board.Items.Add(it);
            Scene.Reanchor(it);
            _last = it;
            Console.WriteLine(Describe(it));
        }

        void Words(string kind, List<string> pos, Dictionary<string, string> opt, float width)
        {
            var it = NewItem(pos, kind);
            if (pos.Count < 2) throw new CliError($"{(kind == "text" ? "label" : kind)} needs its text in quotes");
            it.Text = pos[1];
            if (opt.TryGetValue("text-size", out var s)) it.Size = Float(s, "--text-size");
            it.W = opt.TryGetValue("width", out var w) ? Float(w, "--width")
                // a heading wrapped because nobody could know how wide its
                // words were: here there is no draw loop, so measure them
                : kind == "text" ? it.Text.Split('\n').Max(l => Scene.TextWidth(l, Scene.SizeOf(it))) + 8
                : width;
            if (opt.TryGetValue("color", out var c)) it.Color = Colour(c);
            it.H = 0;

            if (opt.TryGetValue("on", out var on))
            {
                var (host, a, _) = Target(on);
                it.X = host.X + host.W + 40;
                it.Y = LineY(host, a);
                Board.Items.Add(it);
                Free(it, host);
            }
            else
            {
                Place(it, opt);
                Board.Items.Add(it);
            }
            Scene.PinOver(it);
            _last = it;
            Console.WriteLine(Describe(it));
        }

        void Box(List<string> pos, Dictionary<string, string> opt)
        {
            var shape = opt.GetValueOrDefault("shape", "rect");
            var it = NewItem(pos, shape switch
            {
                "rect" or "box" or "rectangle" => "shape",
                "ellipse" or "diamond" => shape,
                _ => throw new CliError($"--shape is rect, ellipse or diamond, not {shape}"),
            });
            if (opt.TryGetValue("text", out var t)) it.Text = t;
            if (opt.TryGetValue("text-size", out var ts)) it.Size = Float(ts, "--text-size");
            it.Color = opt.TryGetValue("color", out var c) ? Colour(c) : "#ffd166";
            if (opt.TryGetValue("fill", out var fill)) it.Fill = Fill(fill);

            if (opt.TryGetValue("around", out var around))
            {
                var (host, a, b) = Target(around);
                // inside the window rather than round its edge: a drawing is
                // pinned by the window under its top left corner
                it.X = host.X + 2;
                it.W = host.W - 4;
                // a hair inside the first and last line, so the pin, which
                // floors, lands on those lines and not the ones beside them
                it.Y = LineY(host, a) + 0.05f;
                it.H = LineY(host, b + 1) - it.Y + 0.05f;
                Board.Items.Add(it);
            }
            else
            {
                (it.W, it.H) = (300, 140);
                if (opt.TryGetValue("size", out var size))
                {
                    var parts = size.Split('x');
                    if (parts.Length != 2) throw new CliError("--size is WxH, e.g. 300x140");
                    (it.W, it.H) = (Float(parts[0], "--size"), Float(parts[1], "--size"));
                }
                Place(it, opt);
                Board.Items.Add(it);
            }
            Scene.PinOver(it);
            _last = it;
            Console.WriteLine(Describe(it));
        }

        void Arrow(List<string> pos, Dictionary<string, string> opt)
        {
            var it = NewItem(pos, "arrow");
            if (pos.Count < 3) throw new CliError("arrow needs two items: arrow <id> <from> <to>");
            it.From = Item(pos[1]).Id;
            it.To = Item(pos[2]).Id;
            it.W = 0;
            if (opt.TryGetValue("from-side", out var fs)) it.FromSide = Side(fs);
            if (opt.TryGetValue("to-side", out var ts)) it.ToSide = Side(ts);
            if (opt.TryGetValue("color", out var c)) it.Color = Colour(c);
            Board.Items.Add(it);
            Console.WriteLine(Describe(it));
        }

        void Set(List<string> pos, Dictionary<string, string> opt)
        {
            if (pos.Count == 0) throw new CliError("set <id> --color ... ");
            var it = Item(pos[0]);
            if (opt.Count == 0) throw new CliError("set needs something to change: --color --fill --text --width --text-size");
            foreach (var (k, v) in opt)
                switch (k)
                {
                    case "color": it.Color = Colour(v); break;
                    case "fill": it.Fill = Fill(v); break;
                    case "text": it.Text = v; break;
                    case "width": it.W = Float(v, "--width"); break;
                    case "text-size": it.Size = Float(v, "--text-size"); break;
                    default: throw new CliError($"set cannot change --{k}");
                }
            Console.WriteLine($"set {it.Id}: {string.Join(", ", opt.Keys)}");
        }

        void Move(List<string> pos, Dictionary<string, string> opt)
        {
            if (pos.Count == 0) throw new CliError("move <id> --right-of X (or --below, --at x,y ...)");
            var it = Item(pos[0]);
            if (it.Kind == "arrow") throw new CliError("an arrow goes where its ends are - move those instead");
            float x = it.X, y = it.Y;
            Place(it, opt);
            float dx = it.X - x, dy = it.Y - y;
            (it.X, it.Y) = (x, y);
            Scene.Move(it, dx, dy);
            if (it.Kind == "file")
                foreach (var on in Board.Items.Where(i => i.Host == it.Id)) Scene.Move(on, dx, dy);
            else Scene.PinOver(it);
            Console.WriteLine(Describe(it));
        }

        void Remove(List<string> pos)
        {
            if (pos.Count == 0) throw new CliError("rm <id>");
            var it = Item(pos[0]);
            Scene.Remove(Board, [it]);
            Console.WriteLine($"removed {it.Id}");
        }

        void Stop(List<string> pos, Dictionary<string, string> opt)
        {
            if (pos.Count == 0) throw new CliError("stop \"<title>\" [--frame a,b]");
            var items = opt.TryGetValue("frame", out var ids) ? Items(ids) : Board.Items;
            if (items.Count == 0) throw new CliError("nothing to frame - the board is empty");
            float pad = opt.TryGetValue("pad", out var p) ? Float(p, "--pad") : 80;
            var r = Around(items, pad);
            var stop = new Stop
            {
                Name = pos[0], X = r.MidX, Y = r.MidY, W = r.Width, H = r.Height, Pad = pad,
                Items = ids is null ? [] : items.Select(i => i.Id).ToList(),
            };
            int at = opt.TryGetValue("at", out var a) ? Math.Clamp(Int(a, "--at") - 1, 0, Board.Stops.Count) : Board.Stops.Count;
            Board.Stops.Insert(at, stop);
            Console.WriteLine($"stop {at + 1}. {stop.Name}");
        }

        void Unstop(List<string> pos)
        {
            if (pos.Count == 0) throw new CliError("unstop N");
            var s = StopAt(pos[0]);
            Board.Stops.Remove(s);
            Console.WriteLine($"removed stop \"{s.Name}\"");
        }

        // ---- placing ----

        /// <summary>where a new or moved item goes: next to another one, at
        /// raw coordinates, or right of the last thing added. Nudged down
        /// until it covers nothing, except at raw coordinates, which are
        /// taken as meant.</summary>
        void Place(BoardItem it, Dictionary<string, string> opt)
        {
            float gap = opt.TryGetValue("gap", out var g) ? Float(g, "--gap") : Gap;
            if (opt.TryGetValue("at", out var at))
            {
                var xy = at.Split(',');
                if (xy.Length != 2) throw new CliError("--at is x,y");
                (it.X, it.Y) = (Float(xy[0], "--at"), Float(xy[1], "--at"));
                return;
            }

            float h = BoxOf(it).Height;
            if (opt.TryGetValue("right-of", out var id)) { var o = BoxOf(Item(id)); (it.X, it.Y) = (o.Right + gap, o.Top); }
            else if (opt.TryGetValue("left-of", out id)) { var o = BoxOf(Item(id)); (it.X, it.Y) = (o.Left - gap - it.W, o.Top); }
            else if (opt.TryGetValue("below", out id)) { var o = BoxOf(Item(id)); (it.X, it.Y) = (o.Left, o.Bottom + gap); }
            else if (opt.TryGetValue("above", out id)) { var o = BoxOf(Item(id)); (it.X, it.Y) = (o.Left, o.Top - gap - h); }
            else if (opt.TryGetValue("row", out id))
            {
                // the row is everything level with X and to the right of it,
                // so a note hung off a window further down does not start a
                // staircase the way --right-of that note would
                var o = BoxOf(Item(id));
                float right = Board.Items.Where(i => i != it && i.Kind != "arrow" && i.Host is null)
                    .Select(BoxOf).Where(b => b.Left >= o.Left && b.Top < o.Top + Math.Max(h, 1) && b.Bottom > o.Top)
                    .Select(b => b.Right).DefaultIfEmpty(o.Right).Max();
                (it.X, it.Y) = (right + gap, o.Top);
            }
            else
            {
                var prev = _last ?? Board.Items.LastOrDefault(i => i.Kind != "arrow" && i.Host is null && i != it);
                if (prev is null) (it.X, it.Y) = (0, 0);
                else { var o = BoxOf(prev); (it.X, it.Y) = (o.Right + gap, o.Top); }
            }
            Free(it, null);
        }

        /// <summary>push an item down past anything solid it lands on.</summary>
        void Free(BoardItem it, BoardItem? host)
        {
            for (int n = 0; n < 500; n++)
            {
                var me = BoxOf(it);
                // what is pinned onto a window is meant to overlap it, and is
                // covered by the window anyway
                var hit = Board.Items.FirstOrDefault(o => o != it && o != host && o.Kind != "arrow" &&
                                                          o.Host is null && BoxOf(o).IntersectsWith(me));
                if (hit is null) return;
                Scene.Move(it, 0, BoxOf(hit).Bottom + 24 - me.Top);
            }
        }

        /// <summary>how big an item is. Measures a note's words, which only
        /// the draw loop may do in the app - here there is no other thread.</summary>
        SKRect BoxOf(BoardItem it) => it.Kind is "note" or "text"
            ? new SKRect(it.X, it.Y, it.X + it.W, it.Y + Scene.ItemHeight(it))
            : Scene.BoundsOf(it);

        SKRect Around(List<BoardItem> items, float pad)
        {
            var r = BoxOf(items[0]);
            foreach (var it in items.Skip(1)) r.Union(BoxOf(it));
            r.Inflate(pad, pad);
            return r;
        }

        // ---- looking things up ----

        BoardItem Item(string id) => Board.Items.FirstOrDefault(i => i.Id == id)
            ?? throw new CliError($"no item \"{id}\" on this board" +
                                  (Board.Items.Count > 0 ? " - ids: " + string.Join(", ", Board.Items.Select(i => i.Id)) : ""));

        List<BoardItem> Items(string ids) => ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Item).ToList();

        Stop StopAt(string n)
        {
            int i = Int(n, "stop number") - 1;
            if (i < 0 || i >= Board.Stops.Count) throw new CliError($"no stop {n} - the board has {Board.Stops.Count}");
            return Board.Stops[i];
        }

        FileRec FileFor(string arg)
        {
            var q = arg.Replace('\\', '/').TrimStart('.', '/');
            var files = Scene.Data.Files;
            var exact = files.FirstOrDefault(f => string.Equals(f.P, q, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
            var ends = files.Where(f => f.P.EndsWith("/" + q, StringComparison.OrdinalIgnoreCase)).ToList();
            if (ends.Count == 1) return ends[0];
            if (ends.Count > 1)
                throw new CliError($"\"{arg}\" matches {ends.Count} files - say which: " + string.Join(", ", ends.Take(6).Select(f => f.P)));
            var name = Path.GetFileNameWithoutExtension(q);
            var near = files.Where(f => f.P.Contains(name, StringComparison.OrdinalIgnoreCase)).Take(6).Select(f => f.P).ToList();
            throw new CliError($"no file \"{arg}\" in the repo" + (near.Count > 0 ? " - similar: " + string.Join(", ", near) : ""));
        }

        string FullPath(FileRec f) => Path.Combine(Scene.Data.Root, f.P.Replace('/', Path.DirectorySeparatorChar));

        static (int From, int To) LinesFrom(string spec, int last)
        {
            var parts = spec.Split('-');
            if (parts.Length is < 1 or > 2 || !int.TryParse(parts[0], out int a) ||
                !int.TryParse(parts[^1], out int b) || a < 1 || b < a)
                throw new CliError($"lines are 51 or 51-60, 1-based, not \"{spec}\"");
            if (a - 1 > last) throw new CliError($"line {a} is past the end of the file ({last + 1} lines)");
            return (a - 1, Math.Min(b - 1, last));
        }

        /// <summary>a declaration by the name a person would write: Login,
        /// AuthService.Login, or Login(2) to pick an overload. Roslyn's names
        /// carry an arity on every method, which nobody wants to spell out.</summary>
        static (int From, int To) SymbolIn(string full, string rel, string query)
        {
            if (!Symbols.Supports(full)) throw new CliError($"symbols are only known in C# files - use --lines for {rel}");
            var syms = Symbols.ForFile(full);
            bool arity = query.Contains('(');
            var hits = syms.Where(s =>
            {
                var name = arity ? s.Name : Clean(s.Name);
                return name == query || name.EndsWith("." + query, StringComparison.Ordinal);
            }).ToList();
            if (hits.Count == 1) return (hits[0].StartLine, hits[0].EndLine);
            if (hits.Count > 1)
                throw new CliError($"\"{query}\" is {hits.Count} declarations in {rel} - pick one: " +
                                   string.Join(", ", hits.Select(h => ShortName(h.Name))));
            var near = syms.Where(s => Clean(s.Name).Contains(query, StringComparison.OrdinalIgnoreCase)).Select(s => ShortName(s.Name)).Take(8).ToList();
            if (near.Count == 0) near = syms.Select(s => ShortName(s.Name)).Take(12).ToList();
            throw new CliError($"no \"{query}\" in {rel}" + (near.Count > 0 ? " - there is: " + string.Join(", ", near) : ""));
        }

        /// <summary>a window and the lines of it meant by "w:Login" or
        /// "w:51-60", 0-based. They must be lines the window shows.</summary>
        (BoardItem Window, int From, int To) Target(string spec)
        {
            int colon = spec.IndexOf(':');
            if (colon < 0) throw new CliError($"\"{spec}\" should be <window>:<symbol or lines>, e.g. auth:Login or auth:51-60");
            var host = Item(spec[..colon]);
            if (host.Kind != "file" || host.File is null) throw new CliError($"{host.Id} is not a window");
            int i = Scene.ResolveFile(host.File, host.Key);
            if (i < 0) throw new CliError($"the file behind {host.Id} is missing: {host.File}");
            var f = Scene.Data.Files[i];
            var where = spec[(colon + 1)..];
            var (a, b) = where.Length > 0 && char.IsDigit(where[0])
                ? LinesFrom(where, Math.Max(0, Scene.LinesIn(f) - 1))
                : SymbolIn(FullPath(f), f.P, where);
            var (from, to) = Scene.RangeOf(host, f);
            if (a < from || b > to)
                throw new CliError($"{where} (lines {a + 1}-{b + 1}) is not in window {host.Id}, which shows lines {from + 1}-{to + 1}");
            return (host, a, b);
        }

        float LineY(BoardItem host, int line)
        {
            var f = Scene.Data.Files[Scene.ResolveFile(host.File!, host.Key)];
            var (from, _) = Scene.RangeOf(host, f);
            return host.Y + Scene.WinHeadH + (line - from) * Scene.LineStepIn(host, f);
        }

        static string Colour(string v)
        {
            var named = SceneView.Colours.FirstOrDefault(c => c.Name == v.ToLowerInvariant());
            if (named.Hex is not null) return named.Hex;
            if (SKColor.TryParse(v, out _) && v.StartsWith('#')) return v;
            throw new CliError($"colour is one of {string.Join(" ", SceneView.Colours.Select(c => c.Name))}, or #rrggbb - not \"{v}\"");
        }

        static string? Fill(string v) => v switch
        {
            "none" => BoardItem.NoFill,
            "border" => BoardItem.BorderFill,
            _ => Colour(v),
        };

        static int Side(string v) => v switch
        {
            "top" => Scene.Top,
            "right" => Scene.Right,
            "bottom" => Scene.Bottom,
            "left" => Scene.Left,
            _ => throw new CliError($"a side is top, right, bottom or left - not \"{v}\""),
        };

        static float Float(string v, string what) =>
            float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
                ? f : throw new CliError($"{what} wants a number, not \"{v}\"");

        static int Int(string v, string what) =>
            int.TryParse(v, out var i) ? i : throw new CliError($"{what} wants a whole number, not \"{v}\"");

        static string F(float v) => MathF.Round(v).ToString(CultureInfo.InvariantCulture);

        static string Short(string? s)
        {
            s = (s ?? "").ReplaceLineEndings("\\n");
            return s.Length > 50 ? s[..47] + "..." : s;
        }
    }

    /// <summary>Roslyn's name without the arities: Demo.Auth.Login(2) is
    /// Demo.Auth.Login.</summary>
    static string Clean(string name)
    {
        var sb = new StringBuilder(name.Length);
        int depth = 0;
        foreach (var c in name)
        {
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (depth == 0) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>the last two parts of a declaration's name, which is what
    /// fits in an error message: Auth.Login(2).</summary>
    static string ShortName(string name)
    {
        var parts = name.Split('.');
        return string.Join(".", parts.Skip(Math.Max(0, parts.Length - 2)));
    }
}

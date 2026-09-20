namespace Atlas;

/// <summary>run: dotnet run -- --annotationtest
/// the point of symbol anchoring is surviving edits, so that is what is tested.</summary>
public static class AnnotationTest
{
    static int _fails;

    static void Check(bool ok, string what)
    {
        if (!ok) { _fails++; Console.WriteLine("  FAIL: " + what); }
    }

    const string Source = """
namespace Demo.App;

public class Startup
{
    readonly string _name;

    public Startup(string name)
    {
        _name = name;
    }

    public void OnStartup()
    {
        Configure();
        Run();
    }

    void Configure()
    {
        var host = BuildHost();
        host.Start();
    }

    void Run() { }
}
""";

    // this file's own line endings must not leak into the fixture
    static string Lf(string s) => s.Replace("\r\n", "\n");
    static string[] Lines(string s) => Lf(s).Split('\n');

    static void Write(string path, string text)
    {
        File.WriteAllText(path, text);
        Symbols.Forget(path);
    }

    public static void Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cv_anntest_" + Guid.NewGuid().ToString("n")[..6]);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Startup.cs");
        try
        {
            var src = Lf(Source);
            Write(path, src);
            var lines = Lines(src);

            var syms = Symbols.ForFile(path);
            Check(syms.Any(s => s.Name == "Demo.App.Startup"), "finds the class");
            Check(syms.Any(s => s.Name == "Demo.App.Startup.OnStartup(0)"), "finds a method with its arity");
            Check(syms.Any(s => s.Name == "Demo.App.Startup..ctor(1)"), "finds the constructor");

            int target = Array.FindIndex(lines, l => l.Contains("host.Start()"));
            Check(target > 0, "found a line to annotate");
            var ann = Anchors.Create("Startup.cs", path, lines, target, "where the host actually starts");
            Check(ann.Symbol == "Demo.App.Startup.Configure(0)", $"anchors to the enclosing method, got '{ann.Symbol}'");
            Check(Anchors.Resolve(ann, path, lines) is { Kind: AnchorKind.Symbol } r0 && r0.Line == target,
                "resolves to the same line in an unchanged file");

            // THE test: insert code above, shifting every line down
            var edited = "// a new header comment\n" + string.Join("\n", Enumerable.Repeat("// filler", 20)) + "\n" + src;
            Write(path, edited);
            var editedLines = Lines(edited);
            var moved = Anchors.Resolve(ann, path, editedLines);
            Check(moved.Kind == AnchorKind.Symbol, $"still anchored by symbol, got {moved.Kind}");
            Check(editedLines[moved.Line].Contains("host.Start()"),
                $"lands on the same code after 21 lines were inserted above, got '{editedLines[moved.Line].Trim()}'");

            // a sibling method added inside the class must not shift it either
            var withMethod = edited.Replace("    void Run() { }",
                "    void Extra()\n    {\n        var x = 1;\n    }\n\n    void Run() { }");
            Write(path, withMethod);
            var withMethodLines = Lines(withMethod);
            Check(withMethodLines[Anchors.Resolve(ann, path, withMethodLines).Line].Contains("host.Start()"),
                "survives a sibling method being added");

            // reindenting must not break the context fingerprint
            var reindented = withMethod.Replace("        host.Start();", "            host.Start();");
            Write(path, reindented);
            var reindentedLines = Lines(reindented);
            Check(reindentedLines[Anchors.Resolve(ann, path, reindentedLines).Line].Contains("host.Start()"),
                "survives reindentation");

            // rename the method: the symbol is gone, context must find it
            var renamed = withMethod.Replace("void Configure()", "void ConfigureServices()")
                                    .Replace("Configure();", "ConfigureServices();");
            Write(path, renamed);
            var renamedLines = Lines(renamed);
            var byContext = Anchors.Resolve(ann, path, renamedLines);
            Check(byContext.Kind == AnchorKind.Context, $"falls back to context on rename, got {byContext.Kind}");
            Check(renamedLines[byContext.Line].Contains("host.Start()"), "context fallback lands on the right line");

            // delete the annotated lines but keep the method: we still know the
            // right declaration, so that is reported rather than discarded
            var deleted = withMethod.Replace("        var host = BuildHost();\n        host.Start();\n", "");
            Check(deleted != withMethod, "the deletion fixture actually changed the file");
            Write(path, deleted);
            var deletedLines = Lines(deleted);
            var drifted = Anchors.Resolve(ann, path, deletedLines);
            Check(drifted.Kind == AnchorKind.Drifted, $"reports drift when the lines are gone, got {drifted.Kind}");
            var configure = Symbols.ForFile(path).First(x => x.Name == "Demo.App.Startup.Configure(0)");
            Check(drifted.Line >= configure.StartLine && drifted.Line <= configure.EndLine,
                "a drifted anchor still points inside its declaration");

            // remove the declaration too: now there is nothing left to find
            var gone = deleted.Replace("    void Configure()\n    {\n    }\n", "");
            Check(gone != deleted, "the removal fixture actually changed the file");
            Write(path, gone);
            var orphan = Anchors.Resolve(ann, path, Lines(gone));
            Check(orphan.Kind == AnchorKind.Orphan, $"reports an orphan once the declaration is gone, got {orphan.Kind}");

            // a language we cannot parse still works by context alone
            var py = Path.Combine(dir, "notes.py");
            Write(py, "def hello():\n    print('hi')\n    return 1\n");
            var pyAnn = Anchors.Create("notes.py", py, Lines(File.ReadAllText(py)), 1, "python note");
            Check(pyAnn.Symbol is null, "no symbol for an unparsed language");
            Write(py, "import sys\nimport os\n\ndef hello():\n    print('hi')\n    return 1\n");
            var pyLines = Lines(File.ReadAllText(py));
            var pyMoved = Anchors.Resolve(pyAnn, py, pyLines);
            Check(pyMoved.Kind == AnchorKind.Context && pyLines[pyMoved.Line].Contains("print"),
                $"context anchoring carries other languages, got {pyMoved.Kind}");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        Console.WriteLine(_fails == 0 ? "PASS" : $"{_fails} FAILURES");
    }
}

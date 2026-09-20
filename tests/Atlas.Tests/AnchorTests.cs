namespace Atlas.Tests;

/// <summary>an annotation has to survive the code moving under it. Symbol
/// first, then a context fingerprint, then drift, then orphan - each rung of
/// that ladder gets its own test so a regression says which one broke.</summary>
public class AnchorTests
{
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

    static string Lf(string s) => s.Replace("\r\n", "\n");
    static string[] Lines(string s) => Lf(s).Split('\n');

    /// <summary>a file on disk plus the annotation under test, so every case
    /// starts from the same known state.</summary>
    sealed class Fixture : IDisposable
    {
        public readonly TempDir Dir = new("atlas_anchor");
        public readonly string Path;
        public readonly Annotation Ann;
        public readonly int TargetLine;

        public Fixture()
        {
            Path = Write(Lf(Source));
            var lines = Lines(Source);
            TargetLine = Array.FindIndex(lines, l => l.Contains("host.Start()"));
            Ann = Anchors.Create("Startup.cs", Path, lines, TargetLine, "where the host starts");
        }

        public string Write(string text)
        {
            var p = Dir.File("Startup.cs", text);
            Symbols.Forget(p);
            return p;
        }

        /// <summary>rewrite the file and resolve the annotation against it.</summary>
        public (Anchor Anchor, string[] Lines) Resolve(string text)
        {
            Write(Lf(text));
            var lines = Lines(text);
            return (Anchors.Resolve(Ann, Path, lines), lines);
        }

        public void Dispose() => Dir.Dispose();
    }

    [Fact]
    public void RoslynFindsTypesAndMembersWithArity()
    {
        using var f = new Fixture();
        var names = Symbols.ForFile(f.Path).Select(s => s.Name).ToList();

        Assert.Contains("Demo.App.Startup", names);
        Assert.Contains("Demo.App.Startup.OnStartup(0)", names);
        Assert.Contains("Demo.App.Startup..ctor(1)", names);
    }

    [Fact]
    public void AnnotatingAnchorsToTheEnclosingMethod()
    {
        using var f = new Fixture();
        Assert.Equal("Demo.App.Startup.Configure(0)", f.Ann.Symbol);
    }

    [Fact]
    public void AnUnchangedFileResolvesToTheSameLine()
    {
        using var f = new Fixture();
        var (anchor, _) = f.Resolve(Source);

        Assert.Equal(AnchorKind.Symbol, anchor.Kind);
        Assert.Equal(f.TargetLine, anchor.Line);
    }

    [Fact]
    public void SurvivesLinesInsertedAbove()
    {
        using var f = new Fixture();
        var header = "// a new header\n" + string.Join("\n", Enumerable.Repeat("// filler", 20)) + "\n";
        var (anchor, lines) = f.Resolve(header + Source);

        Assert.Equal(AnchorKind.Symbol, anchor.Kind);
        Assert.Contains("host.Start()", lines[anchor.Line]);
    }

    [Fact]
    public void SurvivesASiblingMethodBeingAdded()
    {
        using var f = new Fixture();
        var edited = Source.Replace("    void Run() { }",
            "    void Extra()\n    {\n        var x = 1;\n    }\n\n    void Run() { }");
        var (anchor, lines) = f.Resolve(edited);

        Assert.Equal(AnchorKind.Symbol, anchor.Kind);
        Assert.Contains("host.Start()", lines[anchor.Line]);
    }

    [Fact]
    public void SurvivesReindentation()
    {
        using var f = new Fixture();
        var (anchor, lines) = f.Resolve(Source.Replace("        host.Start();", "            host.Start();"));

        Assert.True(anchor.Resolved);
        Assert.Contains("host.Start()", lines[anchor.Line]);
    }

    [Fact]
    public void FallsBackToContextWhenTheMethodIsRenamed()
    {
        using var f = new Fixture();
        var renamed = Source.Replace("void Configure()", "void ConfigureServices()")
                            .Replace("Configure();", "ConfigureServices();");
        var (anchor, lines) = f.Resolve(renamed);

        Assert.Equal(AnchorKind.Context, anchor.Kind);
        Assert.Contains("host.Start()", lines[anchor.Line]);
    }

    [Fact]
    public void ReportsDriftWhenTheLinesAreGoneButTheMethodRemains()
    {
        using var f = new Fixture();
        var deleted = Source.Replace("        var host = BuildHost();\n        host.Start();\n", "");
        Assert.NotEqual(Source, deleted);

        var (anchor, _) = f.Resolve(deleted);
        Assert.Equal(AnchorKind.Drifted, anchor.Kind);

        // a drifted anchor still points inside the declaration it belongs to
        var configure = Symbols.ForFile(f.Path).First(s => s.Name == "Demo.App.Startup.Configure(0)");
        Assert.InRange(anchor.Line, configure.StartLine, configure.EndLine);
    }

    [Fact]
    public void ReportsAnOrphanOnceTheDeclarationIsGone()
    {
        using var f = new Fixture();
        var gone = Source
            .Replace("        var host = BuildHost();\n        host.Start();\n", "")
            .Replace("    void Configure()\n    {\n    }\n", "");

        var (anchor, _) = f.Resolve(gone);
        Assert.Equal(AnchorKind.Orphan, anchor.Kind);
        Assert.False(anchor.Resolved);
    }

    [Fact]
    public void AnEmptyFileIsAnOrphanRatherThanACrash()
    {
        using var f = new Fixture();
        var (anchor, _) = f.Resolve("");
        Assert.Equal(AnchorKind.Orphan, anchor.Kind);
    }

    [Fact]
    public void ALanguageRoslynCannotParseAnchorsByContextAlone()
    {
        using var dir = new TempDir("atlas_py");
        var path = dir.File("notes.py", "def hello():\n    print(1)\n    return 1\n");
        var ann = Anchors.Create("notes.py", path, File.ReadAllLines(path), 1, "python note");

        Assert.Null(ann.Symbol);

        dir.File("notes.py", "import sys\nimport os\n\ndef hello():\n    print(1)\n    return 1\n");
        var lines = File.ReadAllLines(path);
        var anchor = Anchors.Resolve(ann, path, lines);

        Assert.Equal(AnchorKind.Context, anchor.Kind);
        Assert.Contains("print", lines[anchor.Line]);
    }

    [Fact]
    public void AnAnnotationRecordsItsSpanAndFingerprint()
    {
        using var f = new Fixture();
        var lines = Lines(Source);
        var range = Anchors.Create("Startup.cs", f.Path, lines, f.TargetLine, f.TargetLine + 1, "two lines");

        Assert.Equal(2, range.Span);
        Assert.Equal(f.TargetLine, range.Line);
        Assert.Equal(FileKeys.Of(lines), range.Key);
        Assert.NotNull(range.Context);
    }

    [Fact]
    public void ARangeGivenBackwardsIsNormalised()
    {
        using var f = new Fixture();
        var lines = Lines(Source);
        var ann = Anchors.Create("Startup.cs", f.Path, lines, f.TargetLine + 2, f.TargetLine, "backwards");

        Assert.Equal(f.TargetLine, ann.Line);
        Assert.Equal(3, ann.Span);
    }

    [Fact]
    public void ContextIsUnaffectedByWhitespace()
    {
        var lines = Lines(Source);
        var spaced = lines.Select(l => "  " + l + "  ").ToArray();
        Assert.Equal(Anchors.ContextOf(lines, 5), Anchors.ContextOf(spaced, 5));
    }

    [Fact]
    public void InnermostPicksTheTightestEnclosingSymbol()
    {
        using var f = new Fixture();
        var sym = Symbols.Innermost(Symbols.ForFile(f.Path), f.TargetLine);

        Assert.NotNull(sym);
        Assert.Equal("Demo.App.Startup.Configure(0)", sym!.Value.Name);
    }

    [Fact]
    public void InnermostWidensToTheNamespaceOutsideAnyMember()
    {
        using var f = new Fixture();
        // a file-scoped namespace covers the whole file, so the declaration on
        // line 0 is the namespace - never a member that does not contain it
        var sym = Symbols.Innermost(Symbols.ForFile(f.Path), 0);

        Assert.NotNull(sym);
        Assert.DoesNotContain("(", sym!.Value.Name);
    }

    [Fact]
    public void InnermostFindsNothingWhenThereAreNoSymbols() =>
        Assert.Null(Symbols.Innermost([], 3));

    [Fact]
    public void SymbolsAreOnlyLookedForInLanguagesRoslynParses()
    {
        Assert.True(Symbols.Supports("app/Scene.cs"));
        Assert.False(Symbols.Supports("docs/readme.md"));
    }
}

using System.Text;

namespace Atlas.Tests;

/// <summary>what counts as part of the repo.
///
/// The scanner used to hold an allowlist of thirty extensions, so an .org
/// file, an .el, a .nix - anything a little unusual - was simply absent from
/// the map, and absent without saying so. The rule is now the other way
/// round: every text file is in, binaries are out, and the noisy directories
/// are hidden behind a toggle.</summary>
public class FileInclusionTests
{
    static List<string> Paths(TempDir dir, bool showHidden = false) =>
        Scanner.Build(dir.Path, new ScanOptions(showHidden)).Files.Select(f => f.P).ToList();

    // --- unusual languages ------------------------------------------------

    [Theory]
    [InlineData("notes.org")]
    [InlineData("init.el")]
    [InlineData("flake.nix")]
    [InlineData("Cargo.toml")]
    [InlineData("main.zig")]
    [InlineData("query.graphql")]
    [InlineData("deps.edn")]
    [InlineData("Makefile")]
    [InlineData("Dockerfile")]
    [InlineData("LICENSE")]
    [InlineData("CHANGELOG")]
    [InlineData(".gitignore")]
    [InlineData(".editorconfig")]
    public void AnyTextFileIsOnTheMap(string name)
    {
        using var dir = new TempDir();
        dir.File(name, "some text\nmore text\n");

        Assert.Contains(name, Paths(dir));
    }

    [Fact]
    public void TheOldFavouritesStillWork()
    {
        using var dir = new TempDir();
        dir.File("app/Program.cs", "class P { }");
        dir.File("web/index.ts", "export const x = 1;");
        dir.File("docs/readme.md", "# hello");

        Assert.Equal(3, Paths(dir).Count);
    }

    // --- binaries ---------------------------------------------------------

    [Theory]
    [InlineData("app.exe")]
    [InlineData("lib.dll")]
    [InlineData("thing.o")]
    [InlineData("photo.png")]
    [InlineData("archive.zip")]
    [InlineData("font.woff2")]
    [InlineData("data.sqlite")]
    public void BinariesAreLeftOutByExtension(string name)
    {
        using var dir = new TempDir();
        dir.File(name, "pretend contents");
        dir.File("keep.cs", "class K { }");

        Assert.Equal(["keep.cs"], Paths(dir));
    }

    [Fact]
    public void ABinaryWithAnInnocentNameIsCaughtByItsBytes()
    {
        using var dir = new TempDir();
        // no extension list anticipates every format; a NUL byte is what git
        // itself goes on
        File.WriteAllBytes(Path.Combine(dir.Path, "mystery.dat2"),
            [0x7f, 0x45, 0x4c, 0x46, 0x00, 0x01, 0x02, 0x00, 0x03]);
        dir.File("keep.cs", "class K { }");

        Assert.Equal(["keep.cs"], Paths(dir));
    }

    [Fact]
    public void AFileCaughtByItsBytesIsCounted()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(Path.Combine(dir.Path, "mystery.dat2"), [0x41, 0x00, 0x42]);
        dir.File("keep.cs", "class K { }");

        var scan = Scanner.Build(dir.Path);

        Assert.Single(scan.Files);
        Assert.Equal(1, scan.Skipped);
    }

    [Fact]
    public void NothingSkippedIsReportedAsNothing()
    {
        using var dir = new TempDir();
        dir.File("a.cs", "class A { }");

        Assert.Equal(0, Scanner.Build(dir.Path).Skipped);
    }

    [Fact]
    public void TextWithHighBytesIsStillText()
    {
        using var dir = new TempDir();
        // UTF-8 accents and box drawing are not NUL bytes
        File.WriteAllText(Path.Combine(dir.Path, "notes.org"),
            "* Título\n  ├── árbol\n  └── ñu\n", new UTF8Encoding(false));

        Assert.Contains("notes.org", Paths(dir));
    }

    [Fact]
    public void AnEmptyFileIsText()
    {
        using var dir = new TempDir();
        dir.File("empty.cs", "");

        Assert.Contains("empty.cs", Paths(dir));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BinariesAreOutAtEitherSetting(bool showHidden)
    {
        using var dir = new TempDir();
        dir.File("app.exe", "x");
        dir.File("keep.cs", "class K { }");

        Assert.DoesNotContain("app.exe", Paths(dir, showHidden));
    }

    // --- noise ------------------------------------------------------------

    [Fact]
    public void BuildOutputAndDependenciesAreHiddenByDefault()
    {
        using var dir = new TempDir();
        dir.File("app/Program.cs", "class P { }");
        dir.File("bin/Debug/App.cs", "class A { }");
        dir.File("obj/gen.cs", "class G { }");
        dir.File("node_modules/pkg/index.js", "module.exports = 1;");

        Assert.Equal(["app/Program.cs"], Paths(dir));
    }

    [Fact]
    public void TheToggleShowsThem()
    {
        using var dir = new TempDir();
        dir.File("app/Program.cs", "class P { }");
        dir.File("node_modules/pkg/index.js", "module.exports = 1;");

        var shown = Paths(dir, showHidden: true);

        Assert.Contains("app/Program.cs", shown);
        Assert.Contains("node_modules/pkg/index.js", shown);
    }

    [Fact]
    public void GitAndAtlasAreNeverShown()
    {
        using var dir = new TempDir();
        dir.File(".git/config", "[core]\n");
        dir.File(".atlas/boards/b.json", "{}");
        dir.File("app/Program.cs", "class P { }");

        // even with the toggle on: reading your own notes about this repo as
        // cards is a hall of mirrors
        Assert.Equal(["app/Program.cs"], Paths(dir, showHidden: true));
    }

    [Fact]
    public void GithubIsRealWorkAndStaysVisible()
    {
        using var dir = new TempDir();
        dir.File(".github/workflows/ci.yml", "on: push\n");
        dir.File(".husky/pre-commit", "npm test\n");

        var byDefault = Paths(dir);

        Assert.Contains(".github/workflows/ci.yml", byDefault);
        Assert.DoesNotContain(".husky/pre-commit", byDefault);
    }

    // --- secrets ----------------------------------------------------------

    [Theory]
    [InlineData(".env")]
    [InlineData(".env.local")]
    [InlineData("server.pem")]
    [InlineData("private.key")]
    [InlineData("id_rsa")]
    public void SecretsAreHiddenByDefault(string name)
    {
        using var dir = new TempDir();
        dir.File(name, "shhh");
        dir.File("keep.cs", "class K { }");

        Assert.Equal(["keep.cs"], Paths(dir));
    }

    [Fact]
    public void SecretsAreShownWithTheToggleBecauseSometimesYouAreEditingOne()
    {
        using var dir = new TempDir();
        dir.File(".env", "KEY=value\n");

        Assert.Contains(".env", Paths(dir, showHidden: true));
    }

    // --- the invariant ----------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhatTheWalkTakesIsExactlyWhatWantedTakes(bool showHidden)
    {
        // Wanted() filters a commit's tree and the walk filters the working
        // tree. Disagree, and a commit's changed files land nowhere on a map
        // built from the same rule
        using var dir = new TempDir();
        dir.File("app/Program.cs", "class P { }");
        dir.File("notes.org", "* hello");
        dir.File(".gitignore", "bin/\n");
        dir.File(".env", "KEY=1\n");
        dir.File("Makefile", "all:\n");
        dir.File("bin/Gen.cs", "class G { }");
        dir.File(".git/config", "[core]\n");
        dir.File("photo.png", "x");

        var opts = new ScanOptions(showHidden);
        foreach (var f in Scanner.Build(dir.Path, opts).Files)
            Assert.True(Scanner.Wanted(f.P, opts), $"the walk took {f.P} but Wanted() rejects it");
    }

    [Fact]
    public void WantedJudgesByPathAlone()
    {
        // content is the other caller's business: the walk sniffs bytes, a
        // commit's tree asks libgit2. Wanted only decides what a name settles
        Assert.True(Scanner.Wanted("src/mystery.dat2"));
        Assert.False(Scanner.Wanted("src/mystery.exe"));
    }

    [Theory]
    [InlineData("app/Program.cs", true)]
    [InlineData("notes.org", true)]
    [InlineData(".gitignore", true)]
    [InlineData("Makefile", true)]
    [InlineData(".github/workflows/ci.yml", true)]
    [InlineData(".env", false)]
    [InlineData("bin/Gen.cs", false)]
    [InlineData("node_modules/x/i.js", false)]
    [InlineData(".git/config", false)]
    [InlineData(".atlas/boards/b.json", false)]
    [InlineData("assets/logo.png", false)]
    public void WantedAtTheDefaultSetting(string path, bool wanted) =>
        Assert.Equal(wanted, Scanner.Wanted(path));

    [Fact]
    public void LooksBinaryIsAboutNulBytes()
    {
        Assert.False(Scanner.LooksBinary("plain text"u8));
        Assert.False(Scanner.LooksBinary([]));
        Assert.True(Scanner.LooksBinary([0x41, 0x00]));
    }

    // --- line counting ----------------------------------------------------

    [Fact]
    public void LinesAreCountedTheWayTheyAlwaysWere()
    {
        // a card's height is its line count and everything in .atlas points at
        // line numbers, so the new reader must not shift them by one
        using var dir = new TempDir();
        var path = dir.File("a.cs", "one\ntwo\nthree\n");

        var f = Scanner.Build(dir.Path).Files[0];

        Assert.Equal(File.ReadAllLines(path).Length, f.N);
        Assert.Equal(3, f.N);
    }

    [Fact]
    public void AFileWithNoTrailingNewlineCountsTheSame()
    {
        using var dir = new TempDir();
        var path = dir.File("a.cs", "one\ntwo");

        Assert.Equal(File.ReadAllLines(path).Length, Scanner.Build(dir.Path).Files[0].N);
    }
}

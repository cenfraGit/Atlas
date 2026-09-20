namespace Atlas.Tests;

/// <summary>the scan is the model everything else is laid out from, so what it
/// includes, how it classifies a line and where it puts a card are all load
/// bearing.</summary>
public class ScannerTests
{
    [Fact]
    public void IncludesSourceAndSkipsEverythingElse()
    {
        using var repo = SampleRepo.Build();
        var scan = Scanner.Build(repo.Path);
        var paths = scan.Files.Select(f => f.P).ToList();

        Assert.Contains("app/Program.cs", paths);
        Assert.Contains("app/ui/Panel.cs", paths);
        Assert.Contains("docs/readme.md", paths);

        Assert.DoesNotContain("bin/Generated.cs", paths);      // build output
        Assert.DoesNotContain("app/notes.txt", paths);         // not a source extension
        Assert.DoesNotContain(".hidden/Secret.cs", paths);     // hidden directory
    }

    [Fact]
    public void PathsAreRepoRelativeWithForwardSlashes()
    {
        using var repo = SampleRepo.Build();
        var scan = Scanner.Build(repo.Path);

        Assert.All(scan.Files, f => Assert.False(f.P.Contains((char)92), f.P));
        Assert.All(scan.Files, f => Assert.False(Path.IsPathRooted(f.P)));
    }

    [Fact]
    public void FilesComeOutInAStableOrder()
    {
        using var repo = SampleRepo.Build();
        var a = Scanner.Build(repo.Path).Files.Select(f => f.P).ToList();
        var b = Scanner.Build(repo.Path).Files.Select(f => f.P).ToList();

        Assert.Equal(a, b);
        Assert.Equal(a.OrderBy(p => p, StringComparer.Ordinal), a);
    }

    [Fact]
    public void LineCountAndPerLineDataAgree()
    {
        using var repo = SampleRepo.Build();
        var scan = Scanner.Build(repo.Path);
        var file = scan.Files.Single(f => f.P == SampleRepo.LongFile);

        Assert.Equal(SampleRepo.LongFileLines, file.N);
        Assert.Equal(file.N * 3, file.D.Length);
    }

    [Theory]
    [InlineData("", Scanner.Blank)]
    [InlineData("    ", Scanner.Blank)]
    [InlineData("// a note", Scanner.Comment)]
    [InlineData("# a note", Scanner.Comment)]
    [InlineData("/* a note", Scanner.Comment)]
    [InlineData("public class Panel", Scanner.Decl)]
    [InlineData("def hello():", Scanner.Decl)]
    [InlineData("func main() {", Scanner.Decl)]
    [InlineData("var s = \"hello\";", Scanner.Str)]
    [InlineData("x = y + 1;", Scanner.Code)]
    public void ClassifiesALine(string line, int expected)
    {
        using var dir = new TempDir();
        // a second line so that an empty first line is still a line to classify
        dir.File("a.cs", line + "\nend");
        var scan = Scanner.Build(dir.Path);
        Assert.Equal(expected, scan.Files.Single().D[2]);
    }

    [Fact]
    public void AWordInsideAnotherIsNotADeclaration()
    {
        using var dir = new TempDir();
        // "classic" contains "class"; whole-word matching is the point
        dir.File("a.cs", "var classic = 1;");
        var scan = Scanner.Build(dir.Path);
        Assert.NotEqual(Scanner.Decl, scan.Files.Single().D[2]);
    }

    [Fact]
    public void ConfigurationFilesAreOnTheMap()
    {
        using var dir = new TempDir();
        dir.File(".gitignore", "bin/\nobj/\n");
        dir.File(".editorconfig", "root = true\n");
        dir.File("Dockerfile", "FROM scratch\n");
        dir.File("app/Program.cs", "class P { }");

        var paths = Scanner.Build(dir.Path).Files.Select(f => f.P).ToList();

        // a commit that adds .gitignore had nothing on the map to light up
        Assert.Contains(".gitignore", paths);
        Assert.Contains(".editorconfig", paths);
        Assert.Contains("Dockerfile", paths);
    }

    [Fact]
    public void OtherDotfilesAreStillLeftOut()
    {
        using var dir = new TempDir();
        dir.File(".env", "SECRET=1\n");
        dir.File(".DS_Store", "junk");
        dir.File("app/Program.cs", "class P { }");

        var paths = Scanner.Build(dir.Path).Files.Select(f => f.P).ToList();

        Assert.DoesNotContain(".env", paths);
        Assert.DoesNotContain(".DS_Store", paths);
    }

    [Fact]
    public void HiddenDirectoriesAreStillSkipped()
    {
        using var dir = new TempDir();
        dir.File(".secret/Thing.cs", "class T { }");
        dir.File(".github/workflows/ci.yml", "on: push\n");

        var paths = Scanner.Build(dir.Path).Files.Select(f => f.P).ToList();

        Assert.DoesNotContain(".secret/Thing.cs", paths);
        Assert.Contains(".github/workflows/ci.yml", paths);
    }

    [Fact]
    public void WhatTheWalkTakesIsExactlyWhatWantedTakes()
    {
        // the two must agree or a commit's tree is filtered differently from
        // the working tree, and changed files land nowhere
        using var dir = new TempDir();
        dir.File(".gitignore", "bin/\n");
        dir.File(".env", "x=1\n");
        dir.File("Dockerfile", "FROM scratch\n");
        dir.File("app/Program.cs", "class P { }");
        dir.File("app/notes.txt", "no");
        dir.File("bin/Gen.cs", "class G { }");

        foreach (var f in Scanner.Build(dir.Path).Files)
            Assert.True(Scanner.Wanted(f.P), $"the walk took {f.P} but Wanted() rejects it");

        foreach (var rejected in new[] { ".env", "app/notes.txt", "bin/Gen.cs" })
            Assert.False(Scanner.Wanted(rejected), $"Wanted() takes {rejected} but the walk does not");
    }

    [Theory]
    [InlineData(".gitignore", true)]
    [InlineData(".editorconfig", true)]
    [InlineData("Dockerfile", true)]
    [InlineData("Makefile", true)]
    [InlineData(".env", false)]
    [InlineData("app/Program.cs", true)]
    [InlineData("docs/readme.md", true)]
    [InlineData(".github/workflows/ci.yml", true)]
    [InlineData("bin/Generated.cs", false)]
    [InlineData("app/obj/Debug/A.cs", false)]
    [InlineData("node_modules/pkg/index.js", false)]
    [InlineData(".vs/settings.cs", false)]
    [InlineData("app/notes.txt", false)]
    [InlineData("app/.hidden.cs", false)]
    public void WantedMatchesTheFolderWalk(string path, bool wanted) =>
        Assert.Equal(wanted, Scanner.Wanted(path));

    [Fact]
    public void ADistrictPerDirectory()
    {
        using var repo = SampleRepo.Build();
        var scan = Scanner.Build(repo.Path);

        Assert.Equal(["app", "app/ui", "docs"], scan.Districts.Select(d => d.Name).Order());
        Assert.All(scan.Districts, d => Assert.True(d.W > 0 && d.H > 0, $"{d.Name} has no area"));
    }

    [Fact]
    public void CardsSitInsideTheirDistrict()
    {
        using var repo = SampleRepo.Build();
        var scan = Scanner.Build(repo.Path);
        var byName = scan.Districts.ToDictionary(d => d.Name);

        foreach (var f in scan.Files)
        {
            int cut = f.P.LastIndexOf('/');
            var d = byName[cut < 0 ? "." : f.P[..cut]];
            Assert.InRange(f.X, d.X, d.X + d.W);
            Assert.InRange(f.Y, d.Y, d.Y + d.H + 1);
        }
    }

    [Fact]
    public void CardsInOneDirectoryDoNotOverlap()
    {
        using var repo = SampleRepo.Build();
        var scan = Scanner.Build(repo.Path);

        var all = scan.Files.ToList();
        for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
            {
                var (a, b) = (all[i], all[j]);
                bool apart = a.X + a.W <= b.X || b.X + b.W <= a.X ||
                             a.Y + a.H <= b.Y || b.Y + b.H <= a.Y;
                Assert.True(apart, $"{a.P} overlaps {b.P}");
            }
    }

    [Fact]
    public void TheWorldBoundsEveryDistrict()
    {
        using var repo = SampleRepo.Build();
        var scan = Scanner.Build(repo.Path);

        Assert.All(scan.Districts, d =>
        {
            Assert.True(d.X + d.W <= scan.World.W);
            Assert.True(d.Y + d.H <= scan.World.H);
        });
    }

    [Fact]
    public void AnEmptyRepoScansToAnEmptyWorldInsteadOfThrowing()
    {
        using var dir = new TempDir();
        var scan = Scanner.Build(dir.Path);

        Assert.Empty(scan.Files);
        Assert.Empty(scan.Districts);
        Assert.Equal(1, scan.World.W);
        Assert.Equal(1, scan.World.H);
    }

    [Fact]
    public void BuildFromATreeMatchesBuildFromDisk()
    {
        using var repo = SampleRepo.Build();
        var fromDisk = Scanner.Build(repo.Path);
        var tree = fromDisk.Files.ToDictionary(
            f => f.P,
            f => File.ReadAllLines(Path.Combine(repo.Path, f.P)));

        var fromTree = Scanner.BuildFrom(repo.Path, tree);

        Assert.Equal(fromDisk.Files.Select(f => f.P), fromTree.Files.Select(f => f.P));
        Assert.Equal(fromDisk.Files.Select(f => f.N), fromTree.Files.Select(f => f.N));
        Assert.Equal(fromDisk.Districts.Select(d => d.Name), fromTree.Districts.Select(d => d.Name));
    }

    [Fact]
    public void ScanRoundTripsThroughJson()
    {
        using var repo = SampleRepo.Build();
        using var outDir = new TempDir();
        var scan = Scanner.Build(repo.Path);
        var path = outDir.Combine("cache", "scan.json");

        Scanner.Save(scan, path);
        var reread = System.Text.Json.JsonSerializer.Deserialize<Scan>(File.ReadAllText(path))!;

        Assert.Equal(scan.Root, reread.Root);
        Assert.Equal(scan.Files.Count, reread.Files.Count);
        Assert.Equal(scan.Files[0].P, reread.Files[0].P);
        Assert.Equal(scan.Files[0].D, reread.Files[0].D);
        Assert.Equal(scan.World.W, reread.World.W);
    }
}

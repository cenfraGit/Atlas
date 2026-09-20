namespace Atlas.Tests;

/// <summary>a fingerprint is what lets a board window find its file again after
/// a rename, so what must and must not change it is the whole contract.</summary>
public class FileKeysTests
{
    static string[] Lines(string s) => s.Replace("\r\n", "\n").Split('\n');

    const string Source = """
        namespace Demo;

        public class Panel
        {
            public int Width;
        }
        """;

    [Fact]
    public void SameContentGivesSameKey() =>
        Assert.Equal(FileKeys.Of(Lines(Source)), FileKeys.Of(Lines(Source)));

    [Fact]
    public void ReindentingDoesNotChangeTheKey()
    {
        var indented = string.Join("\n", Lines(Source).Select(l => "    " + l));
        Assert.Equal(FileKeys.Of(Lines(Source)), FileKeys.Of(Lines(indented)));
    }

    [Fact]
    public void BlankLinesDoNotChangeTheKey()
    {
        var spaced = string.Join("\n\n", Lines(Source));
        Assert.Equal(FileKeys.Of(Lines(Source)), FileKeys.Of(Lines(spaced)));
    }

    [Fact]
    public void EditingAFirstLineChangesTheKey()
    {
        var edited = Source.Replace("public int Width;", "public int Height;");
        Assert.NotEqual(FileKeys.Of(Lines(Source)), FileKeys.Of(Lines(edited)));
    }

    [Fact]
    public void EditsPastTheSampledLinesDoNotChangeTheKey()
    {
        var head = string.Join("\n", Enumerable.Range(0, 40).Select(i => $"line {i};"));
        var a = FileKeys.Of(Lines(head + "\ntail one;"));
        var b = FileKeys.Of(Lines(head + "\ntail two;\nand more;"));
        Assert.Equal(a, b);
    }

    [Fact]
    public void AnEmptyFileHasNoKey()
    {
        Assert.Equal("", FileKeys.Of([]));
        Assert.Equal("", FileKeys.Of(["", "   ", "\t"]));
    }

    [Fact]
    public void KeysAreShortAndHex()
    {
        var key = FileKeys.Of(Lines(Source));
        Assert.Equal(12, key.Length);
        Assert.All(key, c => Assert.Contains(c, "0123456789ABCDEF"));
    }

    [Fact]
    public void OfFileReadsFromDisk()
    {
        using var dir = new TempDir();
        var path = dir.File("Panel.cs", Source);
        Assert.Equal(FileKeys.Of(Lines(Source)), FileKeys.OfFile(path));
    }

    [Fact]
    public void OfFileReturnsNullForAMissingFile() =>
        Assert.Null(FileKeys.OfFile(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid())));
}

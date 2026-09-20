namespace Atlas.Tests;

/// <summary>a scratch directory that deletes itself. every fixture is built
/// from scratch rather than pointed at the repo under test, so a test cannot
/// start passing or failing because somebody added a file.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir(string tag = "atlas")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"{tag}_{Guid.NewGuid():n}"[..24]);
        Directory.CreateDirectory(Path);
    }

    public string File(string relPath, string contents)
    {
        var full = System.IO.Path.Combine(Path, relPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        // always LF: this repo's own line endings must not leak into a fixture
        System.IO.File.WriteAllText(full, contents.Replace("\r\n", "\n"));
        return full;
    }

    public string Combine(params string[] parts) =>
        System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    public void Dispose()
    {
        // a git repo leaves read-only objects behind on windows
        try { Clear(new DirectoryInfo(Path)); Directory.Delete(Path, true); } catch { }
    }

    static void Clear(DirectoryInfo dir)
    {
        foreach (var f in dir.GetFiles()) f.Attributes = FileAttributes.Normal;
        foreach (var d in dir.GetDirectories()) Clear(d);
    }
}

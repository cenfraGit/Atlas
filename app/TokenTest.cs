using System.Collections.Concurrent;
using SkiaSharp;

namespace Atlas;

/// <summary>tokenises a folder's files the way the canvas does - many at once,
/// on background threads - and reports what failed. run: dotnet run -- --tokentest [dir]
/// </summary>
public static class TokenTest
{
    static readonly string Sep = Path.DirectorySeparatorChar.ToString();

    public static void Run(string[] args)
    {
        var dir = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--") && Directory.Exists(a))
                  ?? Directory.GetCurrentDirectory();
        var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Sep + "obj" + Sep) && !f.Contains(Sep + "bin" + Sep) && !f.Contains(Sep + ".git" + Sep))
            .Where(f => Path.GetExtension(f) is ".cs" or ".json" or ".md" or ".ts" or ".js")
            .Take(60).ToArray();

        Console.WriteLine($"tokenising {files.Length} files from {dir}");
        var hl = new Highlighter(new SKColor(0x9f, 0xd4, 0xea));
        var failures = new ConcurrentBag<string>();
        int ok = 0, nullResult = 0;

        Parallel.ForEach(files, f =>
        {
            try
            {
                var runs = hl.Tokenize(File.ReadAllLines(f), Path.GetExtension(f));
                if (runs is null) Interlocked.Increment(ref nullResult);
                else Interlocked.Increment(ref ok);
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(f)}: {ex.GetType().Name}: {ex.Message}");
            }
        });

        Console.WriteLine($"highlighted={ok}  no-grammar={nullResult}  threw={failures.Count}");
        foreach (var f in failures.Take(5)) Console.WriteLine("  " + f);
        if (failures.Count > 0 || ok < files.Length / 2)
            Console.WriteLine("FAIL: concurrent tokenising is broken");
        else
            Console.WriteLine("PASS");
    }
}

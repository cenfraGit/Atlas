namespace Atlas;

/// <summary>run: dotnet run -- --searchtest &lt;repo&gt;</summary>
public static class SearchTest
{
    static int _fails;

    static void Check(bool ok, string what)
    {
        if (!ok) { _fails++; Console.WriteLine("  FAIL: " + what); }
    }

    static string? Top(Scan scan, string q) =>
        Search.Run(scan, q).Select(h => h.Path).FirstOrDefault();

    public static void Run(string[] args)
    {
        var repo = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--") && Directory.Exists(a));
        if (repo is null) { Console.WriteLine("usage: --searchtest <repo>"); return; }
        var scan = Scanner.Build(repo);
        Console.WriteLine($"searching {scan.Files.Count} files from {repo}");

        // pick a real file with a distinctive name to query for
        var target = scan.Files
            .Select(f => f.P)
            .Where(p => p.Contains('/') && Path.GetFileNameWithoutExtension(p).Length > 8)
            .OrderBy(p => p.Length)
            .FirstOrDefault();
        if (target is null) { Console.WriteLine("no suitable file to test with"); return; }

        var name = Path.GetFileNameWithoutExtension(target);
        Check(Top(scan, name) == target, $"exact name '{name}' ranks '{target}' first, got '{Top(scan, name)}'");

        // subsequence: first letter of the name plus its capitals
        var abbrev = string.Concat(name.Where((c, i) => i == 0 || char.IsUpper(c)));
        if (abbrev.Length >= 2)
        {
            var hits = Search.Run(scan, abbrev).Select(h => h.Path).ToList();
            Check(hits.Contains(target), $"abbreviation '{abbrev}' finds '{target}'");
        }

        Check(Search.Run(scan, "").Count == 0, "empty query returns nothing");
        Check(Search.Run(scan, "zzqqxxjjwwvv").Count == 0, "nonsense query returns nothing");
        Check(Search.Run(scan, name).Count <= 25, "result count is capped");

        var ordered = Search.Run(scan, name);
        Check(ordered.Zip(ordered.Skip(1)).All(p => p.First.Score >= p.Second.Score),
            "results are sorted by descending score");

        Console.WriteLine(_fails == 0 ? "PASS" : $"{_fails} FAILURES");
    }
}

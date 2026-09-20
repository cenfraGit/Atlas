namespace Atlas.Tests;

/// <summary>a small synthetic repo with a known shape: three directories, a
/// file the scanner must skip, and one file long enough to have a real card.
/// Tests assert against these exact paths, so the numbers in them mean
/// something.</summary>
public static class SampleRepo
{
    public const string LongFile = "app/Scene.cs";
    public const int LongFileLines = 300;

    public static TempDir Build(string tag = "atlas_repo")
    {
        var dir = new TempDir(tag);

        dir.File("app/Program.cs", """
            namespace Demo;

            public static class Program
            {
                public static void Main() => Console.WriteLine("hi");
            }
            """);

        dir.File(LongFile, Scene(LongFileLines));

        dir.File("app/ui/Panel.cs", """
            namespace Demo.Ui;

            // a comment line
            public class Panel
            {
                public int Width;
            }
            """);

        dir.File("docs/readme.md", "# docs\n\nsome prose\n");

        // things the scanner must leave out
        dir.File("bin/Generated.cs", "class Generated { }");
        dir.File("app/notes.txt", "not a source extension");
        dir.File(".hidden/Secret.cs", "class Secret { }");

        return dir;
    }

    /// <summary>a file with a predictable line count and a real C# shape, so
    /// Roslyn has something to find and the card has a real height.</summary>
    public static string Scene(int lines)
    {
        var body = new List<string>
        {
            "namespace Demo;",
            "",
            "public sealed class Scene",
            "{",
            "    public void Draw()",
            "    {",
        };
        while (body.Count < lines - 2) body.Add($"        Step({body.Count});");
        body.Add("    }");
        body.Add("}");
        return string.Join("\n", body);
    }
}

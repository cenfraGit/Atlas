using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atlas;

/// <summary>a declaration and the lines it spans. Name is qualified, e.g.
/// "Atlas.Scene.DrawBoard(3)" - methods carry their parameter count
/// so overloads do not collide.</summary>
public readonly record struct SymbolSpan(string Name, int StartLine, int EndLine)
{
    public bool Contains(int line) => line >= StartLine && line <= EndLine;
    public int Lines => EndLine - StartLine + 1;
}

/// <summary>syntax-only symbol extraction. no compilation, no MSBuild
/// workspace: an annotation needs a declaration's name and line range, not
/// semantic binding, and parsing one file is milliseconds.</summary>
public static class Symbols
{
    static readonly ConcurrentDictionary<string, List<SymbolSpan>> Cache = new();

    public static bool Supports(string path) =>
        Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase);

    public static void Forget(string fullPath) => Cache.TryRemove(fullPath, out _);

    /// <summary>declarations in a file, outermost first. empty for languages we
    /// cannot parse, which simply fall back to line anchoring.</summary>
    public static List<SymbolSpan> ForFile(string fullPath)
    {
        if (Cache.TryGetValue(fullPath, out var hit)) return hit;
        var result = new List<SymbolSpan>();
        if (Supports(fullPath))
        {
            try
            {
                var text = File.ReadAllText(fullPath);
                var tree = CSharpSyntaxTree.ParseText(text);
                Walk(tree.GetRoot(), "", result);
            }
            catch { /* unreadable or unparsable: no symbols, line anchors still work */ }
        }
        Cache[fullPath] = result;
        return result;
    }

    /// <summary>the most deeply nested declaration covering a line.</summary>
    public static SymbolSpan? Innermost(List<SymbolSpan> symbols, int line)
    {
        SymbolSpan? best = null;
        foreach (var s in symbols)
            if (s.Contains(line) && (best is null || s.Lines < best.Value.Lines))
                best = s;
        return best;
    }

    static void Walk(SyntaxNode node, string prefix, List<SymbolSpan> into)
    {
        foreach (var child in node.ChildNodes())
        {
            var name = NameOf(child);
            if (name is null)
            {
                Walk(child, prefix, into);
                continue;
            }

            var qualified = prefix.Length == 0 ? name : prefix + "." + name;
            var span = child.GetLocation().GetLineSpan();
            into.Add(new SymbolSpan(qualified, span.StartLinePosition.Line, span.EndLinePosition.Line));
            Walk(child, qualified, into);
        }
    }

    static string? NameOf(SyntaxNode node) => node switch
    {
        BaseNamespaceDeclarationSyntax ns => ns.Name.ToString(),
        TypeDeclarationSyntax t => t.Identifier.ValueText,
        EnumDeclarationSyntax e => e.Identifier.ValueText,
        DelegateDeclarationSyntax d => d.Identifier.ValueText,
        // parameter count keeps overloads apart without needing semantics
        MethodDeclarationSyntax m => $"{m.Identifier.ValueText}({m.ParameterList.Parameters.Count})",
        ConstructorDeclarationSyntax c => $".ctor({c.ParameterList.Parameters.Count})",
        PropertyDeclarationSyntax p => p.Identifier.ValueText,
        EventDeclarationSyntax ev => ev.Identifier.ValueText,
        IndexerDeclarationSyntax => "this[]",
        _ => null,
    };
}

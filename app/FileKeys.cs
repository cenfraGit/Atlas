using System.Security.Cryptography;
using System.Text;

namespace Atlas;

/// <summary>a path is not an identity: files get renamed and moved, and every
/// board window and annotation pointing at one would be orphaned. a fingerprint
/// of the content lets a reference find its file again.</summary>
public static class FileKeys
{
    const int SampleLines = 40;
    const char Sep = (char)1;

    /// <summary>hash of the first few meaningful lines with whitespace removed,
    /// so reindenting and edits further down do not change it.</summary>
    public static string Of(string[] lines)
    {
        var sb = new StringBuilder();
        int taken = 0;
        foreach (var line in lines)
        {
            if (taken >= SampleLines) break;
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            foreach (var ch in trimmed)
                if (!char.IsWhiteSpace(ch)) sb.Append(ch);
            sb.Append(Sep);
            taken++;
        }
        if (taken == 0) return "";
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes)[..12];
    }

    public static string? OfFile(string fullPath)
    {
        try { return Of(File.ReadAllLines(fullPath)); }
        catch { return null; }
    }
}

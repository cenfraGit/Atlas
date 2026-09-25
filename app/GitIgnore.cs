using LibGit2Sharp;

namespace Atlas;

/// <summary>asks the repo what is not its source.
///
/// A hardcoded list of noisy directories gets node_modules and bin right and
/// knows nothing about anything else - Atlas's own data folder is
/// gitignored and still landed on the map. A repo already declares what is
/// not part of it, in a file designed for exactly that, with nested rules and
/// negations and a syntax nobody should reimplement. So this asks libgit2,
/// which is already here for review mode.
///
/// Tracked files are never hidden, whatever the patterns say. Git ignores
/// only what it is not already following, and a file somebody committed is
/// part of the repo by definition - that is what makes committing a
/// `.atlas/` folder work in a repo whose .gitignore mentions it.</summary>
public sealed class GitIgnore : IDisposable
{
    readonly Repository _repo;

    /// <summary>where the scanned folder sits inside the work tree, with a
    /// trailing slash, or empty when it is the work tree itself. Atlas can be
    /// pointed at a subdirectory, and git's paths are relative to the root.</summary>
    readonly string _prefix;

    readonly Dictionary<string, bool> _cache = new(StringComparer.Ordinal);

    GitIgnore(Repository repo, string prefix)
    {
        _repo = repo;
        _prefix = prefix;
    }

    /// <summary>null when the folder is not in a git repo, which is a normal
    /// way to use Atlas rather than a failure.</summary>
    public static GitIgnore? For(string root)
    {
        try
        {
            var found = Repository.Discover(Path.GetFullPath(root));
            if (found is null) return null;

            var repo = new Repository(found);
            var work = repo.Info.WorkingDirectory;
            if (work is null) { repo.Dispose(); return null; }      // a bare repo has no tree

            var prefix = Path.GetRelativePath(work, Path.GetFullPath(root)).Replace('\\', '/');
            if (prefix == ".") prefix = "";
            else if (prefix.StartsWith("..", StringComparison.Ordinal)) { repo.Dispose(); return null; }
            else prefix += "/";

            return new GitIgnore(repo, prefix);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"could not read .gitignore: {ex.Message}");
            return null;
        }
    }

    /// <summary>true when the repo would not track this path. Directories are
    /// asked with a trailing slash, which is how a rule like `build/` knows it
    /// means the directory and not a file of the same name.</summary>
    public bool Ignored(string relPath)
    {
        if (relPath.Length == 0) return false;
        if (_cache.TryGetValue(relPath, out var hit)) return hit;

        bool ignored;
        try
        {
            var full = _prefix + relPath;
            // already tracked beats any pattern
            ignored = _repo.Index[full.TrimEnd('/')] is null && _repo.Ignore.IsPathIgnored(full);
        }
        catch { ignored = false; }

        _cache[relPath] = ignored;
        return ignored;
    }

    public void Dispose() => _repo.Dispose();
}

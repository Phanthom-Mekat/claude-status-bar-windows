using System.IO;

namespace ClaudeStatusBar;

/// <summary>Current git branch for a session's cwd, read straight from .git/HEAD (no git spawn). Worktrees and
/// submodules (.git is a FILE "gitdir: …") are followed; a detached HEAD shows a short SHA; non-git = "".</summary>
public static class GitInfo
{
    // cwd -> resolved HEAD path ("" = confirmed non-git). Resolution walks directories, so cache it.
    static readonly Dictionary<string, string> _headCache = new(StringComparer.OrdinalIgnoreCase);
    static readonly object _lock = new();

    /// <summary>Forget a cached "not a repo" so a folder that was just git-init'd is picked up.</summary>
    public static void RecheckNonGit(string cwd)
    {
        lock (_lock) if (_headCache.TryGetValue(cwd, out var h) && h == "") _headCache.Remove(cwd);
    }

    public static string Branch(string cwd)
    {
        if (string.IsNullOrEmpty(cwd)) return "";
        var head = HeadPath(cwd);
        if (head == null) return "";
        try
        {
            var fi = new FileInfo(head);
            if (!fi.Exists || fi.Length > 1024) { lock (_lock) _headCache.Remove(cwd); return ""; } // stale: retry next time
            return ParseHead(File.ReadAllText(head));
        }
        catch { return ""; }
    }

    /// <summary>Pure: "ref: refs/heads/x" -> "x"; a bare commit hash -> its 7-char prefix; else "".</summary>
    public static string ParseHead(string text)
    {
        var h = text.Trim();
        if (h.StartsWith("ref: refs/heads/")) return h["ref: refs/heads/".Length..];
        if (h.StartsWith("ref: ")) return h[(h.LastIndexOf('/') + 1)..];
        if (h.Length is >= 40 and <= 64 && h.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')) return h[..7];
        return "";
    }

    static string? HeadPath(string cwd)
    {
        lock (_lock) if (_headCache.TryGetValue(cwd, out var hit)) return hit.Length == 0 ? null : hit;
        string? head = null;
        try
        {
            var dir = new DirectoryInfo(cwd);
            for (int i = 0; i < 40 && dir != null; i++, dir = dir.Parent)
            {
                var g = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(g)) { head = Path.Combine(g, "HEAD"); break; }
                if (File.Exists(g))
                {
                    var line = File.ReadLines(g).FirstOrDefault() ?? "";
                    if (line.StartsWith("gitdir: "))
                    {
                        var gd = line[8..].Trim();
                        if (!Path.IsPathRooted(gd)) gd = Path.GetFullPath(Path.Combine(dir.FullName, gd));
                        head = Path.Combine(gd, "HEAD");
                    }
                    break;
                }
            }
        }
        catch { }
        lock (_lock) _headCache[cwd] = head ?? "";
        return head;
    }
}

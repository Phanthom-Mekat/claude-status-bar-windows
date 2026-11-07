namespace ClaudeStatusBar;

/// <summary>Maps a hook to its session's long-lived <c>claude</c> process via /proc. Claude Code runs hooks through
/// <c>sh -c</c>, so the hook's parent is a short-lived shell (claude → sh → node); the hook calls
/// <c>ClaudeStatusBar --claude-pid &lt;its own pid&gt;</c> once per session and we walk up from there.</summary>
public static class ProcessTree
{
    /// <summary>Nearest ancestor (or self) that is claude, or -1.</summary>
    public static int FindClaudeAncestor(int pid)
    {
        for (int i = 0; i < 16 && pid > 1; i++)
        {
            if (IsClaude(pid)) return pid;
            int parent = Parent(pid);
            if (parent <= 0 || parent == pid) break;
            pid = parent;
        }
        return -1;
    }

    /// <summary>The pid and its ancestors, nearest first, with process names (for finding a session's window).</summary>
    public static List<(int pid, string name)> Ancestors(int pid)
    {
        var list = new List<(int, string)>();
        for (int i = 0; i < 16 && pid > 1; i++)
        {
            string name;
            try { name = File.ReadAllText($"/proc/{pid}/comm").Trim(); } catch { break; }
            list.Add((pid, name));
            int parent = Parent(pid);
            if (parent <= 0 || parent == pid) break;
            pid = parent;
        }
        return list;
    }

    /// <summary>Liveness: the pid exists AND is still claude (guards pid reuse).</summary>
    public static bool IsClaudeAlive(int pid) => pid > 0 && IsClaude(pid);

    // Native installs exec a binary named "claude"; npm installs run node, which sets process.title = "claude"
    // (libuv rewrites argv[0] and prctl's comm). Either signal is enough.
    static bool IsClaude(int pid)
    {
        try
        {
            if (File.ReadAllText($"/proc/{pid}/comm").Trim() == "claude") return true;
            var argv0 = File.ReadAllText($"/proc/{pid}/cmdline").Split('\0')[0];
            return Path.GetFileName(argv0) == "claude";
        }
        catch { return false; } // gone
    }

    /// <summary>ppid from /proc/&lt;pid&gt;/stat ("pid (comm) S ppid …"). comm may hold spaces or ')', so split
    /// after the LAST ')'.</summary>
    public static int Parent(int pid)
    {
        try { return ParseStatParent(File.ReadAllText($"/proc/{pid}/stat")); }
        catch { return -1; }
    }

    public static int ParseStatParent(string stat)
    {
        var rest = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
        return int.TryParse(rest[1], out var ppid) ? ppid : -1;
    }
}

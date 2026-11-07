using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeStatusBar;

/// <summary>Jump to a session: bring the window hosting its claude.exe to the front. Walks up from the session's
/// claude.exe (VS Code / Cursor main process, Windows Terminal, a console, the Claude desktop app…) to the first
/// ancestor that owns a visible top-level window; when that process has several (two VS Code workspaces), the one
/// whose title mentions the project wins. A terminal raises its window, not a specific tab (Windows exposes no
/// tab API) — the same limit as macOS.</summary>
public static class SessionFocus
{
    // Ancestors that are never the session's host: walking past them would land on the desktop or taskbar.
    static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
        { "explorer.exe", "services.exe", "svchost.exe", "winlogon.exe", "userinit.exe", "wininit.exe", "sihost.exe" };

    // Folder names too generic to identify a window by ("src" appears in plenty of titles).
    static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
        { "src", "app", "apps", "lib", "libs", "test", "tests", "docs", "packages", "source", "code", "projects", "repos", "users", "home" };

    /// <summary>Jump to a session. For the Claude Code extension (VS Code, Cursor, …) this goes all the way to the
    /// conversation's own tab: after focusing the right editor window we open the extension's documented
    /// <c>&lt;editor&gt;://anthropic.claude-code/open?session=&lt;id&gt;</c> link, whose handler reveals the tab holding
    /// that session (or reopens it if it was closed). The window goes first because the editor hands links to
    /// its last-focused window. Elsewhere it raises the hosting window. False if nothing was found.</summary>
    public static bool Jump(int claudePid, string cwd, string sessionId, string entrypoint)
    {
        var ancestors = claudePid > 0 ? ProcessTree.Ancestors(claudePid) : new();
        var hwnd = Pick(ancestors, TopLevelWindows(), cwd);
        bool focused = hwnd != IntPtr.Zero && Focus(hwnd);
        if (entrypoint == "claude-vscode" && Guid.TryParse(sessionId, out _))
        {
            var url = $"{EditorScheme(ancestors.Select(a => a.name))}://anthropic.claude-code/open?session={sessionId}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        return focused;
    }

    /// <summary>Pure: the URL scheme of the editor hosting the extension, from the session's ancestor exe names.</summary>
    public static string EditorScheme(IEnumerable<string> ancestorNames)
    {
        foreach (var n in ancestorNames)
            switch (n.ToLowerInvariant())
            {
                case "code - insiders.exe": return "vscode-insiders";
                case "cursor.exe": return "cursor";
                case "windsurf.exe": return "windsurf";
                case "code.exe": return "vscode";
            }
        return "vscode";
    }

    public static IntPtr FindWindow(int claudePid, string cwd) =>
        claudePid > 0 ? Pick(ProcessTree.Ancestors(claudePid), TopLevelWindows(), cwd) : IntPtr.Zero;

    /// <summary>Pure: the nearest ancestor that owns windows is the host. Among its windows, prefer the one whose
    /// title names the session's folder — or a parent folder, since editors title windows by workspace root
    /// (cwd D:\work\repo\pkg in a window "… - repo - Visual Studio Code"). Deepest folder wins.</summary>
    public static IntPtr Pick(IReadOnlyList<(int pid, string name)> ancestors,
                              IReadOnlyDictionary<int, List<(IntPtr hwnd, string title)>> windowsByPid, string cwd)
    {
        var folders = cwd.Split('\\', '/', StringSplitOptions.RemoveEmptyEntries)
            .Reverse().Where(f => f.Length >= 3 && !f.EndsWith(':') && !Generic.Contains(f)).ToList();
        foreach (var (pid, name) in ancestors)
        {
            if (Stop.Contains(name)) break;
            if (!windowsByPid.TryGetValue(pid, out var wins) || wins.Count == 0) continue;
            foreach (var folder in folders)
                foreach (var w in wins)
                    if (w.title.Contains(folder, StringComparison.OrdinalIgnoreCase)) return w.hwnd;
            return wins[0].hwnd; // z-order: the most recently used of that app's windows
        }
        return IntPtr.Zero;
    }

    /// <summary>Visible, unowned, titled top-level windows grouped by owning pid, in z-order (topmost first).</summary>
    static Dictionary<int, List<(IntPtr, string)>> TopLevelWindows()
    {
        var map = new Dictionary<int, List<(IntPtr, string)>>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;
            int len = GetWindowTextLength(h);
            if (len == 0) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            GetWindowThreadProcessId(h, out uint pid);
            if (!map.TryGetValue((int)pid, out var list)) map[(int)pid] = list = new();
            list.Add((h, sb.ToString()));
            return true;
        }, IntPtr.Zero);
        return map;
    }

    /// <summary>Restore if minimized and bring to the front. We're handling the user's menu click, so Windows lets
    /// us hand over the foreground; the Alt tap is the documented-safe nudge for when it still refuses.</summary>
    public static bool Focus(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd) return true;
        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        SetForegroundWindow(hwnd);
        return GetForegroundWindow() == hwnd;
    }

    const uint GW_OWNER = 4, KEYEVENTF_KEYUP = 2;
    const int SW_RESTORE = 9;
    const byte VK_MENU = 0x12;
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}

using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace ClaudeStatusBar;

/// <summary>App lifecycle: write apppath.txt, ensure hooks on version change, first-run tip, and debounced self-quit.</summary>
public class Lifecycle
{
    readonly AppSettings _cfg;
    readonly Action _quit;
    readonly Action<string>? _balloon;
    readonly DateTime _launched = DateTime.UtcNow;
    DateTime? _notNeededSince;
    const int LaunchGraceSec = 5, IdleQuitSec = 3;

    public Lifecycle(AppSettings cfg, Action quit, Action<string>? balloon = null)
    {
        _cfg = cfg; _quit = quit; _balloon = balloon;
    }

    public void OnStartup()
    {
        Log.Init(_cfg.DebugLogging);
        try
        {
            Paths.EnsureDir();
            // Stable path next to our binaries. Under the dotnet host, ProcessPath is dotnet.exe, so derive
            // from BaseDirectory. The hooks launch the .dll via the trusted dotnet host when it exists (runs
            // even under Smart App Control), else the exe. The pid lets hooks detect "running" host-agnostically.
            File.WriteAllText(Paths.AppPathTxt, Path.Combine(AppContext.BaseDirectory, "ClaudeStatusBar.exe"));
            File.WriteAllText(Paths.AppPidTxt, Environment.ProcessId.ToString());
        }
        catch (Exception e) { Log.Write("apppath write failed: " + e.Message); }

        var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";
        _ = UpdateChecker.MaybeCheckAsync(_cfg, ver); // once/day; no-op until a repo is configured
        if (_cfg.InstalledVersion != ver)
        {
            try
            {
                HookInstaller.Install();
                bool firstEver = string.IsNullOrEmpty(_cfg.InstalledVersion);
                _cfg.InstalledVersion = ver; _cfg.Save();
                Log.Write("hooks installed for v" + ver);
                if (firstEver) FirstRunTip();
            }
            catch (Exception e)
            {
                Log.Write("hook install failed: " + e.Message);
                _balloon?.Invoke("Couldn't install hooks — see app.log (enable CLAUDE_STATUSBAR_DEBUG=1).");
            }
        }
    }

    void FirstRunTip()
    {
        bool win11 = Environment.OSVersion.Version.Build >= 22000;
        string how = win11
            ? "drag its icon out of the ^ overflow, or Settings → Personalization → Taskbar → Other system tray icons"
            : "right-click the taskbar → Taskbar settings → Select which icons appear on the taskbar";
        _balloon?.Invoke($"Claude Status Bar is in your system tray. To keep it always visible, {how}.");
        Log.Write("first-run tip shown");
    }

    // Match Claude DESKTOP specifically (installed under %LOCALAPPDATA%\AnthropicClaude),
    // NOT the Claude Code CLI — which also runs as a process named "claude" (from the VS Code
    // extension / native binary). Matching the bare name would keep us alive forever on a
    // CLI-only machine and break self-quit.
    public static bool ClaudeDesktopRunning()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("claude"))
            {
                try
                {
                    if ((p.MainModule?.FileName ?? "").Contains("AnthropicClaude", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { /* access denied for some processes — skip */ }
            }
        }
        catch { }
        return false;
    }

    // Total tracked sessions (markers present). Used for keep-alive.
    public static int SessionCount()
    {
        try { return Directory.Exists(Paths.SessionsDir) ? Directory.GetFiles(Paths.SessionsDir).Length : 0; }
        catch { return 0; }
    }

    // Keep the app alive while Claude has been active recently (state.json was written within `sec`),
    // so it doesn't self-quit mid-work even if the session markers churn.
    public static bool RecentStateActivity(int sec)
    {
        try
        {
            return File.Exists(Paths.StateJson) &&
                   (DateTime.UtcNow - File.GetLastWriteTimeUtc(Paths.StateJson)).TotalSeconds < sec;
        }
        catch { return false; }
    }

    // Sessions with RECENT activity. update.js touches sessions.d/<sid> on every hook event, so the
    // marker's mtime is "last activity"; only fresh ones count as active (fixes the inflated "+N").
    public static int ActiveSessionCount(int withinSec = 90)
    {
        try
        {
            if (!Directory.Exists(Paths.SessionsDir)) return 0;
            var now = DateTime.UtcNow;
            return Directory.GetFiles(Paths.SessionsDir)
                .Count(f => (now - File.GetLastWriteTimeUtc(f)).TotalSeconds < withinSec);
        }
        catch { return 0; }
    }

    // Delete markers left by sessions that crashed without firing SessionEnd (no activity for hours).
    static void PruneOrphans(int maxAgeSec = 21600) // 6h
    {
        try
        {
            if (!Directory.Exists(Paths.SessionsDir)) return;
            var now = DateTime.UtcNow;
            foreach (var f in Directory.GetFiles(Paths.SessionsDir))
                if ((now - File.GetLastWriteTimeUtc(f)).TotalSeconds > maxAgeSec)
                    try { File.Delete(f); } catch { }
        }
        catch { }
    }

    /// <summary>Quit when no session is active and Claude Desktop is closed, after a debounced grace.</summary>
    public void CheckLifecycle()
    {
        if ((DateTime.UtcNow - _launched).TotalSeconds < LaunchGraceSec) return;
        PruneOrphans();
        bool needed = SessionCount() > 0 || ClaudeDesktopRunning() || RecentStateActivity(600);
        if (needed) { _notNeededSince = null; return; }
        _notNeededSince ??= DateTime.UtcNow;
        if ((DateTime.UtcNow - _notNeededSince.Value).TotalSeconds >= IdleQuitSec)
        {
            Log.Write("self-quit (no sessions, no desktop)");
            _quit();
        }
    }
}

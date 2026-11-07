using System.Reflection;

namespace ClaudeStatusBar;

/// <summary>Linux lifecycle: write apppath.txt (so the hooks can launch us), install hooks on version change,
/// first-run tip, and debounced self-quit. Same rules as the Windows Lifecycle minus Claude Desktop, which has
/// no official Linux build.</summary>
public class Lifecycle
{
    readonly AppSettings _cfg;
    readonly Action _quit;
    readonly Action<string>? _notify;
    readonly DateTime _launched = DateTime.UtcNow;
    readonly bool _firstRun;
    DateTime? _notNeededSince;
    // See the Windows Lifecycle: first run gets a long grace so the user can find the icon before self-quit.
    const int LaunchGraceSec = 5, IdleQuitSec = 3, FirstRunGraceSec = 180;

    public Lifecycle(AppSettings cfg, Action quit, Action<string>? notify = null)
    {
        _cfg = cfg; _quit = quit; _notify = notify;
        _firstRun = string.IsNullOrEmpty(_cfg.InstalledVersion);
    }

    /// <summary>Our launchable binary: the apphost next to the dll (ProcessPath is `dotnet` under `dotnet run`).
    /// No .exe suffix, so the hooks run it directly instead of mapping it to a .dll for the dotnet host.</summary>
    static string AppBinary()
    {
        var host = Path.Combine(AppContext.BaseDirectory, "ClaudeStatusBar");
        return File.Exists(host) ? host : Environment.ProcessPath ?? host;
    }

    public void OnStartup()
    {
        Log.Init(_cfg.DebugLogging);
        try
        {
            Paths.EnsureDir();
            File.WriteAllText(Paths.AppPathTxt, AppBinary());
            File.WriteAllText(Paths.AppPidTxt, Environment.ProcessId.ToString());
            File.Delete(Paths.QuitIntent); // launching us (by hand or via SessionStart) voids a prior Quit
        }
        catch (Exception e) { Log.Write("apppath write failed: " + e.Message); }
        Updater.CleanupOld(); // the folder a self-update moved aside last run

        var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";
        _ = UpdateChecker.MaybeCheckAsync(_cfg, ver);
        if (_cfg.InstalledVersion != ver)
        {
            try
            {
                HookInstaller.Install();
                _cfg.InstalledVersion = ver; _cfg.Save();
                Log.Write("hooks installed for v" + ver);
                if (_firstRun)
                    _notify?.Invoke("Claude Status Bar is running in your system tray. On GNOME, tray icons need the " +
                                    "AppIndicator extension (Ubuntu ships it on).");
                else _notify?.Invoke($"Updated to v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}.");
            }
            catch (Exception e)
            {
                Log.Write("hook install failed: " + e.Message);
                _notify?.Invoke("Couldn't install hooks. Run with CLAUDE_STATUSBAR_DEBUG=1 and see ~/.claude/statusbar/app.log.");
            }
        }
    }

    static int SessionCount()
    {
        try { return Directory.Exists(Paths.SessionsDir) ? Directory.GetFiles(Paths.SessionsDir).Length : 0; }
        catch { return 0; }
    }

    // Keep alive while Claude was active recently, so marker churn can't quit us mid-work.
    static bool RecentStateActivity(int sec)
    {
        try { return File.Exists(Paths.StateJson) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(Paths.StateJson)).TotalSeconds < sec; }
        catch { return false; }
    }

    // Markers left by sessions that crashed without SessionEnd (no activity for 6h).
    static void PruneOrphans(int maxAgeSec = 21600)
    {
        try
        {
            if (!Directory.Exists(Paths.SessionsDir)) return;
            foreach (var f in Directory.GetFiles(Paths.SessionsDir))
                if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalSeconds > maxAgeSec)
                    try { File.Delete(f); } catch { }
        }
        catch { }
    }

    /// <summary>Quit when no session is tracked and nothing ran recently, after a debounced grace.</summary>
    public void CheckLifecycle()
    {
        if ((DateTime.UtcNow - _launched).TotalSeconds < (_firstRun ? FirstRunGraceSec : LaunchGraceSec)) return;
        PruneOrphans();
        if (SessionCount() > 0 || RecentStateActivity(600)) { _notNeededSince = null; return; }
        _notNeededSince ??= DateTime.UtcNow;
        if ((DateTime.UtcNow - _notNeededSince.Value).TotalSeconds >= IdleQuitSec)
        {
            Log.Write("self-quit (no sessions)");
            _quit();
        }
    }
}

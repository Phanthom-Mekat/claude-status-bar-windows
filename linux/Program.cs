using Avalonia;
using Avalonia.Controls;

namespace ClaudeStatusBar;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // `--uninstall` removes our Claude Code hooks + scripts, then exits.
        if (args.Contains("--uninstall")) { try { HookInstaller.Uninstall(); } catch { } return 0; }

        // `--claude-pid <pid>`: hook helper — prints the session's claude pid (see ProcessTree). Runs before
        // the single-instance lock so it works while the tray app is up.
        if (args.Length == 2 && args[0] == "--claude-pid")
        {
            Console.Out.Write(int.TryParse(args[1], out var p) ? ProcessTree.FindClaudeAncestor(p) : -1);
            Console.Out.Flush();
            return 0;
        }

        // `--wait-pid <pid>`: relaunched by the updater — let the old instance exit and free the lock first.
        int wi = Array.IndexOf(args, "--wait-pid");
        if (wi >= 0 && wi + 1 < args.Length && int.TryParse(args[wi + 1], out var oldPid))
            try { using var old = System.Diagnostics.Process.GetProcessById(oldPid); old.WaitForExit(15000); } catch { /* already gone */ }

        // Single instance via an flock'd file, not a named Mutex: .NET scopes unprefixed Unix mutexes to the
        // login session, and the hooks spawn us detached (setsid), so every launch would get its own "mutex".
        FileStream? instanceLock;
        try
        {
            Paths.EnsureDir();
            instanceLock = new FileStream(Path.Combine(Paths.StatusbarDir, "app.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) { return 0; } // another instance holds it
        using (instanceLock)
        {
            try { return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown); }
            catch (Exception e)
            {
                // Typically no display (SSH session): nothing to show, exit quietly.
                Log.Init(false);
                Log.Write("startup failed: " + e.Message);
                return 1;
            }
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}

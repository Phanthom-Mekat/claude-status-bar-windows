using System.Windows.Forms;

namespace ClaudeStatusBar;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // `ClaudeStatusBar.exe --uninstall` removes our Claude Code hooks + scripts, then exits.
        if (args.Contains("--uninstall")) { try { LidMode.RestoreSaved(); HookInstaller.Uninstall(); } catch { } return; }

        // `--claude-pid <pid>`: hook helper — prints the session's claude.exe pid (see ProcessTree). Runs
        // before the single-instance mutex so it works while the tray app is up.
        if (args.Length == 2 && args[0] == "--claude-pid")
        {
            Console.Out.Write(int.TryParse(args[1], out var p) ? ProcessTree.FindClaudeAncestor(p) : -1);
            Console.Out.Flush();
            return;
        }

        // `--wait-pid <pid>`: relaunched by the updater — let the old instance exit and free the mutex first.
        int wi = Array.IndexOf(args, "--wait-pid");
        if (wi >= 0 && wi + 1 < args.Length && int.TryParse(args[wi + 1], out var oldPid))
            try { using var old = System.Diagnostics.Process.GetProcessById(oldPid); old.WaitForExit(15000); } catch { /* already gone */ }

        // Single instance: a second launch (e.g. SessionStart spawning us again) just exits.
        using var mutex = new Mutex(true, "ClaudeStatusBar.SingleInstance", out bool isNew);
        if (!isNew) return;

        ScrubInheritedEnvironment();
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayController());
    }

    /// <summary>We're usually launched by a hook, and under the VS Code extension the hook inherits VS Code's
    /// extension-host environment — including ELECTRON_RUN_AS_NODE=1. Everything we start inherits it in turn,
    /// and any Electron app started with it runs as plain Node: the vscode:// link a session click opens dies
    /// with exit code 9 (so the tab never switches), and Open Claude (also Electron) would fail the same way.
    /// Clear it once here so every child gets a clean environment.</summary>
    public static void ScrubInheritedEnvironment() =>
        Environment.SetEnvironmentVariable("ELECTRON_RUN_AS_NODE", null);
}

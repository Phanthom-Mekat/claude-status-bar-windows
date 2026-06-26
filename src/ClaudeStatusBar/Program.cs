using System.Windows.Forms;

namespace ClaudeStatusBar;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // `ClaudeStatusBar.exe --uninstall` removes our Claude Code hooks + scripts, then exits.
        if (args.Contains("--uninstall")) { try { HookInstaller.Uninstall(); } catch { } return; }

        // Single instance: a second launch (e.g. SessionStart spawning us again) just exits.
        using var mutex = new Mutex(true, "ClaudeStatusBar.SingleInstance", out bool isNew);
        if (!isNew) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayController());
    }
}

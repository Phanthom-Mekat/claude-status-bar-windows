using System.IO;

namespace ClaudeStatusBar;

/// <summary>Opt-in file logging (off by default). Enable via app-settings.json or CLAUDE_STATUSBAR_DEBUG=1.</summary>
public static class Log
{
    public static bool Enabled;

    public static void Init(bool fromSettings) =>
        Enabled = fromSettings || Environment.GetEnvironmentVariable("CLAUDE_STATUSBAR_DEBUG") == "1";

    public static void Write(string msg)
    {
        if (!Enabled) return;
        try
        {
            Directory.CreateDirectory(Paths.StatusbarDir);
            var f = Path.Combine(Paths.StatusbarDir, "app.log");
            if (File.Exists(f) && new FileInfo(f).Length > 256 * 1024) File.Delete(f); // simple bound
            File.AppendAllText(f, $"{DateTime.Now:o} {msg}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }
}

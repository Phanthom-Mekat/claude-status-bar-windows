using System.IO;

namespace ClaudeStatusBar;

/// <summary>Central path helpers. Everything lives under the user profile (~/.claude/statusbar).</summary>
public static class Paths
{
    // Use USERPROFILE so the C# app and the Node hooks (os.homedir()) agree on "home";
    // also lets tests redirect home to a throwaway dir. Falls back to the known folder.
    public static string Home =>
        Environment.GetEnvironmentVariable("USERPROFILE") is { Length: > 0 } p
            ? p
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string StatusbarDir => Path.Combine(Home, ".claude", "statusbar");
    public static string StateJson => Path.Combine(StatusbarDir, "state.json");
    public static string SessionsDir => Path.Combine(StatusbarDir, "sessions.d");
    public static string AppPathTxt => Path.Combine(StatusbarDir, "apppath.txt");
    public static string AppSettingsJson => Path.Combine(StatusbarDir, "app-settings.json");
    public static string ClaudeSettingsJson => Path.Combine(Home, ".claude", "settings.json");

    public static void EnsureDir() => Directory.CreateDirectory(StatusbarDir);
}

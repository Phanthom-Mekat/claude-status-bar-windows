using System.IO;
using System.Text.Json;

namespace ClaudeStatusBar;

/// <summary>User preferences + update cache, persisted to ~/.claude/statusbar/app-settings.json (replaces macOS UserDefaults).</summary>
public class AppSettings
{
    public bool ShowTimer { get; set; } = false;
    public bool IconSystem { get; set; } = false;
    public bool PlayCompletionSound { get; set; } = false;
    public bool ShowOverlay { get; set; } = true;          // default on (owner request: always-visible text)
    public bool DebugLogging { get; set; } = false;
    public string AnimStyle { get; set; } = "web";
    public int OverlayX { get; set; } = -1;
    public int OverlayY { get; set; } = -1;
    public string InstalledVersion { get; set; } = "";
    public long LastUpdateCheck { get; set; } = 0;
    public string LatestVersion { get; set; } = "";

    public static AppSettings Load() => LoadFrom(Paths.AppSettingsJson);

    public static AppSettings LoadFrom(string path)
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings(); }
        catch { return new AppSettings(); }
    }

    public void Save() => SaveTo(Paths.AppSettingsJson);

    public void SaveTo(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* prefs are best-effort */ }
    }
}

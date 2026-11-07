using System.IO;
using System.Text.Json;

namespace ClaudeStatusBar;

/// <summary>User preferences + update cache, persisted to ~/.claude/statusbar/app-settings.json (replaces macOS UserDefaults).</summary>
public class AppSettings
{
    public bool ShowTimer { get; set; } = false;
    public bool IconSystem { get; set; } = false;
    /// <summary>Legacy (≤0.5) on/off chime at 1 min+; migrated into <see cref="SoundThreshold"/> on load.</summary>
    public bool PlayCompletionSound { get; set; } = false;
    /// <summary>Completion chime: 0 = off, &lt;1 = every turn, else the minimum turn length in seconds.</summary>
    public double SoundThreshold { get; set; } = 0;
    /// <summary>Floating pill. Deliberately NOT persisted (owner request): turning it off hides it for this run
    /// only, and every launch — i.e. every new session — starts with it on again.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool ShowOverlay { get; set; } = true;
    public bool ShowText { get; set; } = true;             // false = icon-only pill
    public bool ShowMascot { get; set; } = true;           // the pill's companion instead of the spinner icon
    /// <summary>Editors (link schemes) where we already trusted Claude Code's links once — never re-applied, so a
    /// user who removes it in their editor settings keeps their choice.</summary>
    public List<string> EditorTrustApplied { get; set; } = new();
    public bool KeepAwake { get; set; } = true;            // no idle sleep while Claude works
    public bool LidMode { get; set; } = false;             // keep running with the lid shut while Claude works
    /// <summary>Lid shut + hottest thermal zone at/over this → hibernate. Calibration knob: ACPI zones differ per laptop.</summary>
    public double LidHeatLimitC { get; set; } = 85;
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
        AppSettings s;
        try { s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings(); }
        catch { return new AppSettings(); }
        if (s.PlayCompletionSound) { s.SoundThreshold = 60; s.PlayCompletionSound = false; } // old "1m+" toggle
        return s;
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

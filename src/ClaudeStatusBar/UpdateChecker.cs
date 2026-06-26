using System.Net.Http;
using System.Text.Json;

namespace ClaudeStatusBar;

/// <summary>Once-a-day GitHub "latest release" check. Inert until <see cref="Repo"/> is set to "owner/name"
/// (there is no hosting repo yet, so this no-ops by default — the only place to enable it).</summary>
public static class UpdateChecker
{
    // "owner/name" of the GitHub repo. Empty => disabled.
    public const string Repo = "Phanthom-Mekat/claude-status-bar-windows";
    const long DaySec = 24 * 60 * 60;

    public static async Task MaybeCheckAsync(AppSettings cfg, string currentVersion)
    {
        if (string.IsNullOrEmpty(Repo)) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now - cfg.LastUpdateCheck < DaySec) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ClaudeStatusBar/" + currentVersion);
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";
            cfg.LatestVersion = tag;
            cfg.LastUpdateCheck = now;
            cfg.Save();
            Log.Write($"update check: latest={tag} current={currentVersion}");
        }
        catch (Exception e) { Log.Write("update check failed: " + e.Message); }
    }

    public static bool UpdateAvailable(AppSettings cfg, string currentVersion) =>
        !string.IsNullOrEmpty(cfg.LatestVersion) && VersionCompare.IsNewer(cfg.LatestVersion, currentVersion);
}

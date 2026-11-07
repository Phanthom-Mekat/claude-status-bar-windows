using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeStatusBar;

/// <summary>Lets a session click go straight to its Claude tab without VS Code's "Allow 'Claude Code' extension
/// to open this URI?" prompt. VS Code skips that prompt for extensions listed in the user setting
/// <c>extensions.confirmedUriHandlerExtensionIds</c> — exactly what its own "Do not ask me again" checkbox
/// records. Applied once per editor, on the user's first jump (owner decision: no hassle for users); never
/// re-added if the user removes it. Edits keep the user's comments and formatting, are validated before
/// writing, and back the file up once.</summary>
public static class EditorTrust
{
    public const string Key = "extensions.confirmedUriHandlerExtensionIds";
    public const string ExtensionId = "anthropic.claude-code";

    static readonly JsonDocumentOptions Jsonc = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>The editor's user settings.json for a link scheme (vscode, vscode-insiders, cursor, windsurf).</summary>
    public static string SettingsPath(string scheme) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        scheme switch { "vscode-insiders" => "Code - Insiders", "cursor" => "Cursor", "windsurf" => "Windsurf", _ => "Code" },
        "User", "settings.json");

    /// <summary>Trust Claude Code's links in this editor. True only if the file was changed now.</summary>
    public static bool Ensure(string settingsPath)
    {
        if (!Directory.Exists(Path.GetDirectoryName(settingsPath))) return false; // editor not set up for this user
        var text = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : "";
        var updated = AddTrusted(text);
        if (updated == null) return false;
        var bak = settingsPath + ".bak-claudestatusbar";
        if (File.Exists(settingsPath) && !File.Exists(bak)) File.Copy(settingsPath, bak);
        var tmp = settingsPath + ".tmp-claudestatusbar";
        File.WriteAllText(tmp, updated);
        File.Move(tmp, settingsPath, overwrite: true);
        return true;
    }

    /// <summary>Pure: the settings text with Claude Code trusted, or null when it already is or the file can't be
    /// edited safely (unparseable, unexpected shape). The result is re-parsed before it's returned.</summary>
    public static string? AddTrusted(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) text = "{}";
        string candidate;
        try
        {
            using var doc = JsonDocument.Parse(text, Jsonc);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty(Key, out var arr))
            {
                if (arr.ValueKind != JsonValueKind.Array) return null;
                if (IsTrusted(root)) return null;
                var m = Regex.Match(text, "\"" + Regex.Escape(Key) + "\"\\s*:\\s*\\[");
                if (!m.Success) return null;
                candidate = text.Insert(m.Index + m.Length, $"\"{ExtensionId}\"" + (arr.GetArrayLength() == 0 ? "" : ", "));
            }
            else
            {
                int brace = text.IndexOf('{');
                bool empty = !root.EnumerateObject().Any();
                candidate = text.Insert(brace + 1, $"\n    \"{Key}\": [\"{ExtensionId}\"]" + (empty ? "\n" : ","));
            }
        }
        catch (JsonException) { return null; }

        try { using var check = JsonDocument.Parse(candidate, Jsonc); return IsTrusted(check.RootElement) ? candidate : null; }
        catch (JsonException) { return null; } // e.g. the first "{" was inside a comment: leave the file alone
    }

    static bool IsTrusted(JsonElement root) =>
        root.TryGetProperty(Key, out var arr) && arr.ValueKind == JsonValueKind.Array &&
        arr.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String &&
                                      string.Equals(x.GetString(), ExtensionId, StringComparison.OrdinalIgnoreCase));
}

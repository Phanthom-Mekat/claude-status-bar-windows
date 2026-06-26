using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeStatusBar;

/// <summary>Writes the Node hooks to ~/.claude/statusbar/ and merges them into ~/.claude/settings.json.
/// Mirrors the macOS install.js: strip-by-marker then append, preserving all unrelated hooks.</summary>
public static class HookInstaller
{
    const string Marker = "statusbar"; // every command we add points into ~/.claude/statusbar/

    static readonly (string evt, string arg, bool matched)[] UpdateHooks =
    {
        ("UserPromptSubmit", "prompt", false),
        ("PreToolUse", "pre", true),
        ("PostToolUse", "post", true),
        ("Notification", "notify", false),
        ("PermissionRequest", "permreq", true),
        ("Stop", "stop", false),
    };

    static readonly (string evt, string arg)[] LifeHooks =
    {
        ("SessionStart", "start"),
        ("SessionEnd", "end"),
    };

    /// <summary>Pure, testable core: returns settings JSON with our hooks merged in (idempotent).</summary>
    public static string MergeSettings(string json, string updatePath, string lifecyclePath)
    {
        var root = string.IsNullOrWhiteSpace(json) ? new JsonObject() : JsonNode.Parse(json)!.AsObject();
        var hooks = root["hooks"] as JsonObject;
        if (hooks is null) { hooks = new JsonObject(); root["hooks"] = hooks; }

        bool Ours(string cmd) =>
            cmd.Contains(updatePath) || cmd.Contains(lifecyclePath) || cmd.Contains(Marker);

        void Add(string evt, string cmd, bool matched)
        {
            var arr = hooks[evt] as JsonArray;
            if (arr is null) { arr = new JsonArray(); hooks[evt] = arr; }
            // strip any of our previous entries (so reinstall/upgrade is idempotent), keep everything else
            for (int i = arr.Count - 1; i >= 0; i--)
            {
                var hs = arr[i]?["hooks"] as JsonArray;
                if (hs is not null)
                    for (int j = hs.Count - 1; j >= 0; j--)
                        if (Ours(hs[j]?["command"]?.GetValue<string>() ?? "")) hs.RemoveAt(j);
                if (hs is null || hs.Count == 0) arr.RemoveAt(i);
            }
            var wrapper = new JsonObject();
            if (matched) wrapper["matcher"] = "*";
            wrapper["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = cmd });
            arr.Add(wrapper);
        }

        foreach (var (evt, arg, matched) in UpdateHooks) Add(evt, $"node \"{updatePath}\" {arg}", matched);
        foreach (var (evt, arg) in LifeHooks) Add(evt, $"node \"{lifecyclePath}\" {arg}", false);

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static void Install()
    {
        Paths.EnsureDir();
        string up = Path.Combine(Paths.StatusbarDir, "update.js");
        string lc = Path.Combine(Paths.StatusbarDir, "lifecycle.js");
        File.WriteAllText(up, ReadResource("update.js"));
        File.WriteAllText(lc, ReadResource("lifecycle.js"));

        string settings = File.Exists(Paths.ClaudeSettingsJson) ? File.ReadAllText(Paths.ClaudeSettingsJson) : "{}";
        string bak = Paths.ClaudeSettingsJson + ".bak-statusbar";
        if (File.Exists(Paths.ClaudeSettingsJson) && !File.Exists(bak)) File.Copy(Paths.ClaudeSettingsJson, bak);
        File.WriteAllText(Paths.ClaudeSettingsJson, MergeSettings(settings, up, lc) + "\n");
    }

    public static void Uninstall()
    {
        if (File.Exists(Paths.ClaudeSettingsJson))
        {
            var root = JsonNode.Parse(File.ReadAllText(Paths.ClaudeSettingsJson))!.AsObject();
            if (root["hooks"] is JsonObject hooks)
                foreach (var kv in hooks.ToList())
                    if (kv.Value is JsonArray arr)
                    {
                        for (int i = arr.Count - 1; i >= 0; i--)
                        {
                            var hs = arr[i]?["hooks"] as JsonArray;
                            if (hs is not null)
                                for (int j = hs.Count - 1; j >= 0; j--)
                                    if ((hs[j]?["command"]?.GetValue<string>() ?? "").Contains(Marker)) hs.RemoveAt(j);
                            if (hs is null || hs.Count == 0) arr.RemoveAt(i);
                        }
                    }
            File.WriteAllText(Paths.ClaudeSettingsJson, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
        foreach (var f in new[] { "update.js", "lifecycle.js", "apppath.txt" })
            try { File.Delete(Path.Combine(Paths.StatusbarDir, f)); } catch { }
    }

    static string ReadResource(string endsWith)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));
        using var s = asm.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}

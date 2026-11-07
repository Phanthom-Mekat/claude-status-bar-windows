using System.Text.Json.Serialization;

namespace ClaudeStatusBar;

/// <summary>Parsed shape of state.json (identical schema to the macOS app).</summary>
public record StatusState(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("transcript")] string Transcript,
    [property: JsonPropertyName("startedAt")] long StartedAt,
    [property: JsonPropertyName("ts")] long Ts)
{
    /// <summary>Full working directory of the session (for the "open folder" menu action). Not positional
    /// so existing 8-arg constructor calls keep working; deserialized from the "cwd" field.</summary>
    [JsonPropertyName("cwd")] public string Cwd { get; init; } = "";

    /// <summary>Short human title for the session (its first user message), like the IDE tab name.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = "";

    /// <summary>The session's claude.exe pid (resolved by the hooks). &gt;0 = reap when it dies; 0/-1 = unknown,
    /// fall back to the age prune.</summary>
    [JsonPropertyName("pid")] public int Pid { get; init; }

    /// <summary>CLAUDE_CODE_ENTRYPOINT: "cli", "claude-desktop", …</summary>
    [JsonPropertyName("entrypoint")] public string Entrypoint { get; init; } = "";

    /// <summary>False only for a merely-opened session (SessionStart seed). Missing = pre-upgrade file = true.</summary>
    [JsonPropertyName("started")] public bool Started { get; init; } = true;

    public static StatusState Idle => new("idle", "", "", "", "", "", 0, 0);
}

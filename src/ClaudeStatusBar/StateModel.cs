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

    public static StatusState Idle => new("idle", "", "", "", "", "", 0, 0);
}

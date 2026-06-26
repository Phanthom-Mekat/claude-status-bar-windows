using System.IO;
using System.Text.Json;

namespace ClaudeStatusBar;

/// <summary>Reads every session's state from sessions.d/ (plus the legacy state.json), applies per-session
/// staleness + interrupt recovery, and returns the busiest session so the icon reflects whichever session
/// is actually working — not just the last writer.</summary>
public class SessionAggregator
{
    const long StaleSec = 900;

    public (StatusState state, int activeCount) Read()
    {
        var list = new List<StatusState>();
        try
        {
            if (Directory.Exists(Paths.SessionsDir))
                foreach (var f in Directory.GetFiles(Paths.SessionsDir))
                {
                    var name = Path.GetFileName(f);
                    var s = Parse(f) ?? StatusState.Idle with { SessionId = name, Ts = ToUnix(File.GetLastWriteTimeUtc(f)) };
                    if (string.IsNullOrEmpty(s.SessionId)) s = s with { SessionId = name };
                    list.Add(s);
                }
        }
        catch { }

        var global = Parse(Paths.StateJson);
        if (global != null) list.Add(global with { SessionId = string.IsNullOrEmpty(global.SessionId) ? "__global__" : global.SessionId });

        return Aggregate(list, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    /// <summary>Pure core: dedupe by session id, drop stale/interrupted to idle, return the busiest + active count.</summary>
    public static (StatusState state, int activeCount) Aggregate(IEnumerable<StatusState> states, long now)
    {
        var bySid = new Dictionary<string, StatusState>();
        foreach (var raw in states)
        {
            var eff = Effective(raw, now);
            var key = string.IsNullOrEmpty(eff.SessionId) ? Guid.NewGuid().ToString() : eff.SessionId;
            if (!bySid.TryGetValue(key, out var cur) || Pri(eff) > Pri(cur) || (Pri(eff) == Pri(cur) && eff.Ts > cur.Ts))
                bySid[key] = eff;
        }
        var best = bySid.Values.OrderByDescending(Pri).ThenByDescending(s => s.Ts).FirstOrDefault() ?? StatusState.Idle;
        int active = bySid.Values.Count(s => Pri(s) >= 2); // working / permission
        return (best, active);
    }

    static StatusState? Parse(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            var txt = sr.ReadToEnd();
            if (string.IsNullOrWhiteSpace(txt)) return null;
            return JsonSerializer.Deserialize<StatusState>(txt);
        }
        catch { return null; }
    }

    static StatusState Effective(StatusState s, long now)
    {
        if (s.State is "thinking" or "tool" or "permission")
        {
            if (now - s.Ts > StaleSec) return s with { State = "idle", Label = "" };
            if (!string.IsNullOrEmpty(s.Transcript) && StateReader.TranscriptInterrupted(s.Transcript))
                return s with { State = "idle", Label = "" };
        }
        return s;
    }

    static int Pri(StatusState s) => s.State switch
    {
        "permission" => 3,
        "tool" => 2,
        "thinking" => 2,
        "waiting" => 1,
        _ => 0,
    };

    static long ToUnix(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}

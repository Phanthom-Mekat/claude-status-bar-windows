using System.IO;
using System.Text.Json;

namespace ClaudeStatusBar;

/// <summary>Reads every session's state from sessions.d/ (plus the legacy state.json), applies per-session
/// staleness + interrupt recovery, reaps sessions whose claude.exe has died, and returns the busiest session
/// so the icon reflects whichever session is actually working — not just the last writer.</summary>
public class SessionAggregator
{
    const long StaleSec = 900, PermissionStaleSec = 7200; // an approval prompt can legitimately sit for a while
    public const long HideIdleAfterSec = 900;              // menu rows only; liveness is the pid

    readonly Func<int, bool> _alive;
    readonly Dictionary<string, (DateTime mtime, StatusState? s)> _parsed = new(StringComparer.OrdinalIgnoreCase);
    DateTime _lastReap = DateTime.MinValue;
    StateReader? _global;

    public SessionAggregator(Func<int, bool>? alive = null) { _alive = alive ?? ProcessTree.IsClaudeAlive; }

    /// <summary>Raw sessions from sessions.d. Re-parses only files whose mtime changed (writes are atomic
    /// renames, so a content change always bumps mtime), and every few seconds reaps sessions whose claude.exe
    /// is gone (closed terminal/crash: no SessionEnd fires).</summary>
    public List<StatusState> Load()
    {
        var list = new List<StatusState>();
        try
        {
            if (!Directory.Exists(Paths.SessionsDir)) { _parsed.Clear(); return list; }
            var files = Directory.GetFiles(Paths.SessionsDir).Where(f => !f.EndsWith(".tmp")).ToList();
            foreach (var gone in _parsed.Keys.Except(files, StringComparer.OrdinalIgnoreCase).ToList()) _parsed.Remove(gone);

            bool reap = (DateTime.UtcNow - _lastReap).TotalSeconds >= 3;
            if (reap) _lastReap = DateTime.UtcNow;
            foreach (var f in files)
            {
                var mt = File.GetLastWriteTimeUtc(f);
                if (!_parsed.TryGetValue(f, out var hit) || hit.mtime != mt)
                    _parsed[f] = hit = (mt, Parse(f) ?? StatusState.Idle with { Ts = ToUnix(mt) });
                var s = hit.s!;
                if (string.IsNullOrEmpty(s.SessionId)) s = s with { SessionId = Path.GetFileName(f) };
                if (reap && s.Pid > 0 && !_alive(s.Pid))
                {
                    try { File.Delete(f); } catch { }
                    _parsed.Remove(f);
                    continue;
                }
                list.Add(s);
            }
        }
        catch { }
        return list;
    }

    public (StatusState state, int activeCount) Read() => Read(Load());

    /// <summary>Busiest + active count over already-loaded sessions. The legacy global state.json only counts
    /// when it has no session id: otherwise sessions.d is authoritative (a reaped session's frozen global
    /// "tool" must not keep the icon spinning).</summary>
    public (StatusState state, int activeCount) Read(List<StatusState> sessions)
    {
        var all = new List<StatusState>(sessions);
        var global = (_global ??= new StateReader(Paths.StateJson)).Poll(); // mtime-gated
        if (global != null && string.IsNullOrEmpty(global.SessionId)) all.Add(global with { SessionId = "__global__" });
        return Aggregate(all, Now());
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

    /// <summary>All live sessions, effective state, busiest-first.</summary>
    public List<StatusState> LiveSessions() => Order(Load(), Now());

    /// <summary>Pure: drop stale/interrupted to idle, then sort busiest-first (ties by most-recent).</summary>
    public static List<StatusState> Order(IEnumerable<StatusState> states, long now) =>
        states.Select(s => Effective(s, now)).OrderByDescending(Pri).ThenByDescending(s => s.Ts).ToList();

    /// <summary>Pure: which (ordered, effective) sessions get a menu row. A desktop conversation that was merely
    /// opened (started=false, resting) stays hidden — CLI/editor sessions are deliberate, so they show at once.
    /// Rows resting longer than <see cref="HideIdleAfterSec"/> hide too, but never all of them: the most recent
    /// one stays so the list isn't empty while a session is alive.</summary>
    public static List<StatusState> Visible(List<StatusState> ordered, long now)
    {
        static bool Resting(StatusState s) => s.State is not ("permission" or "thinking" or "tool");
        var shown = ordered.Where(s => !(s.Entrypoint == "claude-desktop" && !s.Started && Resting(s))).ToList();
        var visible = shown.Where(s => !(Resting(s) && now - s.Ts > HideIdleAfterSec)).ToList();
        if (visible.Count == 0 && shown.Count > 0) visible.Add(shown.MaxBy(s => s.Ts)!);
        return visible;
    }

    /// <summary>Pure: label for each session's row — the folder name, qualified with its parent folder when two
    /// live sessions share a name from different places ("work/myrepo" vs "tmp/myrepo").</summary>
    public static Dictionary<string, string> DisplayNames(IEnumerable<StatusState> sessions)
    {
        var list = sessions.ToList();
        var collide = list.Where(s => s.Project.Length > 0 && s.Cwd.Length > 0)
            .GroupBy(s => s.Project, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(s => NormPath(s.Cwd)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>();
        foreach (var s in list)
        {
            var name = s.Project.Length > 0 ? s.Project : "session";
            if (s.Cwd.Length > 0 && collide.Contains(s.Project))
            {
                var parent = Path.GetFileName(Path.GetDirectoryName(NormPath(s.Cwd)) ?? "");
                if (parent.Length > 0) name = parent + "/" + s.Project;
            }
            names[s.SessionId] = name;
        }
        return names;
    }

    // Forward slashes: Path.GetDirectoryName splits on '/' on both Windows and Linux (Linux ignores '\').
    static string NormPath(string p) => p.Replace('\\', '/').TrimEnd('/');

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
            if (now - s.Ts > (s.State == "permission" ? PermissionStaleSec : StaleSec)) return s with { State = "idle", Label = "" };
            if (!string.IsNullOrEmpty(s.Transcript) && StateReader.TranscriptTurnEnded(s.Transcript))
                return s with { State = "idle", Label = "" };
        }
        return s;
    }

    public static int Pri(StatusState s) => s.State switch
    {
        "permission" => 3,
        "tool" => 2,
        "thinking" => 2,
        "waiting" => 1,
        _ => 0,
    };

    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    static long ToUnix(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}

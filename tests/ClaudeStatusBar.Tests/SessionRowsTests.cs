using System.IO;
using ClaudeStatusBar;
using Xunit;

public class SessionRowsTests
{
    static StatusState S(string state, string sid, long ts, string entry = "cli", bool started = true, string proj = "p", string cwd = "") =>
        new StatusState(state, "", "", proj, sid, "", 0, ts) { Entrypoint = entry, Started = started, Cwd = cwd };

    [Fact]
    public void MerelyOpenedDesktopConversationIsHidden()
    {
        var rows = SessionAggregator.Visible(new() { S("idle", "d", 1000, "claude-desktop", started: false), S("idle", "c", 990) }, now: 1000);
        Assert.Equal(new[] { "c" }, rows.Select(r => r.SessionId));
    }

    [Fact]
    public void UnstartedCliSessionShowsImmediately()
    {
        var rows = SessionAggregator.Visible(new() { S("idle", "c", 1000, "cli", started: false) }, now: 1000);
        Assert.Single(rows);
    }

    [Fact]
    public void LongIdleRowsHideButNeverAll()
    {
        var rows = SessionAggregator.Visible(new() { S("idle", "old", 0), S("idle", "older", -100) }, now: 5000);
        Assert.Equal(new[] { "old" }, rows.Select(r => r.SessionId)); // floor: the most recent stays
        rows = SessionAggregator.Visible(new() { S("tool", "w", 4990), S("idle", "old", 0) }, now: 5000);
        Assert.Equal(new[] { "w" }, rows.Select(r => r.SessionId));
    }

    [Fact]
    public void SameNamedProjectsGetParentQualifier()
    {
        var names = SessionAggregator.DisplayNames(new[]
        {
            S("idle", "a", 1, proj: "repo", cwd: @"C:\work\repo"),
            S("idle", "b", 1, proj: "repo", cwd: "C:/tmp/repo"),
            S("idle", "c", 1, proj: "other", cwd: @"C:\x\other"),
            S("idle", "d", 1, proj: "repo", cwd: @"C:\work\repo\"), // same place as a: not a collision by itself
        });
        Assert.Equal("work/repo", names["a"]);
        Assert.Equal("tmp/repo", names["b"]);
        Assert.Equal("other", names["c"]);
    }

    [Fact]
    public void PermissionOutlivesTheFifteenMinuteStaleWindow()
    {
        var (st, _) = SessionAggregator.Aggregate(new[] { S("permission", "a", ts: 0) }, now: 3600);
        Assert.Equal("permission", st.State);  // an hour-old approval prompt is still waiting on you
        (st, _) = SessionAggregator.Aggregate(new[] { S("permission", "a", ts: 0) }, now: 7300);
        Assert.Equal("idle", st.State);
    }
}

// Load() touches sessions.d under USERPROFILE, which is process-wide — keep these serial.
[Collection("home")]
public class SessionReapTests
{
    [Fact]
    public void ReapsSessionsWhoseClaudeProcessDied()
    {
        var home = Path.Combine(Path.GetTempPath(), "csb-home-" + Guid.NewGuid());
        var prev = Environment.GetEnvironmentVariable("USERPROFILE");
        Environment.SetEnvironmentVariable("USERPROFILE", home);
        try
        {
            Directory.CreateDirectory(Paths.SessionsDir);
            File.WriteAllText(Path.Combine(Paths.SessionsDir, "alive"), "{\"state\":\"idle\",\"sessionId\":\"alive\",\"pid\":111}");
            File.WriteAllText(Path.Combine(Paths.SessionsDir, "dead"), "{\"state\":\"tool\",\"sessionId\":\"dead\",\"pid\":222}");
            File.WriteAllText(Path.Combine(Paths.SessionsDir, "legacy"), "{\"state\":\"idle\",\"sessionId\":\"legacy\"}"); // no pid: kept (age prune)

            var agg = new SessionAggregator(alive: pid => pid == 111);
            var ids = agg.Load().Select(s => s.SessionId).OrderBy(x => x).ToList();

            Assert.Equal(new[] { "alive", "legacy" }, ids);
            Assert.False(File.Exists(Path.Combine(Paths.SessionsDir, "dead"))); // file removed too
        }
        finally { Environment.SetEnvironmentVariable("USERPROFILE", prev); }
    }
}

public class ProcessTreeTests
{
    [Fact]
    public void NonClaudeAndMissingProcessesAreNotAlive()
    {
        Assert.False(ProcessTree.IsClaudeAlive(Environment.ProcessId)); // the test host isn't claude.exe
        Assert.False(ProcessTree.IsClaudeAlive(0));
        Assert.False(ProcessTree.IsClaudeAlive(int.MaxValue));
    }

    [Fact]
    public void AncestorWalkTerminates()
    {
        var pid = ProcessTree.FindClaudeAncestor(Environment.ProcessId); // -1 in CI; a claude pid under Claude Code
        Assert.True(pid == -1 || ProcessTree.IsClaudeAlive(pid));
    }
}

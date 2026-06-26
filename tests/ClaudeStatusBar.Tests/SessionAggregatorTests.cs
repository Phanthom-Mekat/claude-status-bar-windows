using ClaudeStatusBar;
using Xunit;

public class SessionAggregatorTests
{
    static StatusState S(string state, string sid, long ts, long startedAt = 0, string proj = "") =>
        new(state, "", "", proj, sid, "", startedAt, ts);

    [Fact]
    public void BusiestSessionWins()
    {
        var states = new[] { S("idle", "a", 990), S("tool", "b", 995, 980, "proj") };
        var (st, active) = SessionAggregator.Aggregate(states, now: 1000);
        Assert.Equal("tool", st.State);
        Assert.Equal("b", st.SessionId);
        Assert.Equal(1, active);
    }

    [Fact]
    public void PermissionBeatsWorking()
    {
        var states = new[] { S("tool", "a", 999), S("permission", "b", 998) };
        var (st, active) = SessionAggregator.Aggregate(states, now: 1000);
        Assert.Equal("permission", st.State);
        Assert.Equal(2, active);
    }

    [Fact]
    public void StaleThinkingDropsToIdle()
    {
        var states = new[] { S("thinking", "a", ts: 1000) }; // 1000s old > 900 stale window
        var (st, active) = SessionAggregator.Aggregate(states, now: 2001);
        Assert.Equal("idle", st.State);
        Assert.Equal(0, active);
    }

    [Fact]
    public void AllIdleStaysIdle()
    {
        var states = new[] { S("idle", "a", 990), S("done", "b", 995) };
        var (st, active) = SessionAggregator.Aggregate(states, now: 1000);
        Assert.Equal(0, active);
        Assert.True(st.State is "idle" or "done");
    }

    [Fact]
    public void SameSessionDedupedByBusiest()
    {
        // legacy global + per-session for the same id: take the busier, count once
        var states = new[] { S("idle", "a", 1000), S("tool", "a", 999) };
        var (st, active) = SessionAggregator.Aggregate(states, now: 1000);
        Assert.Equal("tool", st.State);
        Assert.Equal(1, active);
    }
}

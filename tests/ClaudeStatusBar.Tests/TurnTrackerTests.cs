using ClaudeStatusBar;
using Xunit;

public class TurnTrackerTests
{
    static StatusState S(string state, string sid, long startedAt = 0) => new(state, "", "", "p", sid, "", startedAt, 0);

    [Fact]
    public void ChimesOnlyForTurnsPastThreshold()
    {
        var t = new TurnTracker(new Random(1));
        Assert.False(t.Update(new[] { S("thinking", "a", startedAt: 1000) }, 60, now: 1000));
        Assert.False(t.Update(new[] { S("done", "a") }, 60, now: 1030));   // 30s turn < 1 min
        Assert.False(t.Update(new[] { S("thinking", "a", startedAt: 2000) }, 60, now: 2000));
        Assert.True(t.Update(new[] { S("done", "a") }, 60, now: 2061));    // 61s turn
        Assert.False(t.Update(new[] { S("done", "a") }, 60, now: 2062));   // edge fires once
    }

    [Fact]
    public void EveryTurnChimesEvenForSubSecondTurns()
    {
        var t = new TurnTracker();
        t.Update(new[] { S("tool", "a", startedAt: 500) }, 0.1, now: 500);
        Assert.True(t.Update(new[] { S("done", "a") }, 0.1, now: 500));
    }

    [Fact]
    public void OffNeverChimes()
    {
        var t = new TurnTracker();
        t.Update(new[] { S("tool", "a", startedAt: 1) }, 0, now: 1);
        Assert.False(t.Update(new[] { S("done", "a") }, 0, now: 5000));
    }

    [Fact]
    public void FirstSightOfDoneDoesNotChime()
    {
        // App launched after the turn finished: never saw it working, so no chime.
        Assert.False(new TurnTracker().Update(new[] { S("done", "a") }, 0.1, now: 100));
    }

    [Fact]
    public void PerSessionTurnsAreIndependent()
    {
        var t = new TurnTracker();
        t.Update(new[] { S("thinking", "a", 100), S("thinking", "b", 190) }, 60, now: 190);
        Assert.False(t.Update(new[] { S("thinking", "a", 100), S("done", "b") }, 60, now: 200)); // b ran 10s
        Assert.True(t.Update(new[] { S("done", "a"), S("done", "b") }, 60, now: 200));          // a ran 100s
    }

    [Fact]
    public void ThinkingWordPickedOnEntryHeldWhileThinkingAndNeverRepeated()
    {
        var t = new TurnTracker(new Random(7));
        t.Update(new[] { S("thinking", "a", 1) }, 0, 1);
        var w1 = t.Word("a");
        Assert.Contains(w1, TurnTracker.Words);
        t.Update(new[] { S("thinking", "a", 1) }, 0, 2);
        Assert.Equal(w1, t.Word("a"));                       // held while still thinking
        t.Update(new[] { S("tool", "a", 1) }, 0, 3);
        t.Update(new[] { S("thinking", "a", 1) }, 0, 4);
        Assert.NotEqual(w1, t.Word("a"));                    // re-entry picks a different word
    }

    [Fact]
    public void ReportsFinishedTurnsWithLengthEvenWhenSoundIsOff()
    {
        var t = new TurnTracker();
        t.Update(new[] { S("tool", "a", startedAt: 100) }, 0, now: 100);
        Assert.Empty(t.Finished);
        t.Update(new[] { S("done", "a") }, 0, now: 172);
        Assert.Equal(new[] { ("a", 72L) }, t.Finished);
        t.Update(new[] { S("done", "a") }, 0, now: 173);
        Assert.Empty(t.Finished);                              // reported once
    }

    [Fact]
    public void ForgetsEndedSessions()
    {
        var t = new TurnTracker();
        t.Update(new[] { S("thinking", "a", 1) }, 0, 1);
        t.Update(Array.Empty<StatusState>(), 0, 2);
        Assert.Equal("", t.Word("a"));
    }
}

using System.IO;
using ClaudeStatusBar;
using Xunit;

public class StateReaderTests
{
    static string TmpDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "csb-" + Guid.NewGuid());
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void ParsesState()
    {
        var f = Path.Combine(TmpDir(), "state.json");
        File.WriteAllText(f, "{\"state\":\"tool\",\"label\":\"Editing\",\"startedAt\":5,\"ts\":9}");
        var s = new StateReader(f).Poll();
        Assert.Equal("tool", s!.State);
        Assert.Equal("Editing", s.Label);
        Assert.Equal(5, s.StartedAt);
    }

    [Fact]
    public void KeepsLastGoodOnCorrupt()
    {
        var f = Path.Combine(TmpDir(), "state.json");
        File.WriteAllText(f, "{\"state\":\"thinking\"}");
        var r = new StateReader(f);
        Assert.Equal("thinking", r.Poll()!.State);

        File.WriteAllText(f, "{ broken");
        File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddSeconds(2));
        Assert.Equal("thinking", r.Poll()!.State); // last good retained, no crash
    }

    [Fact]
    public void DetectsInterruptInTail()
    {
        var f = Path.Combine(TmpDir(), "t.jsonl");
        File.WriteAllText(f, new string('x', 20000) + "\n{\"text\":\"[Request interrupted by user]\"}\n");
        Assert.True(StateReader.TranscriptTurnEnded(f));
    }

    [Fact]
    public void DetectsUsageLimitInTail()
    {
        // Real Claude Code shape: an assistant message flagged isApiErrorMessage whose text says "limit".
        var f = Path.Combine(TmpDir(), "t.jsonl");
        File.WriteAllText(f, "{\"type\":\"assistant\",\"isApiErrorMessage\":true,\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"You've hit your session limit, resets 10pm\"}]}}\n");
        Assert.True(StateReader.TranscriptTurnEnded(f));
    }

    [Fact]
    public void DetectsLimitFollowedByBookkeeping()
    {
        // The limit entry can be followed by a queue/bookkeeping line; still detected via the last-few scan.
        var f = Path.Combine(TmpDir(), "t.jsonl");
        File.WriteAllText(f,
            "{\"type\":\"assistant\",\"isApiErrorMessage\":true,\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"You've hit your usage limit\"}]}}\n" +
            "{\"type\":\"queue-operation\"}\n");
        Assert.True(StateReader.TranscriptTurnEnded(f));
    }

    [Fact]
    public void NoEndWhenAbsent()
    {
        var f = Path.Combine(TmpDir(), "t.jsonl");
        File.WriteAllText(f, "{\"text\":\"hello world\"}\n");
        Assert.False(StateReader.TranscriptTurnEnded(f));
    }

    [Fact]
    public void NoEndWhenTextMerelyMentionsLimit()
    {
        // Ordinary assistant text about a rate limit must NOT count as a turn end (no isApiErrorMessage).
        var f = Path.Combine(TmpDir(), "t.jsonl");
        File.WriteAllText(f, "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"let me add a rate limit to the API\"}]}}\n");
        Assert.False(StateReader.TranscriptTurnEnded(f));
    }
}

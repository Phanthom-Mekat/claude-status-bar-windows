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
        Assert.True(StateReader.TranscriptInterrupted(f));
    }

    [Fact]
    public void NoInterruptWhenAbsent()
    {
        var f = Path.Combine(TmpDir(), "t.jsonl");
        File.WriteAllText(f, "{\"text\":\"hello world\"}\n");
        Assert.False(StateReader.TranscriptInterrupted(f));
    }
}

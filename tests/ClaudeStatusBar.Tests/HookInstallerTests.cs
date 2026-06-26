using ClaudeStatusBar;
using System.Text.Json;
using Xunit;

public class HookInstallerTests
{
    [Fact]
    public void PreservesUnrelatedHooksAndAddsOurs()
    {
        string existing = "{\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"node other.js\"}]}]}}";
        var merged = HookInstaller.MergeSettings(existing, @"C:\u\.claude\statusbar\update.js", @"C:\u\.claude\statusbar\lifecycle.js");

        Assert.Contains("other.js", merged);   // unrelated hook preserved
        Assert.Contains("statusbar", merged);   // ours added

        using var doc = JsonDocument.Parse(merged);
        var hooks = doc.RootElement.GetProperty("hooks");
        Assert.True(hooks.TryGetProperty("SessionStart", out _));
        Assert.True(hooks.TryGetProperty("PreToolUse", out _));
        Assert.Equal(2, hooks.GetProperty("Stop").GetArrayLength()); // unrelated + ours
    }

    [Fact]
    public void IsIdempotent()
    {
        string once = HookInstaller.MergeSettings("{}", @"C:\u\up.js", @"C:\u\lc.js");
        string twice = HookInstaller.MergeSettings(once, @"C:\u\up.js", @"C:\u\lc.js");
        using var doc = JsonDocument.Parse(twice);
        Assert.Equal(1, doc.RootElement.GetProperty("hooks").GetProperty("PreToolUse").GetArrayLength());
    }
}

using System.IO;
using ClaudeStatusBar;
using Xunit;

public class AppSettingsTests
{
    [Fact]
    public void RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), "csb-" + Guid.NewGuid(), "app-settings.json");
        var s = new AppSettings { ShowOverlay = false, AnimStyle = "crab", OverlayX = 42, SoundThreshold = 300, ShowText = false };
        s.SaveTo(path);
        var loaded = AppSettings.LoadFrom(path);
        Assert.Equal("crab", loaded.AnimStyle);
        Assert.Equal(42, loaded.OverlayX);
        Assert.Equal(300, loaded.SoundThreshold);
        Assert.False(loaded.ShowText);
    }

    [Fact]
    public void FloatingPillIsBackOnAfterEveryRestart()
    {
        // Turned off this run -> not remembered; also ignores "false" saved by older versions.
        var path = Path.Combine(Path.GetTempPath(), "csb-" + Guid.NewGuid(), "app-settings.json");
        new AppSettings { ShowOverlay = false }.SaveTo(path);
        Assert.True(AppSettings.LoadFrom(path).ShowOverlay);

        File.WriteAllText(path, "{\"ShowOverlay\":false,\"AnimStyle\":\"mark\"}");
        var old = AppSettings.LoadFrom(path);
        Assert.True(old.ShowOverlay);
        Assert.Equal("mark", old.AnimStyle);
    }

    [Fact]
    public void MigratesLegacySoundToggleToOneMinute()
    {
        var path = Path.Combine(Path.GetTempPath(), "csb-" + Guid.NewGuid(), "app-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"PlayCompletionSound\":true}");
        var loaded = AppSettings.LoadFrom(path);
        Assert.Equal(60, loaded.SoundThreshold);
        Assert.False(loaded.PlayCompletionSound);
    }

    [Fact]
    public void DefaultsWhenMissing()
    {
        var loaded = AppSettings.LoadFrom(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid(), "x.json"));
        Assert.True(loaded.ShowOverlay);   // default on (owner request)
        Assert.Equal("web", loaded.AnimStyle);
        Assert.False(loaded.ShowTimer);
        Assert.True(loaded.ShowText);
        Assert.Equal(0, loaded.SoundThreshold); // chime off by default
        Assert.True(loaded.KeepAwake);
    }

    [Fact]
    public void KeepAwakeHoldsOnlyWhileWorking()
    {
        StatusState S(string st) => StatusState.Idle with { State = st };
        Assert.True(KeepAwake.ShouldHold(new[] { S("idle"), S("tool") }));
        Assert.True(KeepAwake.ShouldHold(new[] { S("thinking") }));
        Assert.False(KeepAwake.ShouldHold(new[] { S("permission"), S("idle"), S("done") }));
        Assert.False(KeepAwake.ShouldHold(Array.Empty<StatusState>()));
    }
}

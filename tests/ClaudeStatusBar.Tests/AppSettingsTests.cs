using System.IO;
using ClaudeStatusBar;
using Xunit;

public class AppSettingsTests
{
    [Fact]
    public void RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), "csb-" + Guid.NewGuid(), "app-settings.json");
        var s = new AppSettings { ShowOverlay = false, AnimStyle = "crab", OverlayX = 42, PlayCompletionSound = true };
        s.SaveTo(path);
        var loaded = AppSettings.LoadFrom(path);
        Assert.False(loaded.ShowOverlay);
        Assert.Equal("crab", loaded.AnimStyle);
        Assert.Equal(42, loaded.OverlayX);
        Assert.True(loaded.PlayCompletionSound);
    }

    [Fact]
    public void DefaultsWhenMissing()
    {
        var loaded = AppSettings.LoadFrom(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid(), "x.json"));
        Assert.True(loaded.ShowOverlay);   // default on (owner request)
        Assert.Equal("web", loaded.AnimStyle);
        Assert.False(loaded.ShowTimer);
    }
}

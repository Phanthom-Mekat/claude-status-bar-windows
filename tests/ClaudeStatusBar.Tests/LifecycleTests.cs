using System;
using ClaudeStatusBar;
using Xunit;

// Self-quit timing. A mock clock + a "needed" override isolate the grace/debounce logic from the
// filesystem and wall clock, so these are deterministic.
public class LifecycleTests
{
    static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // The regression: a fresh install (no InstalledVersion) with no active session must NOT vanish
    // ~8s later — it has to stay through the first-run grace so the user can find + pin the icon.
    [Fact]
    public void FirstRun_StaysAliveThroughGrace_EvenWhenNotNeeded()
    {
        int quits = 0; double sec = 0;
        var life = new Lifecycle(new AppSettings(), () => quits++, null,
            now: () => Start.AddSeconds(sec), needed: () => false);

        foreach (var t in new[] { 6, 30, 90, 175 }) { sec = t; life.CheckLifecycle(); }
        Assert.Equal(0, quits);
    }

    [Fact]
    public void FirstRun_QuitsAfterGraceWhenNotNeeded()
    {
        int quits = 0; double sec = 0;
        var life = new Lifecycle(new AppSettings(), () => quits++, null,
            now: () => Start.AddSeconds(sec), needed: () => false);

        sec = 181; life.CheckLifecycle(); // grace just elapsed -> arm the debounce
        Assert.Equal(0, quits);
        sec = 185; life.CheckLifecycle(); // +debounce -> self-quit
        Assert.Equal(1, quits);
    }

    [Fact]
    public void NotFirstRun_QuitsShortlyWhenNotNeeded()
    {
        int quits = 0; double sec = 0;
        var cfg = new AppSettings { InstalledVersion = "1.2.3.0" };
        var life = new Lifecycle(cfg, () => quits++, null,
            now: () => Start.AddSeconds(sec), needed: () => false);

        sec = 6; life.CheckLifecycle();  // past the 5s launch grace -> arm debounce only
        Assert.Equal(0, quits);
        sec = 10; life.CheckLifecycle(); // +debounce -> self-quit
        Assert.Equal(1, quits);
    }

    [Fact]
    public void NeededKeepsAliveIndefinitely()
    {
        int quits = 0; double sec = 0;
        var life = new Lifecycle(new AppSettings(), () => quits++, null,
            now: () => Start.AddSeconds(sec), needed: () => true);

        foreach (var t in new[] { 6, 200, 1000 }) { sec = t; life.CheckLifecycle(); }
        Assert.Equal(0, quits);
    }
}

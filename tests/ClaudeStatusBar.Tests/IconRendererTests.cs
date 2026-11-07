using ClaudeStatusBar;
using Xunit;

public class IconRendererTests
{
    [Fact]
    public void OrbitSequenceMatchesMacOS()
    {
        using var r = new IconRenderer();
        // en16 + loop110×2 + loop40×4 + ex14
        Assert.Equal(16 + 220 + 160 + 14, r.SequenceLength(AnimKind.Orbit));
        Assert.Equal(16 + 220 + 160, r.FrameCount(AnimKind.Orbit));
        Assert.Equal(16, r.LoopStart(AnimKind.Orbit));
        Assert.True(r.HasOutro(AnimKind.Orbit));
        Assert.False(r.HasOutro(AnimKind.Web));
    }

    [Fact]
    public void OrbitLoopsBackPastTheEntry()
    {
        using var r = new IconRenderer();
        int last = r.FrameCount(AnimKind.Orbit) - 1;
        Assert.Equal(16, r.Next(AnimKind.Orbit, last - 1)); // step 2 off the end -> loop start, never the entry
        Assert.Equal(0, r.Next(AnimKind.Web, r.FrameCount(AnimKind.Web) - 1));
    }

    [Fact]
    public void FramesAreCachedAndEveryOrbitFrameRenders()
    {
        using var r = new IconRenderer();
        Assert.Same(r.FrameBitmap(AnimKind.Web, 1, false, 16), r.FrameBitmap(AnimKind.Web, 1, false, 16));
        Assert.Same(r.Frame(AnimKind.Crab, 2, false, 16), r.Frame(AnimKind.Crab, 2, false, 16));
        for (int i = 0; i < r.SequenceLength(AnimKind.Orbit); i += 7)
            Assert.Equal(22, r.FrameBitmap(AnimKind.Orbit, i, false, 22).Width);
        Assert.NotNull(r.Resting(AnimKind.Orbit, true, 16));
    }

    [Theory]
    [InlineData("web", AnimKind.Web)]
    [InlineData("code", AnimKind.Code)]
    [InlineData("crab", AnimKind.Crab)]
    [InlineData("mark", AnimKind.Orbit)]
    [InlineData("bogus", AnimKind.Web)]
    public void ParsesStyle(string s, AnimKind k) => Assert.Equal(k, IconRenderer.ParseStyle(s));
}

using ClaudeStatusBar;
using Xunit;
using static ClaudeStatusBar.LidMode;

public class LidModeTests
{
    [Theory]
    //          want   overriding lidClosed hot    idleFor  expected
    [InlineData(true,  false,     false,    false, 0,       Act.Override)]        // Claude starts working
    [InlineData(true,  true,      true,     false, 0,       Act.None)]            // working, lid shut, cool: keep going
    [InlineData(true,  true,      true,     true,  0,       Act.RestoreAndSleep)] // too hot with the lid shut: hibernate
    [InlineData(true,  true,      false,    true,  0,       Act.None)]            // hot but lid open: you're there
    [InlineData(false, true,      false,    false, 0,       Act.Restore)]         // done, lid open: give the setting back
    [InlineData(false, true,      true,     false, 5,       Act.None)]            // done, lid shut: grace for an idle blip
    [InlineData(false, true,      true,     false, DoneGraceSec, Act.RestoreAndSleep)] // still done: sleep like Windows would
    [InlineData(false, false,     true,     true,  999,     Act.None)]            // never overrode: hands off
    public void Decides(bool want, bool overriding, bool lidClosed, bool hot, long idleFor, Act expected) =>
        Assert.Equal(expected, Decide(want, overriding, lidClosed, hot, idleFor));
}

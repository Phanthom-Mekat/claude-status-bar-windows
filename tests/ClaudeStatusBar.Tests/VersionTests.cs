using ClaudeStatusBar;
using Xunit;

public class VersionTests
{
    [Theory]
    [InlineData("0.0.10", "0.0.9", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("0.1.0", "0.2.0", false)]
    public void Compares(string a, string b, bool newer) => Assert.Equal(newer, VersionCompare.IsNewer(a, b));
}

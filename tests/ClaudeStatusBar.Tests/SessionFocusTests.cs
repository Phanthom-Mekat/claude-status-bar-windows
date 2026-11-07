using ClaudeStatusBar;
using Xunit;

public class SessionFocusTests
{
    static readonly IntPtr A = new(101), B = new(102), C = new(103), Desk = new(999);

    // claude.exe(1) -> Code ext host(2) -> Code main(3) -> explorer(4): the real VS Code chain.
    static readonly (int, string)[] VsCodeChain = { (1, "claude.exe"), (2, "Code.exe"), (3, "Code.exe"), (4, "explorer.exe") };

    [Fact]
    public void PicksTheEditorWindowForTheSessionsWorkspace()
    {
        var wins = new Dictionary<int, List<(IntPtr, string)>>
        {
            [3] = new() { (A, "notes.md - jaduz - Visual Studio Code"), (B, "Claude chat - claude-status - Visual Studio Code") },
            [4] = new() { (Desk, "Program Manager") },
        };
        // cwd is a subfolder of the workspace the window is titled by
        Assert.Equal(B, SessionFocus.Pick(VsCodeChain, wins, @"D:\claude-status\claude-status-bar-windows"));
        Assert.Equal(A, SessionFocus.Pick(VsCodeChain, wins, @"D:\jaduz\src"));     // "src" is too generic to count
    }

    [Fact]
    public void FallsBackToTheHostsMostRecentWindow()
    {
        var wins = new Dictionary<int, List<(IntPtr, string)>> { [3] = new() { (A, "x - one - Visual Studio Code"), (B, "y - two - Visual Studio Code") } };
        Assert.Equal(A, SessionFocus.Pick(VsCodeChain, wins, @"C:\elsewhere\proj"));
    }

    [Fact]
    public void NeverEscapesToTheDesktop()
    {
        // No editor window (e.g. VS Code closed) -> don't raise explorer's desktop/taskbar.
        var wins = new Dictionary<int, List<(IntPtr, string)>> { [4] = new() { (Desk, "Program Manager") } };
        Assert.Equal(IntPtr.Zero, SessionFocus.Pick(VsCodeChain, wins, @"D:\x\repo"));
    }

    [Theory]
    [InlineData("Code.exe", "vscode")]
    [InlineData("Code - Insiders.exe", "vscode-insiders")]
    [InlineData("Cursor.exe", "cursor")]
    [InlineData("Windsurf.exe", "windsurf")]
    [InlineData("something.exe", "vscode")]
    public void PicksTheEditorsLinkScheme(string host, string scheme) =>
        Assert.Equal(scheme, SessionFocus.EditorScheme(new[] { "claude.exe", host, "explorer.exe" }));

    [Fact]
    public void TerminalSessionRaisesTheTerminal()
    {
        var chain = new[] { (10, "claude.exe"), (11, "pwsh.exe"), (12, "WindowsTerminal.exe"), (4, "explorer.exe") };
        var wins = new Dictionary<int, List<(IntPtr, string)>> { [12] = new() { (C, "✳ Claude Code") } };
        Assert.Equal(C, SessionFocus.Pick(chain, wins, @"D:\repo"));
    }
}

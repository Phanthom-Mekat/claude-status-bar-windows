using System.IO;
using System.Text.Json;
using ClaudeStatusBar;
using Xunit;

public class EditorTrustTests
{
    static bool Trusted(string text)
    {
        using var d = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return d.RootElement.GetProperty(EditorTrust.Key).EnumerateArray().Any(x => x.GetString() == EditorTrust.ExtensionId);
    }

    [Fact]
    public void AddsTheKeyAndKeepsCommentsAndOtherSettings()
    {
        var src = "// my settings\n{\n    // big text\n    \"editor.fontSize\": 16,\n    \"files.autoSave\": \"afterDelay\",\n}\n";
        var outp = EditorTrust.AddTrusted(src)!;
        Assert.True(Trusted(outp));
        Assert.Contains("// big text", outp);
        Assert.Contains("\"editor.fontSize\": 16", outp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\n}")]
    public void WorksOnEmptySettings(string src) => Assert.True(Trusted(EditorTrust.AddTrusted(src)!));

    [Fact]
    public void AppendsToAnExistingList()
    {
        var outp = EditorTrust.AddTrusted("{ \"extensions.confirmedUriHandlerExtensionIds\": [\"vscode.git\"], \"a\": 1 }")!;
        Assert.True(Trusted(outp));
        Assert.Contains("vscode.git", outp);
        Assert.True(Trusted(EditorTrust.AddTrusted("{ \"extensions.confirmedUriHandlerExtensionIds\": [] }")!));
    }

    [Fact]
    public void LeavesAlreadyTrustedAndUnsafeFilesAlone()
    {
        Assert.Null(EditorTrust.AddTrusted("{ \"extensions.confirmedUriHandlerExtensionIds\": [\"Anthropic.claude-code\"] }"));
        Assert.Null(EditorTrust.AddTrusted("{ broken"));
        Assert.Null(EditorTrust.AddTrusted("[1, 2]"));
        Assert.Null(EditorTrust.AddTrusted("{ \"extensions.confirmedUriHandlerExtensionIds\": \"oops\" }"));
        Assert.Null(EditorTrust.AddTrusted("/* { */ { \"a\": 1 }")); // first "{" sits in a comment -> validation refuses
    }

    [Fact]
    public void EnsureWritesOnceWithBackupAndSkipsMissingEditors()
    {
        var dir = Path.Combine(Path.GetTempPath(), "csb-trust-" + Guid.NewGuid(), "User");
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, "settings.json");
        File.WriteAllText(f, "{ \"editor.fontSize\": 16 }");

        Assert.True(EditorTrust.Ensure(f));
        Assert.True(Trusted(File.ReadAllText(f)));
        Assert.Equal("{ \"editor.fontSize\": 16 }", File.ReadAllText(f + ".bak-claudestatusbar"));
        Assert.False(EditorTrust.Ensure(f));                           // already trusted: no rewrite

        Assert.False(EditorTrust.Ensure(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid(), "User", "settings.json")));
    }

    [Fact]
    public void ScrubRemovesTheVsCodeExtensionHostVariable()
    {
        Environment.SetEnvironmentVariable("ELECTRON_RUN_AS_NODE", "1");
        Program.ScrubInheritedEnvironment();
        Assert.Null(Environment.GetEnvironmentVariable("ELECTRON_RUN_AS_NODE"));
    }
}

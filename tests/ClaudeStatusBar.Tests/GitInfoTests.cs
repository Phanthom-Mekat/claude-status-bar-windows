using System.IO;
using ClaudeStatusBar;
using Xunit;

public class GitInfoTests
{
    static string Tmp() { var d = Path.Combine(Path.GetTempPath(), "csb-git-" + Guid.NewGuid()); Directory.CreateDirectory(d); return d; }

    [Theory]
    [InlineData("ref: refs/heads/main\n", "main")]
    [InlineData("ref: refs/heads/feat/login-fix", "feat/login-fix")]
    [InlineData("0123456789abcdef0123456789abcdef01234567", "0123456")] // detached -> short SHA
    [InlineData("ref: refs/remotes/origin/release", "release")] // non-heads ref -> last segment
    [InlineData("garbage", "")]
    public void ParsesHead(string head, string expected) => Assert.Equal(expected, GitInfo.ParseHead(head));

    [Fact]
    public void FindsBranchFromNestedFolder()
    {
        var repo = Tmp();
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        File.WriteAllText(Path.Combine(repo, ".git", "HEAD"), "ref: refs/heads/dev\n");
        var sub = Directory.CreateDirectory(Path.Combine(repo, "src", "deep")).FullName;
        Assert.Equal("dev", GitInfo.Branch(sub));
    }

    [Fact]
    public void FollowsWorktreeGitFile()
    {
        var root = Tmp();
        var gitdir = Directory.CreateDirectory(Path.Combine(root, "main", ".git", "worktrees", "wt")).FullName;
        File.WriteAllText(Path.Combine(gitdir, "HEAD"), "ref: refs/heads/hotfix\n");
        var wt = Directory.CreateDirectory(Path.Combine(root, "wt")).FullName;
        File.WriteAllText(Path.Combine(wt, ".git"), "gitdir: ../main/.git/worktrees/wt\n"); // relative, as git writes it
        Assert.Equal("hotfix", GitInfo.Branch(wt));
    }

    [Fact]
    public void PicksUpGitInitAfterRecheck()
    {
        var dir = Tmp();
        Assert.Equal("", GitInfo.Branch(dir));          // not a repo (cached)
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        File.WriteAllText(Path.Combine(dir, ".git", "HEAD"), "ref: refs/heads/main\n");
        GitInfo.RecheckNonGit(dir);
        Assert.Equal("main", GitInfo.Branch(dir));
    }
}

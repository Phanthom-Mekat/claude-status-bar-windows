using System.IO;
using System.IO.Compression;
using ClaudeStatusBar;
using Xunit;

public class UpdaterTests
{
    static string Dir(string name) { var d = Path.Combine(Path.GetTempPath(), "csb-upd-" + Guid.NewGuid(), name); Directory.CreateDirectory(d); return d; }

    [Fact]
    public void SwapReplacesAddsAndKeepsOldAside()
    {
        var install = Dir("install"); var staging = Dir("staging");
        File.WriteAllText(Path.Combine(install, "a.dll"), "old-a");
        File.WriteAllText(Path.Combine(install, "keep.txt"), "untouched");
        File.WriteAllText(Path.Combine(staging, "a.dll"), "new-a");
        Directory.CreateDirectory(Path.Combine(staging, "sub"));
        File.WriteAllText(Path.Combine(staging, "sub", "b.json"), "new-b");

        Updater.Swap(staging, install);

        Assert.Equal("new-a", File.ReadAllText(Path.Combine(install, "a.dll")));
        Assert.Equal("old-a", File.ReadAllText(Path.Combine(install, "a.dll.old")));
        Assert.Equal("new-b", File.ReadAllText(Path.Combine(install, "sub", "b.json")));
        Assert.Equal("untouched", File.ReadAllText(Path.Combine(install, "keep.txt")));

        Updater.CleanupOld(install);
        Assert.False(File.Exists(Path.Combine(install, "a.dll.old")));
    }

    [Fact]
    public void SwapWorksWhileTheOldFileIsInUse()
    {
        // The running app's dll is open (mapped) — it can't be overwritten, but it can be renamed.
        var install = Dir("install"); var staging = Dir("staging");
        var target = Path.Combine(install, "ClaudeStatusBar.dll");
        File.WriteAllText(target, "old");
        File.WriteAllText(Path.Combine(staging, "ClaudeStatusBar.dll"), "new");
        using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            Updater.Swap(staging, install);
        Assert.Equal("new", File.ReadAllText(target));
    }

    [Fact]
    public void FailedSwapRollsEverythingBack()
    {
        var install = Dir("install"); var staging = Dir("staging");
        File.WriteAllText(Path.Combine(install, "1.dll"), "old-1");
        File.WriteAllText(Path.Combine(install, "2.dll"), "old-2");
        File.WriteAllText(Path.Combine(staging, "1.dll"), "new-1");
        File.WriteAllText(Path.Combine(staging, "2.dll"), "new-2");
        File.WriteAllText(Path.Combine(staging, "0-new.txt"), "brand new");

        // 2.dll held without FileShare.Delete -> it can't be moved aside -> the whole swap must undo.
        using (new FileStream(Path.Combine(install, "2.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => Updater.Swap(staging, install));

        Assert.Equal("old-1", File.ReadAllText(Path.Combine(install, "1.dll")));
        Assert.Equal("old-2", File.ReadAllText(Path.Combine(install, "2.dll")));
        Assert.False(File.Exists(Path.Combine(install, "0-new.txt")));
        Assert.Empty(Directory.GetFiles(install, "*.old"));
    }

    static string ZipOf(params (string name, string srcPath)[] files)
    {
        var zip = Path.Combine(Dir("dl"), "ClaudeStatusBar.zip");
        using var z = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (name, src) in files) z.CreateEntryFromFile(src, name);
        return zip;
    }

    // The real app assembly (the test references it), so version checks run against genuine metadata.
    static string AppDll => typeof(Updater).Assembly.Location;
    static string AppVersion => typeof(Updater).Assembly.GetName().Version!.ToString(3);

    [Fact]
    public void ApplyZipInstallsTheVerifiedVersion()
    {
        var install = Dir("install");
        File.WriteAllText(Path.Combine(install, Updater.AppDll), "old");
        Updater.ApplyZip(ZipOf((Updater.AppDll, AppDll)), install, AppVersion);
        Assert.Equal(new FileInfo(AppDll).Length, new FileInfo(Path.Combine(install, Updater.AppDll)).Length);
    }

    [Fact]
    public void ApplyZipRejectsTheWrongVersionAndChangesNothing()
    {
        var install = Dir("install");
        File.WriteAllText(Path.Combine(install, Updater.AppDll), "old");
        Assert.Throws<InvalidDataException>(() => Updater.ApplyZip(ZipOf((Updater.AppDll, AppDll)), install, "99.0.0"));
        Assert.Equal("old", File.ReadAllText(Path.Combine(install, Updater.AppDll)));
    }

    [Fact]
    public void ApplyZipRejectsABundleWithoutTheApp()
    {
        var install = Dir("install");
        var other = Path.Combine(Dir("x"), "readme.txt"); File.WriteAllText(other, "hi");
        Assert.Throws<InvalidDataException>(() => Updater.ApplyZip(ZipOf(("readme.txt", other)), install, AppVersion));
        Assert.Empty(Directory.GetFiles(install));
    }
}

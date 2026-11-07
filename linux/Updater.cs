using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ClaudeStatusBar;

/// <summary>One-click self-update (Linux): download the release tarball for the version the daily check found,
/// verify it really is that version of this app, swap it in, relaunch. Linux lets a running program's folder be
/// renamed, so the swap is two renames between sibling folders on the same filesystem: install → .old, staged →
/// install. A failure renames the old folder back, so a failed update changes nothing. The .old folder is deleted
/// on the next start.
/// ponytail: trusts HTTPS + the GitHub release (unsigned), same as Windows; add a signed checksum if releases get signed.</summary>
public static class Updater
{
    const long MaxBytes = 160L * 1024 * 1024; // the tarball is ~40 MB

    public static string InstallDir => Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
    static string Rid => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";

    // Test seam for linux/test: a loopback-only override of where releases are downloaded from.
    static string ReleaseBase(string version) =>
        Environment.GetEnvironmentVariable("CLAUDE_STATUSBAR_UPDATE_BASE") is { } o && o.StartsWith("http://127.0.0.1:")
            ? o.TrimEnd('/')
            : $"https://github.com/{UpdateChecker.Repo}/releases/download/v{version}";

    /// <summary>Download + apply <paramref name="version"/>. Throws with a readable message on failure.</summary>
    public static async Task UpdateAsync(string version)
    {
        var dir = InstallDir;
        var parent = Path.GetDirectoryName(dir)!;
        var staged = Path.Combine(parent, $".{Path.GetFileName(dir)}.new-{Guid.NewGuid():N}");
        var tgz = staged + ".tar.gz";
        try
        {
            await DownloadAsync($"{ReleaseBase(version)}/ClaudeStatusBar-{Rid}.tar.gz", tgz);
            Directory.CreateDirectory(staged);
            await using (var fs = File.OpenRead(tgz))
            await using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                await TarFile.ExtractToDirectoryAsync(gz, staged, overwriteFiles: true); // rejects entries outside staged
            Verify(staged, version);
            Swap(staged, dir);
        }
        finally
        {
            try { File.Delete(tgz); } catch { }
            try { if (Directory.Exists(staged)) Directory.Delete(staged, true); } catch { }
        }
    }

    static async Task DownloadAsync(string url, string dest)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ClaudeStatusBar-Updater");
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(dest);
        var buf = new byte[81920];
        long total = 0;
        for (int n; (n = await src.ReadAsync(buf)) > 0;)
        {
            if ((total += n) > MaxBytes) throw new InvalidDataException("download is unexpectedly large");
            await dst.WriteAsync(buf.AsMemory(0, n));
        }
    }

    /// <summary>The staged folder must hold ClaudeStatusBar <paramref name="expected"/> (read from the dll's metadata;
    /// nothing is executed) plus its launcher, which is made executable.</summary>
    static void Verify(string staged, string expected)
    {
        var dll = Path.Combine(staged, "ClaudeStatusBar.dll");
        var bin = Path.Combine(staged, "ClaudeStatusBar");
        if (!File.Exists(dll) || !File.Exists(bin)) throw new InvalidDataException("the download isn't a Claude Status Bar bundle");
        var got = AssemblyName.GetAssemblyName(dll);
        if (got.Name != "ClaudeStatusBar" || !SameVersion(got.Version, expected))
            throw new InvalidDataException($"expected v{expected}, the download is {got.Name} v{got.Version}");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(bin, (UnixFileMode)0b111_101_101); // 755
    }

    static bool SameVersion(Version? v, string expected) =>
        v != null && Version.TryParse(expected, out var e) && Norm(v) == Norm(e);
    static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    static void Swap(string staged, string dir)
    {
        var old = Path.Combine(Path.GetDirectoryName(dir)!, $".{Path.GetFileName(dir)}.old-{Guid.NewGuid():N}");
        Directory.Move(dir, old);
        try { Directory.Move(staged, dir); }
        catch { Directory.Move(old, dir); throw; }
    }

    /// <summary>Delete the folders a previous update (or an interrupted one) left next to the install.</summary>
    public static void CleanupOld()
    {
        try
        {
            var dir = InstallDir;
            var name = Path.GetFileName(dir);
            foreach (var d in Directory.GetDirectories(Path.GetDirectoryName(dir)!, $".{name}.*"))
                if (d.Contains($".{name}.old-") || d.Contains($".{name}.new-"))
                    try { Directory.Delete(d, true); } catch { }
        }
        catch { }
    }

    /// <summary>Start the freshly installed app; it waits for this process to exit (and free the instance lock).</summary>
    public static void Relaunch() =>
        Process.Start(new ProcessStartInfo(Path.Combine(InstallDir, "ClaudeStatusBar"))
        {
            ArgumentList = { "--wait-pid", Environment.ProcessId.ToString() },
            UseShellExecute = false,
            WorkingDirectory = InstallDir,
        })?.Dispose();
}

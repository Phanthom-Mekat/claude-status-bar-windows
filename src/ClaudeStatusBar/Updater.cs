using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;

namespace ClaudeStatusBar;

/// <summary>One-click self-update: download the release bundle for the version the daily check found, verify it
/// really is that version of this app, swap it into the install folder, relaunch. A running app's files can't be
/// overwritten on Windows but CAN be renamed, so each old file moves aside to *.old (deleted on the next start)
/// and the new one is copied in; any failure rolls every file back, so a failed update changes nothing.
/// ponytail: trusts HTTPS + the GitHub release (no signature check; the release is unsigned) — add a signed
/// checksum if releases ever get signed.</summary>
public static class Updater
{
    public const string AppDll = "ClaudeStatusBar.dll";
    const long MaxZipBytes = 64L * 1024 * 1024;

    public static string InstallDir => AppContext.BaseDirectory;

    /// <summary>Download + apply <paramref name="version"/>. Throws with a readable message on failure.</summary>
    public static async Task UpdateAsync(string version)
    {
        // The release is a loose-dll bundle; a single-file build has no dll next to it and can't take it.
        if (!File.Exists(Path.Combine(InstallDir, AppDll))) throw new InvalidOperationException("this build can't self-update");
        var work = Path.Combine(Path.GetTempPath(), "ClaudeStatusBar-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var zip = Path.Combine(work, "ClaudeStatusBar.zip");
            await DownloadAsync($"https://github.com/{UpdateChecker.Repo}/releases/download/v{version}/ClaudeStatusBar.zip", zip);
            ApplyZip(zip, InstallDir, version);
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    static async Task DownloadAsync(string url, string dest)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ClaudeStatusBar-Updater");
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(dest);
        var buf = new byte[81920];
        long total = 0;
        for (int n; (n = await src.ReadAsync(buf)) > 0;)
        {
            if ((total += n) > MaxZipBytes) throw new InvalidDataException("download is unexpectedly large");
            await dst.WriteAsync(buf.AsMemory(0, n));
        }
    }

    /// <summary>Extract, verify the bundle is ClaudeStatusBar <paramref name="expectedVersion"/>, then swap it in.</summary>
    public static void ApplyZip(string zip, string installDir, string expectedVersion)
    {
        var staging = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(zip))!, "staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(zip, staging); // throws on entries that would land outside staging
            var dll = Path.Combine(staging, AppDll);
            if (!File.Exists(dll)) throw new InvalidDataException("the download isn't a Claude Status Bar bundle");
            var got = AssemblyName.GetAssemblyName(dll); // metadata only, nothing is executed
            if (got.Name != "ClaudeStatusBar" || !SameVersion(got.Version, expectedVersion))
                throw new InvalidDataException($"expected v{expectedVersion}, the download is {got.Name} v{got.Version}");
            Swap(staging, installDir);
        }
        finally { try { Directory.Delete(staging, true); } catch { } }
    }

    static bool SameVersion(Version? v, string expected) =>
        v != null && Version.TryParse(expected, out var e) && Norm(v) == Norm(e);
    static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    /// <summary>Move every file of <paramref name="staging"/> into <paramref name="installDir"/>, renaming what it
    /// replaces to *.old. All-or-nothing: on any failure, everything already swapped is put back.</summary>
    public static void Swap(string staging, string installDir)
    {
        var done = new List<(string target, string? old)>();
        try
        {
            foreach (var src in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(installDir, Path.GetRelativePath(staging, src));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string? old = null;
                if (File.Exists(target))
                {
                    old = target + ".old";
                    if (File.Exists(old))
                        try { File.Delete(old); }
                        catch { old = $"{target}.{Guid.NewGuid():N}.old"; } // still locked by a previous run
                    File.Move(target, old);
                }
                done.Add((target, old)); // before the copy, so a failed copy is rolled back too
                File.Copy(src, target);
            }
        }
        catch
        {
            for (int i = done.Count - 1; i >= 0; i--)
            {
                var (target, old) = done[i];
                try { if (File.Exists(target)) File.Delete(target); } catch { }
                try { if (old != null) File.Move(old, target); } catch { }
            }
            throw;
        }
    }

    /// <summary>Delete the *.old files a previous update left behind (they were locked by the old process).</summary>
    public static void CleanupOld(string installDir)
    {
        try
        {
            foreach (var f in Directory.GetFiles(installDir, "*.old", SearchOption.AllDirectories))
                try { File.Delete(f); } catch { }
        }
        catch { }
    }

    /// <summary>Start the freshly installed app; it waits for this process to exit before taking the
    /// single-instance mutex. Same host as now: the dotnet host (Smart App Control) or the exe.</summary>
    public static void Relaunch()
    {
        var me = Environment.ProcessId.ToString();
        var host = Environment.ProcessPath ?? "";
        var psi = Path.GetFileName(host).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo(host) { ArgumentList = { Path.Combine(InstallDir, AppDll), "--wait-pid", me } }
            : new ProcessStartInfo(Path.Combine(InstallDir, "ClaudeStatusBar.exe")) { ArgumentList = { "--wait-pid", me } };
        psi.UseShellExecute = false;
        psi.WorkingDirectory = InstallDir;
        Process.Start(psi);
    }
}

using System.IO;
using System.Text;
using System.Text.Json;

namespace ClaudeStatusBar;

/// <summary>mtime-gated reader for state.json (keeps last-good on parse failure) + transcript interrupt detection.</summary>
public class StateReader
{
    readonly string _path;
    DateTime _lastMTime = DateTime.MinValue;
    StatusState? _last;

    public StateReader(string path) { _path = path; }

    /// <summary>Returns the current state, re-reading only when the file's mtime changes. Last-good on corrupt/missing.</summary>
    public StatusState? Poll()
    {
        try
        {
            var mt = File.GetLastWriteTimeUtc(_path);
            if (mt == _lastMTime) return _last;
            _lastMTime = mt;
            // Permissive sharing so we never block the hook's rename (Windows MoveFileEx is lock-sensitive).
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            var parsed = JsonSerializer.Deserialize<StatusState>(sr.ReadToEnd());
            if (parsed != null) _last = parsed; // keep last-good when null/partial
        }
        catch { /* keep last good */ }
        return _last;
    }

    /// <summary>True if the tail (~8 KB) of the transcript's last non-empty line marks a user interrupt.</summary>
    public static bool TranscriptInterrupted(string transcriptPath)
    {
        try
        {
            using var fs = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, fs.Length - 8192);
            fs.Seek(start, SeekOrigin.Begin);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            var tail = sr.ReadToEnd();
            var lastLine = tail.Split('\n').Where(l => l.Trim().Length > 0).LastOrDefault() ?? "";
            return lastLine.Contains("interrupted by user", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

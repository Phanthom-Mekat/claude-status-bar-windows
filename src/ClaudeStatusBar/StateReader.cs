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

    /// <summary>True if the tail (~8 KB) of the transcript shows the turn ENDED with no Stop hook firing,
    /// so a frozen thinking/tool state must drop to idle. Two such cases:
    ///   • user interrupt — the last line marks "interrupted by user" (Esc / denied permission);
    ///   • usage/session/rate limit — Claude Code writes an assistant message with isApiErrorMessage:true,
    ///     e.g. "You've hit your session limit · resets 10pm". No Stop fires, so state.json freezes.
    /// The isApiErrorMessage flag keeps this precise: ordinary text that merely mentions "limit" (code,
    /// discussion) is ignored. The limit entry can be followed by a bookkeeping line, so we scan the last
    /// few entries for it (the interrupt marker is only ever the final line).</summary>
    public static bool TranscriptTurnEnded(string transcriptPath)
    {
        try
        {
            using var fs = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, fs.Length - 8192);
            fs.Seek(start, SeekOrigin.Begin);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            var lines = sr.ReadToEnd().Split('\n').Where(l => l.Trim().Length > 0).ToList();
            if (lines.Count == 0) return false;
            if (lines[^1].Contains("interrupted by user", StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var l in lines.Skip(Math.Max(0, lines.Count - 4)))
                if (l.Contains("isApiErrorMessage", StringComparison.OrdinalIgnoreCase)
                    && l.Contains("limit", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        catch { return false; }
    }
}

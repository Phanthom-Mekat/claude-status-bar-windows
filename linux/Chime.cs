using System.Diagnostics;
using System.Reflection;

namespace ClaudeStatusBar;

/// <summary>The completion chime (the macOS clip, as WAV). Played by whichever player the desktop has:
/// PipeWire, then PulseAudio, then ALSA. No audio library dependency.</summary>
public static class Chime
{
    static readonly (string exe, string[] args)[] Players =
    {
        ("pw-play", Array.Empty<string>()),
        ("paplay", Array.Empty<string>()),
        ("aplay", new[] { "-q" }),
    };

    public static void Play()
    {
        try
        {
            var path = Path.Combine(Paths.StatusbarDir, "completion.wav");
            if (!File.Exists(path))
            {
                var asm = Assembly.GetExecutingAssembly();
                var name = asm.GetManifestResourceNames().First(n => n.EndsWith("completion.wav"));
                using var src = asm.GetManifestResourceStream(name)!;
                using var dst = File.Create(path);
                src.CopyTo(dst);
            }
            foreach (var (exe, args) in Players)
            {
                try
                {
                    var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
                    foreach (var a in args) psi.ArgumentList.Add(a);
                    psi.ArgumentList.Add(path);
                    using var _ = Process.Start(psi);
                    return;
                }
                catch { /* not installed — try the next */ }
            }
            Log.Write("chime: no audio player (pw-play/paplay/aplay) found");
        }
        catch (Exception e) { Log.Write("chime: " + e.Message); }
    }
}

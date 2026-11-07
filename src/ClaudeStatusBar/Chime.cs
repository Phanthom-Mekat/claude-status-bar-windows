using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ClaudeStatusBar;

/// <summary>The completion chime: the macOS clip, played through winmm MCI (handles MP3 natively, no extra
/// dependency). Falls back to the system Asterisk sound if MCI can't open it.</summary>
public static class Chime
{
    const string Alias = "csbchime";
    static bool _opened;

    public static void Play()
    {
        try
        {
            if (!_opened)
            {
                var path = Path.Combine(Paths.StatusbarDir, "completion.mp3");
                if (!File.Exists(path))
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var name = asm.GetManifestResourceNames().First(n => n.EndsWith("completion.mp3"));
                    using var src = asm.GetManifestResourceStream(name)!;
                    using var dst = File.Create(path);
                    src.CopyTo(dst);
                }
                if (Mci($"open \"{path}\" type mpegvideo alias {Alias}") != 0) throw new IOException("mci open failed");
                Mci($"setaudio {Alias} volume to 700"); // the clip is loud at full volume (macOS plays it at 0.7)
                _opened = true;
            }
            Mci($"play {Alias} from 0");
        }
        catch (Exception e)
        {
            Log.Write("chime: " + e.Message);
            System.Media.SystemSounds.Asterisk.Play();
        }
    }

    static int Mci(string cmd) => mciSendString(cmd, null, 0, IntPtr.Zero);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern int mciSendString(string command, System.Text.StringBuilder? ret, int retLen, IntPtr hwnd);
}

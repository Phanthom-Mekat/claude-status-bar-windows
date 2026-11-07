using System.Runtime.InteropServices;

namespace ClaudeStatusBar;

/// <summary>Stops Windows idle-sleeping while any Claude session is working (Insomnia-style, no admin:
/// the same request a video player makes). The screen may still turn off; lid close / Start > Sleep still sleep.
/// SetThreadExecutionState is per-thread, so call <see cref="Apply"/> from the UI thread only.</summary>
public static class KeepAwake
{
    const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);

    public static bool Held { get; private set; }

    /// <summary>Hold only while a session is thinking/running a tool — not while it waits for your approval
    /// (nothing runs then, and a prompt left overnight shouldn't pin the PC awake). Input is the effective
    /// (staleness-applied) session list, so a crashed "working" session lets go after the usual 15 min.</summary>
    public static bool ShouldHold(IEnumerable<StatusState> sessions) => sessions.Any(s => s.State is "thinking" or "tool");

    public static void Apply(bool hold)
    {
        if (hold == Held) return;
        Held = hold;
        if (hold) SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
        else
        {
            SetThreadExecutionState(ES_CONTINUOUS);      // drop the hold
            SetThreadExecutionState(ES_SYSTEM_REQUIRED); // one-shot idle-timer reset: a full sleep timeout from "done", not instant sleep
        }
        Log.Write(hold ? "keep-awake: on (Claude working)" : "keep-awake: off");
    }
}

using Tmds.DBus.Protocol;

namespace ClaudeStatusBar;

/// <summary>Stops the desktop idle-suspending while any Claude session is working, through the desktop portal's
/// Inhibit call (GNOME, KDE, …: the same request a video player makes; no root). Only the Suspend flag: the screen
/// can still blank, and closing the lid still sleeps. The inhibit lives on our own DBus connection, so if the app
/// dies the desktop drops it by itself. Linux counterpart of the Windows KeepAwake (SetThreadExecutionState).</summary>
public static class KeepAwake
{
    const uint InhibitSuspend = 4;
    static readonly SemaphoreSlim Gate = new(1, 1);
    static Connection? _conn;
    static string? _request; // the portal's handle for our inhibit; closing it releases

    public static bool Held { get; private set; }

    /// <summary>Hold only while a session is thinking/running a tool — not while it waits for your approval.</summary>
    public static bool ShouldHold(IEnumerable<StatusState> sessions) => sessions.Any(s => s.State is "thinking" or "tool");

    public static void Apply(bool hold)
    {
        if (hold == Held) return;
        Held = hold;
        Log.Write(hold ? "keep-awake: on (Claude working)" : "keep-awake: off");
        _ = SyncAsync();
    }

    // Serialized, and re-reads Held: quick on/off flips settle on the latest wish.
    static async Task SyncAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (Held && _request == null)
            {
                var c = await ConnAsync();
                _request = await c.CallMethodAsync(InhibitMessage(c), (m, _) => m.GetBodyReader().ReadObjectPathAsString(), null);
            }
            else if (!Held && _request != null)
            {
                var r = _request; _request = null;
                var c = await ConnAsync();
                await c.CallMethodAsync(CloseMessage(c, r));
            }
        }
        catch (Exception e) { Log.Write("keep-awake: portal unavailable: " + e.Message); _conn?.Dispose(); _conn = null; }
        finally { Gate.Release(); }
    }

    static async Task<Connection> ConnAsync()
    {
        if (_conn != null) return _conn;
        var addr = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") ?? throw new InvalidOperationException("no session bus");
        var c = new Connection(addr);
        await c.ConnectAsync();
        return _conn = c;
    }

    // MessageWriter is a ref struct: messages are built in plain (non-async) helpers.
    static MessageBuffer InhibitMessage(Connection c)
    {
        using var w = c.GetMessageWriter();
        w.WriteMethodCallHeader("org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop",
            "org.freedesktop.portal.Inhibit", "Inhibit", "sua{sv}");
        w.WriteString("");              // no parent window
        w.WriteUInt32(InhibitSuspend);
        var d = w.WriteDictionaryStart();
        w.WriteDictionaryEntryStart();
        w.WriteString("reason");
        w.WriteVariantString("Claude is working");
        w.WriteDictionaryEnd(d);
        return w.CreateMessage();
    }

    static MessageBuffer CloseMessage(Connection c, string request)
    {
        using var w = c.GetMessageWriter();
        w.WriteMethodCallHeader("org.freedesktop.portal.Desktop", request, "org.freedesktop.portal.Request", "Close");
        return w.CreateMessage();
    }
}

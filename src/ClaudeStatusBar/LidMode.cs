using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClaudeStatusBar;

/// <summary>Lid-closed mode (Insomnia-style): while Claude works, closing the lid doesn't sleep the PC.
/// Sets the power plan's lid action to "Do nothing" (power API, no admin) and puts the user's own values back
/// when Claude stops. Safety: with the lid shut it hibernates when too hot, or once Claude has stopped working
/// (what Windows would have done at close). The originals are saved to disk BEFORE any change, so a crash is
/// undone at the next launch (hooks relaunch the app on the next Claude event) or by --uninstall.</summary>
public sealed class LidMode : NativeWindow, IDisposable
{
    public enum Act { None, Override, Restore, RestoreAndSleep }
    public const long DoneGraceSec = 30, HeatCheckSec = 10;

    /// <summary>Pure decision. <paramref name="overriding"/> = our "Do nothing" is in place.</summary>
    public static Act Decide(bool want, bool overriding, bool lidClosed, bool hot, long notWorkingFor)
    {
        if (overriding && lidClosed && hot) return Act.RestoreAndSleep;       // heat beats everything
        if (want) return overriding ? Act.None : Act.Override;
        if (!overriding) return Act.None;
        if (!lidClosed) return Act.Restore;
        return notWorkingFor >= DoneGraceSec ? Act.RestoreAndSleep : Act.None; // grace: a brief idle blip isn't "done"
    }

    readonly Action<string> _tip;
    readonly Func<double> _heatLimitC;
    readonly IntPtr _lidNotify;
    bool _lidClosed, _hot, _tried;
    long _notWorkingSince, _lastHeatCheck, _sleptAt;

    public LidMode(Action<string> tip, Func<double> heatLimitC)
    {
        _tip = tip; _heatLimitC = heatLimitC;
        CreateHandle(new CreateParams()); // hidden window: receives the lid open/close notification
        var g = LidSwitchGuid;
        _lidNotify = RegisterPowerSettingNotification(Handle, ref g, 0); // Windows sends the current state right away
    }

    public static bool Overriding => File.Exists(SavedPath);

    /// <summary>Call every poll (UI thread). <paramref name="want"/> = lid mode on AND Claude working.</summary>
    public void Update(bool want, long now)
    {
        if (want) _notWorkingSince = 0; else if (_notWorkingSince == 0) _notWorkingSince = now;
        if (!want) _tried = false;
        bool overriding = Overriding;

        if (overriding && _lidClosed)
        {
            if (now - _lastHeatCheck >= HeatCheckSec)
            {
                _lastHeatCheck = now;
                var c = Thermal.MaxCelsius();
                _hot = c >= _heatLimitC(); // NaN (sensor vanished) compares false; we only override with a sensor
                if (_hot) Log.Write($"lid mode: {c:F0} °C with the lid shut, hibernating");
            }
        }
        else _hot = false;

        switch (Decide(want, overriding, _lidClosed, _hot, _notWorkingSince == 0 ? 0 : now - _notWorkingSince))
        {
            case Act.Override when !_tried: _tried = true; Override(); break;
            case Act.Restore: RestoreSaved(); break;
            case Act.RestoreAndSleep: RestoreSaved(); Sleep(now); break;
        }
    }

    void Override()
    {
        if (double.IsNaN(Thermal.MaxCelsius()))
        {
            _tip("Lid-closed mode is off: this PC doesn't report its temperature, so it can't be kept safe with the lid shut.");
            return;
        }
        if (!ReadLid(out var scheme, out var ac, out var dc)) return;
        if (ac == 0 && dc == 0) return; // your own lid setting is already "Do nothing"
        try { Paths.EnsureDir(); File.WriteAllText(SavedPath, $"{scheme} {ac} {dc}"); }
        catch (Exception e) { Log.Write("lid mode: can't save originals, not overriding: " + e.Message); return; }
        if (WriteLid(scheme, 0, 0)) Log.Write($"lid mode: on (was ac={ac} dc={dc})");
        else RestoreSaved();
    }

    /// <summary>Put the user's lid action back, if we changed it. Safe to call any time (startup, exit, uninstall).</summary>
    public static void RestoreSaved()
    {
        if (!Overriding) return;
        Guid scheme; uint ac = 1, dc = 1; // unreadable file -> Windows' default (Sleep) on the active plan
        try
        {
            var p = File.ReadAllText(SavedPath).Split(' ');
            scheme = Guid.Parse(p[0]); ac = uint.Parse(p[1]); dc = uint.Parse(p[2]);
        }
        catch { if (!ActiveScheme(out scheme)) return; }
        if (WriteLid(scheme, ac, dc)) { try { File.Delete(SavedPath); } catch { } Log.Write("lid mode: off, lid action restored"); }
    }

    // Hibernate: fully off, so a shut laptop cools and keeps every window/session for when it opens.
    void Sleep(long now)
    {
        if (now - _sleptAt < 60) return; // one request per minute, not one per poll while Windows winds down
        _sleptAt = now;
        try
        {
            using var p = Process.Start(new ProcessStartInfo("shutdown.exe", "/h") { CreateNoWindow = true, UseShellExecute = false });
            if (p != null && p.WaitForExit(3000) && p.ExitCode != 0) Application.SetSuspendState(PowerState.Suspend, false, false); // hibernate disabled
        }
        catch (Exception e) { Log.Write("lid mode: sleep failed: " + e.Message); }
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_POWERBROADCAST = 0x218, PBT_POWERSETTINGCHANGE = 0x8013;
        if (m.Msg == WM_POWERBROADCAST && (int)m.WParam == PBT_POWERSETTINGCHANGE &&
            Marshal.PtrToStructure<Guid>(m.LParam) == LidSwitchGuid)
            _lidClosed = Marshal.ReadInt32(m.LParam, 20) == 0; // POWERBROADCAST_SETTING.Data: 0 = closed
        base.WndProc(ref m);
    }

    /// <summary>On exit: with the lid shut there's nothing left to stay awake for, so sleep like Windows would have.</summary>
    public void Dispose()
    {
        bool sleep = Overriding && _lidClosed;
        RestoreSaved();
        if (sleep) Sleep(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (_lidNotify != IntPtr.Zero) UnregisterPowerSettingNotification(_lidNotify);
        DestroyHandle();
    }

    // ---- power plan (no admin needed for the current user's plan) ----
    static string SavedPath => Path.Combine(Paths.StatusbarDir, "lid-restore.txt");
    static readonly Guid LidSwitchGuid = new("BA3E0F4D-B817-4094-A2D1-D56379E6A0F3");
    static Guid SubButtons = new("4f971e89-eebd-4455-a8de-9e59040e7347"), LidAction = new("5ca83367-6e45-459f-a27b-476b1d01c936");

    static bool ActiveScheme(out Guid scheme)
    {
        scheme = Guid.Empty;
        if (PowerGetActiveScheme(IntPtr.Zero, out var p) != 0) return false;
        scheme = Marshal.PtrToStructure<Guid>(p); LocalFree(p);
        return true;
    }

    static bool ReadLid(out Guid scheme, out uint ac, out uint dc)
    {
        ac = dc = 0;
        return ActiveScheme(out scheme)
            && PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubButtons, ref LidAction, out ac) == 0
            && PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SubButtons, ref LidAction, out dc) == 0;
    }

    static bool WriteLid(Guid scheme, uint ac, uint dc)
    {
        bool ok = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SubButtons, ref LidAction, ac) == 0
               && PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SubButtons, ref LidAction, dc) == 0;
        if (ok && ActiveScheme(out var active) && active == scheme) PowerSetActiveScheme(IntPtr.Zero, ref active); // apply now
        if (!ok) Log.Write("lid mode: writing the lid action failed");
        return ok;
    }

    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
    [DllImport("user32.dll")] static extern IntPtr RegisterPowerSettingNotification(IntPtr hwnd, ref Guid setting, int flags);
    [DllImport("user32.dll")] static extern bool UnregisterPowerSettingNotification(IntPtr h);
}

/// <summary>Hottest ACPI thermal zone in °C via the "Thermal Zone Information" performance counter
/// (readable without admin, unlike WMI's MSAcpi_ThermalZoneTemperature). NaN when the PC exposes none.</summary>
public static class Thermal
{
    static IntPtr _query, _counter;

    public static double MaxCelsius()
    {
        try
        {
            if (_query == IntPtr.Zero)
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out var q) != 0) return double.NaN;
                if (PdhAddEnglishCounter(q, @"\Thermal Zone Information(*)\Temperature", IntPtr.Zero, out _counter) != 0) { PdhCloseQuery(q); return double.NaN; }
                _query = q;
            }
            if (PdhCollectQueryData(_query) != 0) return double.NaN;
            uint size = 0;
            if ((uint)PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE, ref size, out _, IntPtr.Zero) != PDH_MORE_DATA) return double.NaN;
            var buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE, ref size, out var n, buf) != 0) return double.NaN;
                double max = double.NaN;
                for (int i = 0; i < n; i++) // PDH_FMT_COUNTERVALUE_ITEM: name ptr, CStatus @8, double @16; 24 bytes (x86 + x64)
                {
                    if (Marshal.ReadInt32(buf, i * 24 + 8) != 0) continue;
                    var kelvin = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(buf, i * 24 + 16));
                    if (kelvin <= 200) continue; // 0 K = a zone with no reading
                    var c = kelvin - 273.15;
                    if (double.IsNaN(max) || c > max) max = c;
                }
                return max;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { return double.NaN; }
    }

    const uint PDH_FMT_DOUBLE = 0x200, PDH_MORE_DATA = 0x800007D2;
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
}

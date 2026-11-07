using System.Diagnostics;

namespace ClaudeStatusBar;

/// <summary>Lid-closed mode (Linux): while Claude works, closing the lid doesn't suspend. Holds a logind inhibitor lock
/// (handle-lid-switch + sleep, block mode; logind allows it for the active local user without root) through a
/// systemd-inhibit child. That child also exits on its own within ~2 s if this app dies, so a crash can never leave
/// the lid disabled — nothing is changed on disk, unlike the Windows power-plan switch.
/// Safety, same rules as Windows: with the lid shut it hibernates (or suspends, if hibernation isn't set up) when the
/// hottest thermal zone reaches the limit, or once Claude has stopped working for <see cref="DoneGraceSec"/>.
/// It refuses to start without a temperature sensor or a readable lid switch.</summary>
public sealed class LidMode : IDisposable
{
    public enum Act { None, Override, Restore, RestoreAndSleep }
    public const long DoneGraceSec = 30, HeatCheckSec = 10;

    /// <summary>Pure decision, identical to the Windows LidMode.Decide (keep the two in step).</summary>
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
    Process? _holder;
    bool _hot, _tried;
    long _notWorkingSince, _lastHeatCheck, _sleptAt;

    public LidMode(Action<string> tip, Func<double> heatLimitC) { _tip = tip; _heatLimitC = heatLimitC; }

    bool Overriding => _holder is { HasExited: false };

    /// <summary>Call every poll. <paramref name="want"/> = lid mode on AND Claude working.</summary>
    public void Update(bool want, long now)
    {
        if (want) _notWorkingSince = 0; else if (_notWorkingSince == 0) _notWorkingSince = now;
        if (!want) _tried = false;
        bool overriding = Overriding;
        bool lidClosed = overriding && LidClosed() == true;

        if (overriding && lidClosed)
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

        switch (Decide(want, overriding, lidClosed, _hot, _notWorkingSince == 0 ? 0 : now - _notWorkingSince))
        {
            case Act.Override when !_tried: _tried = true; Override(); break;
            case Act.Restore: Restore(); break;
            case Act.RestoreAndSleep: Restore(); Sleep(now); break;
        }
    }

    void Override()
    {
        if (double.IsNaN(Thermal.MaxCelsius()))
        {
            _tip("Lid-closed mode is off: this PC doesn't report its temperature, so it can't be kept safe with the lid shut.");
            return;
        }
        if (LidClosed() == null)
        {
            _tip("Lid-closed mode is off: this PC doesn't report its lid switch.");
            return;
        }
        try
        {
            var psi = new ProcessStartInfo("systemd-inhibit")
            {
                UseShellExecute = false, RedirectStandardError = true,
                ArgumentList =
                {
                    "--what=handle-lid-switch:sleep", "--mode=block", "--who=Claude Status Bar",
                    "--why=Claude is working (lid-closed mode)",
                    // the lock's lifetime: until we stop it, or until this app is gone
                    "sh", "-c", $"while kill -0 {Environment.ProcessId} 2>/dev/null; do sleep 2; done",
                },
            };
            _holder = Process.Start(psi);
            if (_holder == null || _holder.WaitForExit(400))
            {
                var err = _holder?.StandardError.ReadToEnd().Trim();
                _holder = null;
                Log.Write("lid mode: systemd-inhibit refused: " + err);
                _tip("Lid-closed mode couldn't take the lid lock (" + (string.IsNullOrEmpty(err) ? "systemd-inhibit failed" : err) + ").");
                return;
            }
            Log.Write("lid mode: on");
        }
        catch (Exception e)
        {
            Log.Write("lid mode: " + e.Message);
            _tip("Lid-closed mode needs systemd (systemd-inhibit wasn't found).");
        }
    }

    void Restore()
    {
        if (_holder == null) return;
        try { if (!_holder.HasExited) { _holder.Kill(entireProcessTree: true); _holder.WaitForExit(2000); } } catch { }
        _holder.Dispose();
        _holder = null;
        Log.Write("lid mode: off, lid lock released");
    }

    // Hibernate (fully off, cools in a bag, keeps every window); suspend if hibernation isn't set up.
    void Sleep(long now)
    {
        if (now - _sleptAt < 60) return; // one request per minute, not one per poll while the system winds down
        _sleptAt = now;
        try
        {
            using var h = Process.Start(new ProcessStartInfo("systemctl", "hibernate") { UseShellExecute = false, RedirectStandardError = true });
            if (h != null && h.WaitForExit(5000) && h.ExitCode != 0)
                Process.Start(new ProcessStartInfo("systemctl", "suspend") { UseShellExecute = false })?.Dispose();
        }
        catch (Exception e) { Log.Write("lid mode: sleep failed: " + e.Message); }
    }

    /// <summary>true/false from the ACPI lid button; null when the machine has none (a desktop, or no ACPI procfs).</summary>
    public static bool? LidClosed()
    {
        try
        {
            var dir = TestRoot + "/proc/acpi/button/lid";
            if (!Directory.Exists(dir)) return null;
            foreach (var d in Directory.GetDirectories(dir))
            {
                var f = Path.Combine(d, "state");
                if (File.Exists(f)) return File.ReadAllText(f).Contains("closed");
            }
        }
        catch { }
        return null;
    }

    // Test seam for linux/test: a fake root for the lid and thermal files (the container has neither).
    internal static string TestRoot => Environment.GetEnvironmentVariable("CLAUDE_STATUSBAR_TEST_ROOT") ?? "";

    /// <summary>On exit: with the lid shut there's nothing left to stay awake for, so sleep like the system would have.</summary>
    public void Dispose()
    {
        bool sleep = Overriding && LidClosed() == true;
        Restore();
        if (sleep) Sleep(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }
}

/// <summary>Hottest thermal zone in °C from /sys/class/thermal (readable without root). NaN when there is none.</summary>
public static class Thermal
{
    public static double MaxCelsius()
    {
        double max = double.NaN;
        try
        {
            foreach (var zone in Directory.GetDirectories(LidMode.TestRoot + "/sys/class/thermal", "thermal_zone*"))
            {
                try
                {
                    if (!double.TryParse(File.ReadAllText(Path.Combine(zone, "temp")).Trim(), out var milli)) continue;
                    var c = milli / 1000.0;
                    if (c <= 0 || c > 150) continue; // unpopulated or bogus zones
                    if (double.IsNaN(max) || c > max) max = c;
                }
                catch { /* some zones refuse reads while their device sleeps */ }
            }
        }
        catch { }
        return max;
    }
}

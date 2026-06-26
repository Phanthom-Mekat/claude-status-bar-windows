using System.Diagnostics;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using System.Windows.Forms;

namespace ClaudeStatusBar;

/// <summary>Owns the tray icon + menu, polls state.json (0.4s), animates, and drives the pill.</summary>
public class TrayController : ApplicationContext
{
    readonly NotifyIcon _tray = new() { Visible = true, Text = "Claude Status Bar" };
    readonly SessionAggregator _agg = new();
    readonly IconRenderer _icons = new();
    readonly AppSettings _cfg = AppSettings.Load();
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 400 };
    readonly System.Windows.Forms.Timer _anim = new() { Interval = 110 };
    readonly ToolStripMenuItem _header = new("Idle") { Enabled = false };
    ContextMenuStrip _menu = null!;
    ToolStripMenuItem _overlayItem = null!;
    ToolStripMenuItem _versionItem = null!;
    bool _updateShown;
    readonly List<ToolStripItem> _sessionRows = new();

    Lifecycle _life = null!;
    OverlayPill? _pill;
    Icon? _shown;
    SynchronizationContext? _sync;
    int _frame;
    long _startedAt;
    string _prevEff = "";
    long _lastTurnStart;
    int _activeCount;

    AnimKind Kind => IconRenderer.ParseStyle(_cfg.AnimStyle);
    int DpiSize { get { using var g = Graphics.FromHwnd(IntPtr.Zero); return Math.Max(16, (int)Math.Round(16 * g.DpiX / 96.0)); } }
    int PillIconSize => DpiSize + 6;

    public TrayController()
    {
        BuildMenu();
        _life = new Lifecycle(_cfg, () => { _tray.Visible = false; ExitThread(); }, ShowTip);
        if (_cfg.ShowOverlay) CreatePill();
        SetTrayIcon(_icons.Resting(Kind, _cfg.IconSystem, DpiSize));

        _poll.Tick += (_, _) => Tick();
        _anim.Tick += (_, _) => AnimStep();
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenClaude(); };
        SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode == PowerModes.Resume) _sync?.Post(_ => Tick(), null); };

        _life.OnStartup();
        _poll.Start();
        Tick();
    }

    // ---- menu ----
    void BuildMenu()
    {
        _menu = new ContextMenuStrip();
        _menu.Items.Add(_header);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Open Claude", null, (_, _) => OpenClaude());

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Section("Options"));
        _menu.Items.Add(Check("Show timer", _cfg.ShowTimer, v => { _cfg.ShowTimer = v; _cfg.Save(); Tick(); }));
        _menu.Items.Add(Check("Play completion sound  (1m+)", _cfg.PlayCompletionSound, v => { _cfg.PlayCompletionSound = v; _cfg.Save(); }));
        _overlayItem = Check("Show text overlay", _cfg.ShowOverlay, v => { _cfg.ShowOverlay = v; _cfg.Save(); ToggleOverlay(); });
        _menu.Items.Add(_overlayItem);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Section("Animation"));
        AddRadio(new[] { ("Claude Spark", "web"), ("Claude Code", "code"), ("Crab Walking", "crab") },
            () => _cfg.AnimStyle, v => { _cfg.AnimStyle = v; _cfg.Save(); _anim.Stop(); _frame = 0; Tick(); });

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Section("Color"));
        AddRadio(new[] { ("Orange", "orange"), ("System", "system") },
            () => _cfg.IconSystem ? "system" : "orange", v => { _cfg.IconSystem = v == "system"; _cfg.Save(); Tick(); });

        _menu.Items.Add(new ToolStripSeparator());
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        _versionItem = new ToolStripMenuItem("Version " + ver) { Enabled = false };
        _menu.Items.Add(_versionItem);
        _menu.Items.Add("Quit", null, (_, _) => { _tray.Visible = false; ExitThread(); });

        _menu.Opening += (_, _) => RebuildSessions(); // refresh the live session list only when the menu opens
        _tray.ContextMenuStrip = _menu;
    }

    // Rebuilds the "Sessions" rows (inserted right after the status header) each time the menu opens.
    void RebuildSessions()
    {
        foreach (var it in _sessionRows) _menu.Items.Remove(it);
        _sessionRows.Clear();

        var sessions = _agg.LiveSessions();
        if (sessions.Count == 0) return;

        int idx = 2; // after [0] header + [1] separator
        void Insert(ToolStripItem it) { _menu.Items.Insert(idx++, it); _sessionRows.Add(it); }

        Insert(new ToolStripMenuItem("Sessions") { Enabled = false });
        int shown = 0;
        foreach (var s in sessions)
        {
            if (shown >= 8) { Insert(new ToolStripMenuItem($"   +{sessions.Count - shown} more") { Enabled = false }); break; }
            var item = new ToolStripMenuItem("   " + FormatSession(s));
            var cwd = s.Cwd;
            if (!string.IsNullOrEmpty(cwd) && Directory.Exists(cwd)) { item.ToolTipText = cwd; item.Click += (_, _) => OpenFolder(cwd); }
            else item.Enabled = false;
            Insert(item);
            shown++;
        }
        Insert(new ToolStripSeparator());
    }

    static string FormatSession(StatusState s)
    {
        string st = s.State switch
        {
            "permission" => "Awaiting permission",
            "tool" => string.IsNullOrEmpty(s.Label) ? "Working" : s.Label,
            "thinking" => "Thinking…",
            "waiting" => "Waiting",
            _ => "idle",
        };
        string proj = string.IsNullOrEmpty(s.Project) ? "—" : s.Project;
        string t = (s.State is "tool" or "thinking") && s.StartedAt > 0 ? "  ·  " + Elapsed(s.StartedAt) : "";
        return $"{proj}  ·  {st}{t}";
    }

    void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("open folder failed: " + ex.Message); }
    }

    static ToolStripMenuItem Section(string title) => new(title) { Enabled = false };

    static ToolStripMenuItem Check(string text, bool on, Action<bool> set)
    {
        var it = new ToolStripMenuItem(text) { Checked = on, CheckOnClick = true };
        it.Click += (_, _) => set(it.Checked);
        return it;
    }

    void AddRadio((string label, string value)[] opts, Func<string> get, Action<string> set)
    {
        var items = new List<ToolStripMenuItem>();
        foreach (var (label, value) in opts)
        {
            var it = new ToolStripMenuItem(label) { Checked = get() == value };
            it.Click += (_, _) => { set(value); foreach (var x in items) x.Checked = (x.Tag as string) == value; };
            it.Tag = value;
            items.Add(it);
            _menu.Items.Add(it);
        }
    }

    void CreatePill()
    {
        _pill = new OverlayPill(_cfg);
        _pill.Clicked = () => _pill!.ShowMenu(_menu);
        _pill.CloseRequested = () => { _overlayItem.Checked = false; _cfg.ShowOverlay = false; _cfg.Save(); _pill?.Hide(); };
        _pill.Show();
    }
    void ToggleOverlay()
    {
        if (_cfg.ShowOverlay) { if (_pill == null) CreatePill(); else _pill.Show(); }
        else _pill?.Hide();
    }

    void OpenClaude()
    {
        try
        {
            var exe = Path.Combine(Paths.Home, "AppData", "Local", "AnthropicClaude", "claude.exe");
            if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Write("open claude failed: " + ex.Message); }
    }

    void ShowTip(string msg) { try { _tray.ShowBalloonTip(9000, "Claude Status Bar", msg, ToolTipIcon.Info); } catch { } }

    // ---- state ----
    void Tick()
    {
        _sync ??= SynchronizationContext.Current;
        _life.CheckLifecycle();
        var (st, active) = _agg.Read();
        _activeCount = active;
        Evaluate(st);
        MaybeShowUpdate();
    }

    void MaybeShowUpdate()
    {
        if (_updateShown) return;
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
        if (!UpdateChecker.UpdateAvailable(_cfg, ver)) return;
        _updateShown = true;
        _versionItem.Text = $"⬆ Update available: v{_cfg.LatestVersion}";
        _versionItem.Enabled = true;
        _versionItem.Click += (_, _) => OpenReleases();
        ShowTip($"Update available: v{_cfg.LatestVersion} — open the tray menu to get it.");
    }

    void OpenReleases()
    {
        try { Process.Start(new ProcessStartInfo($"https://github.com/{UpdateChecker.Repo}/releases/latest") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("open releases failed: " + ex.Message); }
    }

    public void Evaluate(StatusState s)
    {
        var eff = s.State;
        var label = s.Label;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (eff is "thinking" or "tool" or "permission")
        {
            if (now - s.Ts > 900) { eff = "idle"; label = ""; }
            else if (!string.IsNullOrEmpty(s.Transcript) && StateReader.TranscriptInterrupted(s.Transcript)) { eff = "idle"; label = ""; }
        }

        // completion chime: a turn that ran >= 60s transitions to "done"
        if ((eff == "thinking" || eff == "tool") && s.StartedAt > 0) _lastTurnStart = s.StartedAt;
        if (eff == "done" && _prevEff != "done" && _cfg.PlayCompletionSound && _lastTurnStart > 0 && now - _lastTurnStart >= 60)
            System.Media.SystemSounds.Asterisk.Play();
        if (eff == "done") _lastTurnStart = 0;
        _prevEff = eff;

        _startedAt = s.StartedAt;
        bool active = eff is not ("idle" or "done");
        bool animate = eff is "thinking" or "tool";
        if (animate) { if (!_anim.Enabled) { _anim.Interval = _icons.IntervalMs(Kind); _anim.Start(); } }
        else
        {
            _anim.Stop(); _frame = 0;
            SetTrayIcon(eff == "permission" ? _icons.Dot(DpiSize) : _icons.Resting(Kind, _cfg.IconSystem, DpiSize));
            _pill?.SetIcon(eff == "permission" ? _icons.DotBitmap(PillIconSize) : _icons.RestingBitmap(Kind, _cfg.IconSystem, PillIconSize));
        }

        string status = eff switch
        {
            "thinking" => string.IsNullOrEmpty(label) ? "Thinking…" : label,
            "tool" => string.IsNullOrEmpty(label) ? "Working…" : label,
            "permission" => "Awaiting permission",
            "waiting" => string.IsNullOrEmpty(label) ? "Waiting" : label,
            _ => "Idle",
        };

        int sessions = _activeCount; // sessions actually working/awaiting (from the aggregator)
        string suffix = "";
        if (_cfg.ShowTimer && active && _startedAt > 0) suffix += "  " + Elapsed(_startedAt);
        if (active && sessions > 1) suffix += $"  ·  {sessions} active";

        _header.Text = status + suffix;
        var full = status + suffix;
        _tray.Text = full.Length > 63 ? full[..63] : full;

        // Keep the pill short: show the project only when >1 session is active (to disambiguate); no "+N".
        string proj = (active && sessions > 1) ? Short(s.Project, 16) : "";
        _pill?.SetText(active ? status : "", proj, (_cfg.ShowTimer && active) ? _startedAt : 0, eff == "permission");
    }

    static string Elapsed(long startedAt)
    {
        var s = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - startedAt);
        return s >= 60 ? $"{s / 60}m {s % 60}s" : $"{s}s";
    }

    static string Short(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..(max - 1)] + "…";

    void AnimStep()
    {
        _frame = (_frame + 1) % _icons.FrameCount(Kind);
        SetTrayIcon(_icons.Frame(Kind, _frame, _cfg.IconSystem, DpiSize));
        _pill?.SetIcon(_icons.FrameBitmap(Kind, _frame, _cfg.IconSystem, PillIconSize));
    }

    void SetTrayIcon(Icon icon)
    {
        var old = _shown;
        _tray.Icon = icon;
        _shown = icon;
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _poll.Dispose(); _anim.Dispose(); _shown?.Dispose(); _tray.Dispose(); _pill?.Dispose(); }
        base.Dispose(disposing);
    }
}

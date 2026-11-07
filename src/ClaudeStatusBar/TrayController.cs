using System.Diagnostics;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using System.Windows.Forms;

namespace ClaudeStatusBar;

/// <summary>Owns the tray icon + menu, polls the session files (0.4s), animates, and drives the pill.</summary>
public class TrayController : ApplicationContext
{
    readonly NotifyIcon _tray = new() { Visible = true, Text = "Claude Status Bar" };
    readonly SessionAggregator _agg = new();
    readonly IconRenderer _icons = new();
    readonly TurnTracker _turns = new();
    readonly AppSettings _cfg = AppSettings.Load();
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 400 };
    readonly System.Windows.Forms.Timer _anim = new() { Interval = 110 };
    readonly ToolStripMenuItem _header = new("Idle") { Tag = Theme.StatusTag };
    ContextMenuStrip _menu = null!;
    ToolStripMenuItem _versionItem = null!;
    bool _updateShown;
    readonly List<ToolStripItem> _sessionRows = new();
    readonly List<(ToolStripMenuItem item, string sid)> _sessionItemMap = new();
    List<StatusState> _ordered = new();        // effective sessions, busiest-first (refreshed each poll)
    string? _pinnedSid; // when set, the pill/tray follow this session instead of the auto "busiest"

    Lifecycle _life = null!;
    LidMode _lid = null!;
    OverlayPill? _pill;
    Icon? _shown;
    SynchronizationContext? _sync;
    int _frame;
    bool _outro;          // playing Orbit's exit after work stopped
    long _startedAt;
    int _activeCount;
    long _doneUntil, _doneSecs; // the "Done · 3m 12s" moment after a turn finishes
    int _dpiSize;
    string _tip = "";

    AnimKind Kind => IconRenderer.ParseStyle(_cfg.AnimStyle);
    int PillIconSize => _dpiSize + 6;

    public TrayController()
    {
        _dpiSize = ComputeDpiSize();
        ApplyMenuTheme();
        BuildMenu();
        _life = new Lifecycle(_cfg, () => { _tray.Visible = false; ExitThread(); }, ShowTip);
        _lid = new LidMode(ShowTip, () => _cfg.LidHeatLimitC);
        if (_cfg.ShowOverlay) CreatePill();
        SetTrayIcon(_icons.Resting(Kind, _cfg.IconSystem, _dpiSize));

        _poll.Tick += (_, _) => Tick();
        _anim.Tick += (_, _) => AnimStep();
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenClaude(); };
        SystemEvents.PowerModeChanged += OnPower;
        SystemEvents.UserPreferenceChanged += OnUserPref;
        SystemEvents.DisplaySettingsChanged += OnDisplay;

        _life.OnStartup();
        _poll.Start();
        Tick();
    }

    void OnPower(object? s, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) _sync?.Post(_ => Tick(), null); }
    // Light/dark switch: re-tint System-colored icons and re-theme the menu.
    void OnUserPref(object? s, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)) return;
        _sync?.Post(_ => { _icons.ClearCache(); ApplyMenuTheme(); _menu.Invalidate(); Redraw(); }, null);
    }
    void OnDisplay(object? s, EventArgs e) => _sync?.Post(_ => { _dpiSize = ComputeDpiSize(); Redraw(); }, null);

    static int ComputeDpiSize() { using var g = Graphics.FromHwnd(IntPtr.Zero); return Math.Max(16, (int)Math.Round(16 * g.DpiX / 96.0)); }

    // ---- menu ----
    static void ApplyMenuTheme() => ToolStripManager.Renderer = new Theme.MenuRenderer(Theme.Palette.Current());

    void BuildMenu()
    {
        _menu = new ContextMenuStrip { ShowItemToolTips = true, Padding = new Padding(0, 4, 0, 4) };
        _header.Font = BoldFont;
        _header.Enabled = false; // not clickable; the renderer still draws it full-strength (StatusTag)
        _menu.Items.Add(_header);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Open Claude", null, (_, _) => OpenClaude());

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Header("Options"));
        _menu.Items.Add(Check("Show timer", _cfg.ShowTimer, v => { _cfg.ShowTimer = v; _cfg.Save(); Tick(); }));
        _menu.Items.Add(Check("Show text", _cfg.ShowText, v => { _cfg.ShowText = v; _cfg.Save(); Tick(); },
            "Off = an icon-only floating pill"));
        _menu.Items.Add(Check("Show floating pill", _cfg.ShowOverlay, v => { _cfg.ShowOverlay = v; _cfg.Save(); ToggleOverlay(); },
            "An always-on-top status pill above the taskbar (drag to move). Turning it off hides it until the app next starts."));
        _menu.Items.Add(Check("Keep PC awake while working", _cfg.KeepAwake, v => { _cfg.KeepAwake = v; _cfg.Save(); Tick(); },
            "Windows won't go to sleep while Claude is thinking or running a tool (VS Code, CLI or Desktop). " +
            "The screen can still turn off. Closing the lid still sleeps the PC (see below)."));
        _menu.Items.Add(Check("Keep running with lid closed", _cfg.LidMode, v => { _cfg.LidMode = v; _cfg.Save(); Tick(); },
            "While Claude works, closing the lid won't sleep the PC.\n" +
            $"Safety: with the lid shut it hibernates if it reaches {_cfg.LidHeatLimitC:0} °C, " +
            "or 30 s after Claude stops working. Your own lid setting comes back when Claude is idle.\n" +
            "Use it on a desk, not in a closed bag."));
        _menu.Items.Add(Check("Pill mascot", _cfg.ShowMascot, v => { _cfg.ShowMascot = v; _cfg.Save(); Redraw(); },
            "A little companion on the floating pill that acts out what Claude is doing"));

        _menu.Items.Add(Choice("Animation", new[] { ("Spark", "web"), ("Unicode", "code"), ("Clawd™", "crab"), ("Orbit", "mark") },
            () => _cfg.AnimStyle, v =>
            {
                _cfg.AnimStyle = v; _cfg.Save();
                _anim.Stop(); _frame = 0; _outro = false;
                _icons.ClearCache(); // drop the old style's frames
                Redraw();
            }));
        _menu.Items.Add(Choice("Color", new[] { ("Orange", "orange"), ("System", "system") },
            () => _cfg.IconSystem ? "system" : "orange",
            v => { _cfg.IconSystem = v == "system"; _cfg.Save(); _icons.ClearCache(); Redraw(); }));
        _menu.Items.Add(Choice("Completion sound",
            new[] { ("Off", "0"), ("Every turn", "0.1"), ("1 min+", "60"), ("5 min+", "300"), ("15 min+", "900") },
            () => _cfg.SoundThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
            v =>
            {
                _cfg.SoundThreshold = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); _cfg.Save();
                if (_cfg.SoundThreshold > 0) Chime.Play(); // preview
            }));

        _menu.Items.Add(new ToolStripSeparator());
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        _versionItem = new ToolStripMenuItem("Version " + ver) { Enabled = false };
        _menu.Items.Add(_versionItem);
        _menu.Items.Add("Quit", null, (_, _) => Quit());

        _menu.Opening += (_, _) => RebuildSessions(); // refresh the live session list only when the menu opens
        _menu.Opened += (_, _) => Theme.RoundCorners(_menu.Handle);
        _tray.ContextMenuStrip = _menu;
    }

    // Rebuilds the "Sessions" rows (inserted right after the status header) each time the menu opens.
    void RebuildSessions()
    {
        foreach (var it in _sessionRows) { _menu.Items.Remove(it); it.Dispose(); }
        _sessionRows.Clear();
        _sessionItemMap.Clear();

        // Branches otherwise refresh only when a session file changes; re-read on open (one tiny file each)
        // so a checkout or git init made while a session sat idle shows up.
        foreach (var s in _ordered) if (s.Cwd.Length > 0) GitInfo.RecheckNonGit(s.Cwd);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var visible = SessionAggregator.Visible(_ordered, now);
        if (visible.Count == 0) return;
        var names = SessionAggregator.DisplayNames(_ordered);

        int idx = 2; // after [0] header + [1] separator
        void Insert(ToolStripItem it) { _menu.Items.Insert(idx++, it); _sessionRows.Add(it); }

        Insert(Header(_pinnedSid == null ? "Sessions" : "Sessions · following one (Ctrl+click it again for auto)"));
        int shown = 0;
        foreach (var s in visible)
        {
            if (shown >= 8) { Insert(new ToolStripMenuItem($"+{visible.Count - shown} more") { Enabled = false }); break; }
            var sid = s.SessionId;
            var name = names.TryGetValue(sid, out var n) ? n : s.Project;
            var branch = GitInfo.Branch(s.Cwd);
            var item = new ToolStripMenuItem(RowText(s, name, branch))
            {
                ShortcutKeyDisplayString = RowDetail(s),
                ToolTipText = RowTip(s, name, branch) + "\n\nClick to jump to this session" +
                              (_pinnedSid == sid ? "\nCtrl+click to follow the busiest again" : "\nCtrl+click to show it in the status"),
                Image = RowImage(s),
                Tag = Theme.RowTag,
            };
            if (_pinnedSid == sid) item.Font = BoldFont;
            var target = s;
            item.Click += (_, _) =>
            {
                if ((Control.ModifierKeys & Keys.Control) != 0) { _pinnedSid = _pinnedSid == sid ? null : sid; Tick(); }
                else JumpTo(target);
            };
            Insert(item);
            _sessionItemMap.Add((item, sid));
            shown++;
        }
        Insert(new ToolStripSeparator());
    }

    static string Trunc(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..(max - 1)] + "…";

    // "<conversation title>  ·  <project> · <branch>" — the title is what you typed, the project/branch is where.
    static string RowText(StatusState s, string name, string branch)
    {
        var where = Trunc(name, 32) + (branch.Length > 0 ? " · " + Trunc(branch, 22) : "");
        return string.IsNullOrEmpty(s.Name) ? where : Trunc(s.Name, 28) + Theme.RowSplit + where;
    }

    // Right-aligned, dim: the live timer (or "needs approval") plus where it runs.
    static string RowDetail(StatusState s)
    {
        var parts = new List<string>();
        if (s.State is "tool" or "thinking" && s.StartedAt > 0) parts.Add(Elapsed(s.StartedAt));
        else if (s.State == "permission") parts.Add("needs approval");
        var tag = s.Entrypoint switch { "" => "", "claude-desktop" => "APP", _ => "CLI" };
        if (tag.Length > 0) parts.Add(tag);
        return string.Join("  ·  ", parts);
    }

    static string RowTip(StatusState s, string name, string branch)
    {
        var tip = string.IsNullOrEmpty(s.Name) ? name : s.Name + "\n" + name;
        if (branch.Length > 0) tip += " · " + branch;
        if (s.Cwd.Length > 0) tip += "\n" + s.Cwd;
        return tip;
    }

    // Working rows animate with the tray (same cached frames, always brand orange: the menu's theme can differ
    // from the taskbar's that "System" tracks); permission = amber dot; resting = faint dot.
    Bitmap RowImage(StatusState s) => s.State switch
    {
        "tool" or "thinking" => _icons.FrameBitmap(Kind, _frame, false, 16),
        "permission" => _icons.StatusDot(IconRenderer.Amber, 16),
        _ => _icons.StatusDot(Color.FromArgb(140, 140, 140, 148), 16),
    };

    Font? _headerFont, _boldFont;
    Font HeaderFont => _headerFont ??= new Font(_menu.Font.FontFamily, _menu.Font.Size - 0.5f, FontStyle.Bold);
    Font BoldFont => _boldFont ??= new Font(_menu.Font, FontStyle.Bold);

    ToolStripMenuItem Header(string title) => new(title) { Tag = Theme.HeaderTag, Enabled = false, Font = HeaderFont };

    static ToolStripMenuItem Check(string text, bool on, Action<bool> set, string? tip = null)
    {
        var it = new ToolStripMenuItem(text) { Checked = on, CheckOnClick = true, ToolTipText = tip };
        it.Click += (_, _) => set(it.Checked);
        return it;
    }

    // A submenu of mutually exclusive options, the current one checked (and named on the parent, dim).
    static ToolStripMenuItem Choice(string title, (string label, string value)[] opts, Func<string> get, Action<string> set)
    {
        var parent = new ToolStripMenuItem(title);
        void Sync()
        {
            foreach (ToolStripMenuItem x in parent.DropDownItems) x.Checked = (string)x.Tag! == get();
            parent.ShortcutKeyDisplayString = opts.FirstOrDefault(o => o.value == get()).label ?? "";
        }
        foreach (var (label, value) in opts)
        {
            var it = new ToolStripMenuItem(label) { Tag = value };
            it.Click += (_, _) => { set(value); Sync(); };
            parent.DropDownItems.Add(it);
        }
        parent.DropDown.Opened += (_, _) => Theme.RoundCorners(parent.DropDown.Handle);
        Sync();
        return parent;
    }

    void CreatePill()
    {
        _pill = new OverlayPill(_cfg);
        _pill.Clicked = () => _pill!.ShowMenu(_menu);
        _pill.Show();
        Redraw();
    }
    void ToggleOverlay()
    {
        if (_cfg.ShowOverlay) { if (_pill == null) CreatePill(); else { _pill.Show(); Redraw(); } }
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

    // Bring the session's window (editor, terminal, desktop app) to the front — for the VS Code-family extension,
    // its exact Claude tab.
    async void JumpTo(StatusState s)
    {
        try
        {
            if (s.Entrypoint == "claude-vscode" && TrustEditorOnce(s)) await Task.Delay(600); // let the editor reload settings
            if (SessionFocus.Jump(s.Pid, s.Cwd, s.SessionId, s.Entrypoint)) return;
            if (s.Entrypoint == "claude-desktop") { OpenClaude(); return; } // no pid yet: the app itself is the window
            ShowTip($"Couldn't find the window for {(string.IsNullOrEmpty(s.Project) ? "that session" : s.Project)}.");
        }
        catch (Exception e) { Log.Write("jump failed: " + e.Message); }
    }

    // First jump into an editor: skip VS Code's "Allow 'Claude Code' extension to open this URI?" from now on
    // (see EditorTrust). True if settings were just changed.
    bool TrustEditorOnce(StatusState s)
    {
        var scheme = SessionFocus.EditorScheme(ProcessTree.Ancestors(s.Pid).Select(a => a.name));
        if (_cfg.EditorTrustApplied.Contains(scheme)) return false;
        bool changed = false;
        try { changed = EditorTrust.Ensure(EditorTrust.SettingsPath(scheme)); }
        catch (Exception e) { Log.Write("editor trust failed: " + e.Message); }
        _cfg.EditorTrustApplied.Add(scheme);
        _cfg.Save();
        if (changed)
            ShowTip("Clicking a session now opens its exact Claude tab. (VS Code trusts Claude Code's links: " +
                    $"\"{EditorTrust.Key}\" in its settings — remove it there to undo.)");
        return changed;
    }

    // The marker keeps the hooks' self-relaunch from undoing an explicit Quit; cleared on the next
    // SessionStart (lifecycle.js) or the next manual launch (Lifecycle.OnStartup).
    void Quit()
    {
        try { Paths.EnsureDir(); File.WriteAllText(Paths.QuitIntent, ""); } catch { }
        _tray.Visible = false;
        ExitThread();
    }

    void ShowTip(string msg) { try { _tray.ShowBalloonTip(9000, "Claude Status Bar", msg, ToolTipIcon.Info); } catch { } }

    // ---- state ----
    StatusState _lead = StatusState.Idle;

    void Tick()
    {
        _sync ??= SynchronizationContext.Current;
        _life.CheckLifecycle();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var raw = _agg.Load();
        _ordered = SessionAggregator.Order(raw, now);
        bool working = KeepAwake.ShouldHold(_ordered);
        KeepAwake.Apply((_cfg.KeepAwake || _cfg.LidMode) && working); // lid mode needs the idle-sleep hold too
        _lid.Update(_cfg.LidMode && working, now);
        if (_turns.Update(_ordered, _cfg.SoundThreshold, now)) Chime.Play();
        if (_turns.Finished.Count > 0) { _doneSecs = _turns.Finished.Max(f => f.secs); _doneUntil = now + 6; }

        var (st, active) = _agg.Read(raw);
        _activeCount = active;

        // Smart pin: follow the selected session while it's active; if it's idle, fall back to the auto
        // "busiest" so you never stare at "idle" while another session works; snap back when it resumes.
        if (_pinnedSid != null)
        {
            var pinned = _ordered.FirstOrDefault(s => s.SessionId == _pinnedSid);
            if (pinned == null) _pinnedSid = null;                          // pinned session ended -> auto
            else if (pinned.State is not ("idle" or "done")) st = pinned;   // active -> show it; else leave auto-busiest
        }

        _lead = st;
        Evaluate(st);
        MaybeShowUpdate();

        // Keep the open menu's session timers ticking (update in place, no rebuild = no flicker).
        if (_menu.Visible)
            foreach (var (item, sid) in _sessionItemMap)
            {
                var s = _ordered.FirstOrDefault(x => x.SessionId == sid);
                if (s == null) continue;
                var d = RowDetail(s);
                if (item.ShortcutKeyDisplayString != d) item.ShortcutKeyDisplayString = d;
                if (s.State is not ("tool" or "thinking")) item.Image = RowImage(s);
            }
    }

    void Redraw() => Evaluate(_lead, force: true);

    void MaybeShowUpdate()
    {
        if (_updateShown) return;
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
        if (!UpdateChecker.UpdateAvailable(_cfg, ver)) return;
        _updateShown = true;
        _versionItem.Text = $"⬆ Update to v{_cfg.LatestVersion}";
        _versionItem.ToolTipText = "Downloads and installs the update, then restarts the app";
        _versionItem.Enabled = true;
        _versionItem.Click += async (_, _) => await InstallUpdate();
        ShowTip($"Update available: v{_cfg.LatestVersion} — open the tray menu and click it to install.");
    }

    bool _updating;
    async Task InstallUpdate()
    {
        if (_updating) return;
        _updating = true;
        var v = _cfg.LatestVersion;
        _versionItem.Text = $"Updating to v{v}…";
        _versionItem.Enabled = false;
        _poll.Stop(); // no self-quit mid-download
        try
        {
            await Updater.UpdateAsync(v);
            Log.Write("updated to v" + v + ", relaunching");
            Updater.Relaunch();      // the new app waits for us to exit, then takes over
            _tray.Visible = false;
            ExitThread();
        }
        catch (Exception e)
        {
            Log.Write("update failed: " + e);
            _versionItem.Text = $"⬆ Update to v{v}";
            _versionItem.Enabled = true;
            _updating = false;
            _poll.Start();
            ShowTip($"Couldn't update automatically ({e.Message}). Opening the download page instead.");
            OpenReleases();
        }
    }

    void OpenReleases()
    {
        try { Process.Start(new ProcessStartInfo($"https://github.com/{UpdateChecker.Repo}/releases/latest") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("open releases failed: " + ex.Message); }
    }

    string StatusText(StatusState s) => s.State switch
    {
        "thinking" => _turns.Word(s.SessionId) is { Length: > 0 } w ? w + "…" : "Thinking…",
        "tool" => string.IsNullOrEmpty(s.Label) ? "Working…" : s.Label,
        "permission" => "Awaiting permission",
        _ => "Idle",
    };

    /// <summary>Render the (already effective) lead session: icon, animation, tooltip, menu header, pill.</summary>
    public void Evaluate(StatusState s, bool force = false)
    {
        var eff = s.State;
        bool active = eff is "thinking" or "tool" or "permission";
        bool animate = eff is "thinking" or "tool";
        _startedAt = s.StartedAt;

        if (animate)
        {
            if (_outro) { _outro = false; _frame = 0; } // work resumed mid-outro: replay the entry
            if (!_anim.Enabled) { _anim.Interval = _icons.IntervalMs(Kind); _anim.Start(); }
        }
        else if (eff == "permission")
        {
            _anim.Stop(); _outro = false; _frame = 0;
            SetTrayIcon(_icons.Dot(_dpiSize));
            _pill?.SetIcon(_icons.DotBitmap(PillIconSize));
        }
        else if (_anim.Enabled && !_outro && _icons.HasOutro(Kind) && _frame >= _icons.LoopStart(Kind))
        {
            _outro = true; _frame = _icons.FrameCount(Kind); // Orbit: play the exit, AnimStep rests after
        }
        else if (!_outro)
        {
            _anim.Stop(); _frame = 0;
            SetTrayIcon(_icons.Resting(Kind, _cfg.IconSystem, _dpiSize));
            _pill?.SetIcon(_icons.RestingBitmap(Kind, _cfg.IconSystem, PillIconSize));
        }
        // After a cache clear the tray/pill still hold disposed frames: push fresh ones now, not next tick.
        if (force && _anim.Enabled) ShowFrame();

        // "Done · 3m 12s" lingers a few seconds after a turn finishes (unless new work already started).
        bool celebrating = !active && DateTimeOffset.UtcNow.ToUnixTimeSeconds() < _doneUntil;
        string status = celebrating ? "Done · " + Dur(_doneSecs) : StatusText(s);
        string suffix = "";
        if (_cfg.ShowTimer && animate && _startedAt > 0) suffix += "  " + Elapsed(_startedAt);
        if (active && _activeCount > 1) suffix += $"  ·  {_activeCount} active";

        var full = status + suffix;
        if (_header.Text != full) _header.Text = full;
        var tip = full.Length > 63 ? full[..63] : full;
        if (tip != _tip) { _tip = tip; _tray.Text = tip; }

        // Main status (pill) shows only the project when >1 session is active — never the session/tab name
        // (that lives in the right-click Sessions list).
        string proj = (active && _activeCount > 1) ? Trunc(s.Project, 16) : "";
        _pill?.SetMascot(!_cfg.ShowMascot ? null : eff switch
        {
            "permission" => MascotMood.Asking,
            "thinking" => MascotMood.Thinking,
            "tool" => MascotMood.Working,
            _ => celebrating ? MascotMood.Celebrating : MascotMood.Sleeping,
        });
        _pill?.SetText(active || celebrating ? status : "", proj, (_cfg.ShowTimer && animate) ? _startedAt : 0,
            eff == "permission", active || celebrating, _cfg.ShowText);
    }

    static string Elapsed(long startedAt) => Dur(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - startedAt);
    static string Dur(long secs) { var s = Math.Max(0, secs); return s >= 60 ? $"{s / 60}m {s % 60}s" : $"{s}s"; }

    void AnimStep()
    {
        if (_outro)
        {
            _frame += _icons.Step(Kind);
            if (_frame >= _icons.SequenceLength(Kind) - 1)
            {
                _outro = false; _anim.Stop(); _frame = 0;
                SetTrayIcon(_icons.Resting(Kind, _cfg.IconSystem, _dpiSize));
                _pill?.SetIcon(_icons.RestingBitmap(Kind, _cfg.IconSystem, PillIconSize));
                return;
            }
        }
        else _frame = _icons.Next(Kind, _frame);
        ShowFrame();
    }

    void ShowFrame()
    {
        SetTrayIcon(_icons.Frame(Kind, _frame, _cfg.IconSystem, _dpiSize));
        _pill?.SetIcon(_icons.FrameBitmap(Kind, _frame, _cfg.IconSystem, PillIconSize));
        if (_menu.Visible && !_outro)
            foreach (var (item, sid) in _sessionItemMap)
                if (_ordered.FirstOrDefault(x => x.SessionId == sid) is { State: "tool" or "thinking" } s) item.Image = RowImage(s);
    }

    void SetTrayIcon(Icon icon)
    {
        if (ReferenceEquals(_shown, icon)) return; // cached frames: same object = nothing to push to the shell
        _tray.Icon = icon;
        _shown = icon; // owned by the IconRenderer cache — never dispose here
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.PowerModeChanged -= OnPower;
            SystemEvents.UserPreferenceChanged -= OnUserPref;
            SystemEvents.DisplaySettingsChanged -= OnDisplay;
            _poll.Dispose(); _anim.Dispose(); _tray.Dispose(); _pill?.Dispose(); _icons.Dispose();
            _lid?.Dispose(); // puts the user's lid action back
        }
        base.Dispose(disposing);
    }
}

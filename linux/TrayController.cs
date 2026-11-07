using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;

namespace ClaudeStatusBar;

/// <summary>Owns the tray icon (StatusNotifierItem) + menu, polls the session files (0.4s), animates, and drives
/// the pill. Same behavior as the Windows TrayController; the menu is one NativeMenu, exported to the panel over
/// DBus and mirrored into an in-app menu for the pill.</summary>
public sealed class TrayController
{
    const int TraySize = 32, PillIcon = 40, RowIcon = 32;

    readonly IClassicDesktopStyleApplicationLifetime _app;
    readonly TrayIcon _tray = new() { ToolTipText = "Claude Status Bar", IsVisible = true };
    readonly NativeMenu _menu = new();
    readonly NativeMenuItem _header = new("Idle") { IsEnabled = false };
    readonly NativeMenuItem _versionItem;
    readonly List<NativeMenuItemBase> _sessionRows = new();
    readonly SessionAggregator _agg = new();
    readonly IconRenderer _icons = new();
    readonly TurnTracker _turns = new();
    readonly AppSettings _cfg = AppSettings.Load();
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly DispatcherTimer _anim = new();
    readonly Lifecycle _life;
    readonly LidMode _lid;
    readonly PosixSignalRegistration _sigterm; // held: disposing it (or GC) unregisters the handler
    Pill? _pill;
    List<StatusState> _ordered = new();
    string? _pinnedSid;
    StatusState _lead = StatusState.Idle;
    string _rowsSig = "", _tip = "";
    int _frame, _activeCount;
    long _doneUntil, _doneSecs; // the "Done · 3m 12s" moment after a turn finishes
    bool _outro, _updateShown, _updating;
    Bitmap? _shownTray;

    AnimKind Kind => IconRenderer.ParseStyle(_cfg.AnimStyle);
    bool Sys => _cfg.IconSystem;
    static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";

    public TrayController(IClassicDesktopStyleApplicationLifetime app)
    {
        _app = app;
        var a = Application.Current!;
        _icons.SystemIsLight = a.ActualThemeVariant == ThemeVariant.Light;
        a.ActualThemeVariantChanged += (_, _) =>
        {
            _icons.SystemIsLight = a.ActualThemeVariant == ThemeVariant.Light;
            _icons.ClearCache();
            Redraw();
        };

        _versionItem = new NativeMenuItem("Version " + Version[..Version.LastIndexOf('.')]) { IsEnabled = false };
        BuildMenu();
        _tray.Menu = _menu;
        _tray.Clicked += (_, _) => OpenClaude();
        TrayIcon.SetIcons(a, new TrayIcons { _tray });

        _life = new Lifecycle(_cfg, Exit, Notify);
        _lid = new LidMode(Notify, () => _cfg.LidHeatLimitC);
        if (_cfg.ShowOverlay) CreatePill();
        SetTrayIcon(_icons.Resting(Kind, Sys, TraySize));

        _poll.Tick += (_, _) => Tick();
        _anim.Tick += (_, _) => AnimStep();
        // The GUI loop doesn't end on SIGTERM by itself (install.sh/uninstall.sh `kill` us): shut down cleanly,
        // which also unregisters the tray item instead of leaving a ghost icon.
        _sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; Dispatcher.UIThread.Post(Exit); });
        _life.OnStartup();
        _poll.Start();
        Tick();
    }

    // ---- menu ----
    sealed class Cmd(Action run) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => run();
    }

    // DBusMenu and Avalonia menus both read '_' as an access-key marker.
    static string Esc(string s) => s.Replace("_", "__");

    static NativeMenuItem Item(string text, Action run) => new(text) { Command = new Cmd(run) };
    static NativeMenuItem Header(string text) => new(text) { IsEnabled = false };

    static NativeMenuItem Check(string text, Func<bool> get, Action<bool> set)
    {
        var it = new NativeMenuItem(text) { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = get() };
        it.Command = new Cmd(() => { set(!get()); it.IsChecked = get(); });
        return it;
    }

    // A submenu of mutually exclusive options, the current one checked and named on the parent.
    static NativeMenuItem Choice(string title, (string label, string value)[] opts, Func<string> get, Action<string> set)
    {
        var parent = new NativeMenuItem(title) { Menu = new NativeMenu() };
        void Sync()
        {
            foreach (var x in parent.Menu.Items.OfType<NativeMenuItem>()) x.IsChecked = (string)x.CommandParameter! == get();
            parent.Header = $"{title}: {opts.FirstOrDefault(o => o.value == get()).label}";
        }
        foreach (var (label, value) in opts)
            parent.Menu.Items.Add(new NativeMenuItem(label)
            {
                ToggleType = NativeMenuItemToggleType.Radio, CommandParameter = value,
                Command = new Cmd(() => { set(value); Sync(); }),
            });
        Sync();
        return parent;
    }

    void BuildMenu()
    {
        _menu.Items.Add(_header);
        _menu.Items.Add(new NativeMenuItemSeparator());
        _menu.Items.Add(Item("Open Claude", OpenClaude));
        _menu.Items.Add(new NativeMenuItemSeparator());
        _menu.Items.Add(Header("Options"));
        _menu.Items.Add(Check("Show timer", () => _cfg.ShowTimer, v => { _cfg.ShowTimer = v; _cfg.Save(); Tick(); }));
        _menu.Items.Add(Check("Show text", () => _cfg.ShowText, v => { _cfg.ShowText = v; _cfg.Save(); Tick(); }));
        _menu.Items.Add(Check("Show floating pill", () => _cfg.ShowOverlay, v => { _cfg.ShowOverlay = v; _cfg.Save(); ToggleOverlay(); }));
        _menu.Items.Add(Check("Pill mascot", () => _cfg.ShowMascot, v => { _cfg.ShowMascot = v; _cfg.Save(); Redraw(); }));
        _menu.Items.Add(Check("Keep PC awake while working", () => _cfg.KeepAwake, v => { _cfg.KeepAwake = v; _cfg.Save(); Tick(); }));
        _menu.Items.Add(Check("Keep running with lid closed", () => _cfg.LidMode, v => { _cfg.LidMode = v; _cfg.Save(); Tick(); }));
        _menu.Items.Add(Choice("Animation", new[] { ("Spark", "web"), ("Unicode", "code"), ("Clawd™", "crab"), ("Orbit", "mark") },
            () => _cfg.AnimStyle, v =>
            {
                _cfg.AnimStyle = v; _cfg.Save();
                _anim.Stop(); _frame = 0; _outro = false;
                _icons.ClearCache();
                Redraw();
            }));
        _menu.Items.Add(Choice("Color", new[] { ("Orange", "orange"), ("System", "system") },
            () => _cfg.IconSystem ? "system" : "orange",
            v => { _cfg.IconSystem = v == "system"; _cfg.Save(); _icons.ClearCache(); Redraw(); }));
        _menu.Items.Add(Choice("Completion sound",
            new[] { ("Off", "0"), ("Every turn", "0.1"), ("1 min+", "60"), ("5 min+", "300"), ("15 min+", "900") },
            () => _cfg.SoundThreshold.ToString(CultureInfo.InvariantCulture),
            v =>
            {
                _cfg.SoundThreshold = double.Parse(v, CultureInfo.InvariantCulture); _cfg.Save();
                if (_cfg.SoundThreshold > 0) Chime.Play(); // preview
            }));
        _menu.Items.Add(new NativeMenuItemSeparator());
        _menu.Items.Add(_versionItem);
        _menu.Items.Add(Item("Quit", Quit));
    }

    /// <summary>The "Sessions" rows (after the status header). Rebuilt only when what they'd show changes —
    /// at most once a second while a timer ticks — so the panel isn't flooded with DBus layout updates.</summary>
    void SyncSessionRows()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var visible = SessionAggregator.Visible(_ordered, now);
        var names = SessionAggregator.DisplayNames(_ordered);
        var rows = visible.Take(8).Select(s =>
        {
            var name = names.TryGetValue(s.SessionId, out var n) ? n : s.Project;
            var detail = RowDetail(s);
            var text = RowText(s, name, GitInfo.Branch(s.Cwd)) + (detail.Length > 0 ? "   —   " + detail : "");
            return (sid: s.SessionId, text, state: s.State, s, name);
        }).ToList();
        int more = visible.Count - rows.Count;

        var sig = string.Join("\n", rows.Select(r => $"{r.sid}|{r.text}|{r.state}")) + $"|{_pinnedSid}|{more}";
        if (sig == _rowsSig) return;
        _rowsSig = sig;

        foreach (var it in _sessionRows) _menu.Items.Remove(it);
        _sessionRows.Clear();
        if (rows.Count == 0) return;

        int idx = 2; // after [0] header + [1] separator
        void Insert(NativeMenuItemBase it) { _menu.Items.Insert(idx++, it); _sessionRows.Add(it); }
        Insert(Header(_pinnedSid == null ? "Sessions · click to jump to one" : "Sessions · the status follows one"));
        foreach (var r in rows)
        {
            var target = r.s;
            Insert(new NativeMenuItem(Esc(r.text)) { Icon = RowImage(r.state), Command = new Cmd(() => JumpTo(target)) });
        }
        if (more > 0) Insert(Header($"+{more} more"));

        // Windows pins with Ctrl+click, but a DBus menu click carries no modifier keys: pinning gets its own submenu.
        if (rows.Count > 1 || _pinnedSid != null)
        {
            var pinnedName = rows.FirstOrDefault(r => r.sid == _pinnedSid).name;
            var follow = new NativeMenuItem("Status follows: " + (_pinnedSid == null || pinnedName == null ? "busiest" : Esc(Trunc(pinnedName, 24))))
            { Menu = new NativeMenu() };
            follow.Menu.Items.Add(new NativeMenuItem("Busiest session (auto)")
            {
                ToggleType = NativeMenuItemToggleType.Radio, IsChecked = _pinnedSid == null,
                Command = new Cmd(() => { _pinnedSid = null; Tick(); }),
            });
            foreach (var r in rows)
            {
                var sid = r.sid;
                var label = string.IsNullOrEmpty(r.s.Name) ? r.name : $"{Trunc(r.s.Name, 28)} · {Trunc(r.name, 20)}";
                follow.Menu.Items.Add(new NativeMenuItem(Esc(label))
                {
                    ToggleType = NativeMenuItemToggleType.Radio, IsChecked = _pinnedSid == sid,
                    Command = new Cmd(() => { _pinnedSid = sid; Tick(); }),
                });
            }
            Insert(follow);
        }
        Insert(new NativeMenuItemSeparator());
    }

    static string Trunc(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..(max - 1)] + "…";

    // "<conversation title>   <project> · <branch>"
    static string RowText(StatusState s, string name, string branch)
    {
        var where = Trunc(name, 24) + (branch.Length > 0 ? " · " + Trunc(branch, 22) : "");
        return string.IsNullOrEmpty(s.Name) ? where : $"{Trunc(s.Name, 28)}   {where}";
    }

    static string RowDetail(StatusState s)
    {
        var parts = new List<string>();
        if (s.State is "tool" or "thinking" && s.StartedAt > 0) parts.Add(Elapsed(s.StartedAt));
        else if (s.State == "permission") parts.Add("needs approval");
        var tag = s.Entrypoint switch { "" => "", "claude-desktop" => "APP", _ => "CLI" };
        if (tag.Length > 0) parts.Add(tag);
        return string.Join(" · ", parts);
    }

    // Static per state (animating icons over DBus would re-send the whole layout every frame).
    Bitmap RowImage(string state) => state switch
    {
        "tool" or "thinking" => _icons.Resting(AnimKind.Web, false, RowIcon),
        "permission" => _icons.StatusDot(IconRenderer.Amber, RowIcon),
        _ => _icons.StatusDot(new SKColor(140, 140, 148, 140), RowIcon),
    };

    /// <summary>The tray menu as an in-app menu (for the pill): same items, same commands, rebuilt per open so
    /// it's always current.</summary>
    static IEnumerable<Control> Mirror(NativeMenu m) => m.Items.Select<NativeMenuItemBase, Control>(i => i switch
    {
        NativeMenuItemSeparator => new Separator(),
        NativeMenuItem n => new MenuItem
        {
            Header = n.Header,
            IsEnabled = n.IsEnabled,
            Command = n.Command,
            ToggleType = n.ToggleType switch
            {
                NativeMenuItemToggleType.CheckBox => MenuItemToggleType.CheckBox,
                NativeMenuItemToggleType.Radio => MenuItemToggleType.Radio,
                _ => MenuItemToggleType.None,
            },
            IsChecked = n.IsChecked,
            Icon = n.Icon is { } b ? new Image { Source = b, Width = 16, Height = 16 } : null,
            ItemsSource = n.Menu is { } sub ? Mirror(sub).ToList() : null,
        },
        _ => new Separator(),
    });

    void CreatePill()
    {
        _pill = new Pill(_cfg);
        _pill.Clicked = () => _pill!.ShowMenu(new ContextMenu { ItemsSource = Mirror(_menu).ToList() });
        _pill.Show();
        Redraw();
    }

    void ToggleOverlay()
    {
        if (_cfg.ShowOverlay) { if (_pill == null) CreatePill(); else { _pill.Show(); Redraw(); } }
        else _pill?.Hide();
    }

    // Claude Desktop has no official Linux build: launch a community `claude-desktop` if present, else the web app.
    void OpenClaude()
    {
        try { Process.Start(new ProcessStartInfo("claude-desktop") { UseShellExecute = false })?.Dispose(); return; }
        catch { }
        Open("https://claude.ai/code");
    }

    static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { url }, UseShellExecute = false })?.Dispose(); }
        catch (Exception ex) { Log.Write("xdg-open failed: " + ex.Message); }
    }

    static void Notify(string msg)
    {
        try
        {
            Process.Start(new ProcessStartInfo("notify-send")
            {
                ArgumentList = { "-a", "Claude Status Bar", "-i", "dialog-information", "Claude Status Bar", msg },
                UseShellExecute = false,
            })?.Dispose();
        }
        catch { Log.Write("notify: " + msg); }
    }

    // The marker keeps the hooks' self-relaunch from undoing an explicit Quit.
    void Quit()
    {
        try { Paths.EnsureDir(); File.WriteAllText(Paths.QuitIntent, ""); } catch { }
        Exit();
    }

    // Bring the session's window (editor, terminal) to the front; VS Code-family sessions open their exact tab.
    async void JumpTo(StatusState s)
    {
        try
        {
            if (s.Entrypoint == "claude-vscode" && TrustEditorOnce(s)) await Task.Delay(600); // let the editor reload settings
            if (SessionFocus.Jump(s.Pid, s.Cwd, s.SessionId, s.Entrypoint)) return;
            Notify($"Couldn't find the window for {(string.IsNullOrEmpty(s.Project) ? "that session" : s.Project)}" +
                   (Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland" ? " (Wayland only lets apps raise XWayland windows)." : "."));
        }
        catch (Exception e) { Log.Write("jump failed: " + e.Message); }
    }

    // First jump into an editor: skip VS Code's "Allow 'Claude Code' extension to open this URI?" from now on
    // (see EditorTrust; same as Windows). True if settings were just changed.
    bool TrustEditorOnce(StatusState s)
    {
        var scheme = SessionFocus.EditorScheme(ProcessTree.Ancestors(s.Pid).Select(a => a.name));
        if (_cfg.EditorTrustApplied.Contains(scheme)) return false;
        bool changed = false;
        try { changed = EditorTrust.Ensure(EditorTrust.SettingsPath(scheme)); } // ~/.config/<Code|Cursor|…>/User/settings.json
        catch (Exception e) { Log.Write("editor trust failed: " + e.Message); }
        _cfg.EditorTrustApplied.Add(scheme);
        _cfg.Save();
        if (changed)
            Notify("Clicking a session now opens its exact Claude tab. (VS Code trusts Claude Code's links: " +
                   $"\"{EditorTrust.Key}\" in its settings — remove it there to undo.)");
        return changed;
    }

    void Exit()
    {
        _lid.Dispose(); // releases the lid lock (and sleeps if the lid is already shut)
        _poll.Stop(); _anim.Stop();
        _tray.IsVisible = false;
        _tray.Dispose();
        _app.Shutdown();
    }

    // ---- state ----
    void Tick()
    {
        _life.CheckLifecycle();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var raw = _agg.Load();
        _ordered = SessionAggregator.Order(raw, now);
        if (_turns.Update(_ordered, _cfg.SoundThreshold, now)) Chime.Play();
        if (_turns.Finished.Count > 0) { _doneSecs = _turns.Finished.Max(f => f.secs); _doneUntil = now + 6; }
        bool working = KeepAwake.ShouldHold(_ordered);
        KeepAwake.Apply((_cfg.KeepAwake || _cfg.LidMode) && working); // lid mode needs the idle-sleep hold too
        _lid.Update(_cfg.LidMode && working, now);

        var (st, active) = _agg.Read(raw);
        _activeCount = active;

        // Smart pin: follow the chosen session while it's active; when it rests, fall back to the busiest.
        if (_pinnedSid != null)
        {
            var pinned = _ordered.FirstOrDefault(s => s.SessionId == _pinnedSid);
            if (pinned == null) _pinnedSid = null;
            else if (pinned.State is not ("idle" or "done")) st = pinned;
        }

        _lead = st;
        Evaluate(st);
        MaybeShowUpdate();
        SyncSessionRows();
    }

    void Redraw() => Evaluate(_lead, force: true);

    void MaybeShowUpdate()
    {
        if (_updateShown || !UpdateChecker.UpdateAvailable(_cfg, Version)) return;
        _updateShown = true;
        _versionItem.Header = $"⬆ Update to v{_cfg.LatestVersion}";
        _versionItem.IsEnabled = true;
        _versionItem.Command = new Cmd(() => _ = InstallUpdate());
        Notify($"Update available: v{_cfg.LatestVersion} — open the tray menu and click it to install.");
    }

    async Task InstallUpdate()
    {
        if (_updating) return;
        _updating = true;
        var v = _cfg.LatestVersion;
        _versionItem.Header = $"Updating to v{v}…";
        _versionItem.IsEnabled = false;
        _poll.Stop(); // no self-quit mid-download
        try
        {
            await Updater.UpdateAsync(v);
            Log.Write("updated to v" + v + ", relaunching");
            Updater.Relaunch(); // the new app waits for us to exit, then takes over
            Exit();
        }
        catch (Exception e)
        {
            Log.Write("update failed: " + e);
            _versionItem.Header = $"⬆ Update to v{v}";
            _versionItem.IsEnabled = true;
            _updating = false;
            _poll.Start();
            Notify($"Couldn't update automatically ({e.Message}). Opening the download page instead.");
            Open($"https://github.com/{UpdateChecker.Repo}/releases/latest");
        }
    }

    string StatusText(StatusState s) => s.State switch
    {
        "thinking" => _turns.Word(s.SessionId) is { Length: > 0 } w ? w + "…" : "Thinking…",
        "tool" => string.IsNullOrEmpty(s.Label) ? "Working…" : s.Label,
        "permission" => "Awaiting permission",
        _ => "Idle",
    };

    /// <summary>Render the (already effective) lead session: icon, animation, tooltip, menu header, pill.</summary>
    void Evaluate(StatusState s, bool force = false)
    {
        var eff = s.State;
        bool active = eff is "thinking" or "tool" or "permission";
        bool animate = eff is "thinking" or "tool";

        if (animate)
        {
            if (_outro) { _outro = false; _frame = 0; } // work resumed mid-outro: replay the entry
            if (!_anim.IsEnabled) { _anim.Interval = TimeSpan.FromMilliseconds(_icons.IntervalMs(Kind)); _anim.Start(); }
        }
        else if (eff == "permission")
        {
            _anim.Stop(); _outro = false; _frame = 0;
            SetTrayIcon(_icons.Dot(TraySize));
            _pill?.SetIcon(_icons.Dot(PillIcon));
        }
        else if (_anim.IsEnabled && !_outro && _icons.HasOutro(Kind) && _frame >= _icons.LoopStart(Kind))
        {
            _outro = true; _frame = _icons.FrameCount(Kind); // Orbit: play the exit, AnimStep rests after
        }
        else if (!_outro)
        {
            _anim.Stop(); _frame = 0;
            SetTrayIcon(_icons.Resting(Kind, Sys, TraySize));
            _pill?.SetIcon(_icons.Resting(Kind, Sys, PillIcon));
        }
        if (force && _anim.IsEnabled) ShowFrame();

        // "Done · 3m 12s" lingers a few seconds after a turn finishes (unless new work already started).
        bool celebrating = !active && DateTimeOffset.UtcNow.ToUnixTimeSeconds() < _doneUntil;
        string status = celebrating ? "Done · " + Dur(_doneSecs) : StatusText(s);
        string suffix = "";
        if (_cfg.ShowTimer && animate && s.StartedAt > 0) suffix += "  " + Elapsed(s.StartedAt);
        if (active && _activeCount > 1) suffix += $"  ·  {_activeCount} active";
        var full = status + suffix;
        if ((string?)_header.Header != Esc(full)) _header.Header = Esc(full);
        if (full != _tip) { _tip = full; _tray.ToolTipText = full; }

        string proj = (active && _activeCount > 1) ? Trunc(s.Project, 16) : "";
        _pill?.SetMascot(!_cfg.ShowMascot ? null : eff switch
        {
            "permission" => MascotMood.Asking,
            "thinking" => MascotMood.Thinking,
            "tool" => MascotMood.Working,
            _ => celebrating ? MascotMood.Celebrating : MascotMood.Sleeping,
        });
        _pill?.SetText(active || celebrating ? status : "", proj, (_cfg.ShowTimer && animate) ? s.StartedAt : 0,
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
                SetTrayIcon(_icons.Resting(Kind, Sys, TraySize));
                _pill?.SetIcon(_icons.Resting(Kind, Sys, PillIcon));
                return;
            }
        }
        else _frame = _icons.Next(Kind, _frame);
        ShowFrame();
    }

    void ShowFrame()
    {
        SetTrayIcon(_icons.Frame(Kind, _frame, Sys, TraySize));
        _pill?.SetIcon(_icons.Frame(Kind, _frame, Sys, PillIcon));
    }

    void SetTrayIcon(Bitmap b)
    {
        if (ReferenceEquals(_shownTray, b)) return; // cached frames: same object = nothing to send
        _shownTray = b;
        _tray.Icon = _icons.Icon(b);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;

namespace ClaudeStatusBar;

/// <summary>Always-on-top floating pill: live animated icon + status + project + mono timer inside a stadium with
/// a state ring. Over the Windows pill it adds a breathing glow — a soft orange halo while working and a stronger
/// amber pulse while waiting on you — and a smooth hover lift. Drag to move, click (either button) for the menu.</summary>
public sealed class Pill : Window
{
    static readonly Color Brand = Color.FromRgb(0xD9, 0x77, 0x57);
    static readonly Color Amber = Color.FromRgb(0xF2, 0xBA, 0x2E);
    static readonly Color IdleRing = Color.FromArgb(75, 140, 140, 148);
    static readonly IBrush Bg = new SolidColorBrush(Color.FromArgb(240, 24, 24, 27));
    static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(236, 236, 238));
    static readonly IBrush DimBrush = new SolidColorBrush(Color.FromRgb(150, 150, 156));
    const double H = 30, GlowRoom = 12;

    readonly AppSettings _cfg;
    readonly Border _shell;
    readonly Image _icon = new() { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _main = Text(TextBrush), _proj = Text(DimBrush);
    readonly TextBlock _timer = new()
    {
        Foreground = DimBrush, FontSize = 12, FontFamily = new FontFamily("monospace"),
        MinWidth = 54, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
    };
    readonly DispatcherTimer _second = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _glow = new() { Interval = TimeSpan.FromMilliseconds(50) };
    string _label = "", _project = "";
    long _startedAt;
    bool _permission, _active, _showText = true, _hover, _placed;
    double _phase;

    bool _down;
    PointerPressedEventArgs? _press;
    Point _pressAt;

    public Action? Clicked;

    static TextBlock Text(IBrush fg) => new() { Foreground = fg, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };

    public Pill(AppSettings cfg)
    {
        _cfg = cfg;
        Title = "Claude Status";
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        WindowStartupLocation = WindowStartupLocation.Manual;
        RequestedThemeVariant = ThemeVariant.Dark; // the pill's menu matches the pill
        Cursor = new Cursor(StandardCursorType.Hand);

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_icon);
        row.Children.Add(_mascot);
        row.Children.Add(_main);
        row.Children.Add(_proj);
        row.Children.Add(_timer);
        _shell = new Border
        {
            Background = Bg, Height = H, CornerRadius = new CornerRadius(H / 2), BorderThickness = new Thickness(1.3),
            Margin = new Thickness(GlowRoom), Child = row,
        };
        Content = _shell;

        _shell.PointerEntered += (_, _) => { _hover = true; Restyle(); };
        _shell.PointerExited += (_, _) => { if (!_down) { _hover = false; Restyle(); } };
        _shell.PointerPressed += OnPressed;
        _shell.PointerMoved += OnMoved;
        _shell.PointerReleased += OnReleased;
        _second.Tick += (_, _) => { UpdateTimer(); if (_mood == MascotMood.Sleeping) DrawMascot(); }; // the "z" drifts
        _glow.Tick += (_, _) => { _phase += 0.05; Restyle(); };
        _mascotTick.Tick += (_, _) => DrawMascot();
        _second.Start();
        Relayout();
        Place();
        Opened += (_, _) => { Place(); _placed = true; };
        PositionChanged += OnPositionChanged;
    }

    /// <param name="active">working or awaiting permission (drives the ring + glow)</param>
    /// <param name="showText">false = icon-only pill (plus the timer if it's on)</param>
    public void SetText(string label, string project, long startedAt, bool permission, bool active, bool showText)
    {
        if (_label == label && _project == project && _startedAt == startedAt && _permission == permission
            && _active == active && _showText == showText) return;
        _label = label; _project = project; _startedAt = startedAt; _permission = permission;
        _active = active; _showText = showText;
        Relayout();
    }

    public void SetIcon(Bitmap bmp) { if (!ReferenceEquals(_icon.Source, bmp)) _icon.Source = bmp; }

    // ---- Sprout (replaces the icon when on) ----
    // Same proportions as the Windows pill: the lead area is (H-2) tall and 1.3x as wide, room for its "!", "z" and
    // sparkles. Drawn by Skia straight into a WriteableBitmap at device resolution, so it's crisp at any scale.
    const double LeadH = H - 2, LeadW = LeadH * 1.3;
    readonly Image _mascot = new() { Width = LeadW, Height = LeadH, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    readonly DispatcherTimer _mascotTick = new() { Interval = TimeSpan.FromMilliseconds(50) }; // only while the mood moves
    WriteableBitmap? _mascotBmp;
    MascotMood? _mood;

    /// <summary>Show Sprout in <paramref name="mood"/> instead of the icon (null = icon).</summary>
    public void SetMascot(MascotMood? mood)
    {
        if (_mood == mood) return;
        bool swap = (_mood == null) != (mood == null);
        _mood = mood;
        if (mood is { } m && Mascot.Animated(m)) _mascotTick.Start(); else _mascotTick.Stop();
        if (swap) Relayout();
        DrawMascot();
    }

    void DrawMascot()
    {
        if (_mood is not { } mood) return;
        double scale = Math.Max(2, RenderScaling);
        var px = new PixelSize((int)Math.Ceiling(LeadW * scale), (int)Math.Ceiling(LeadH * scale));
        if (_mascotBmp?.PixelSize != px)
        {
            _mascotBmp = new WriteableBitmap(px, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            _mascot.Source = _mascotBmp;
        }
        using (var fb = _mascotBmp.Lock())
        using (var surface = SKSurface.Create(new SKImageInfo(px.Width, px.Height, SKColorType.Bgra8888, SKAlphaType.Premul), fb.Address, fb.RowBytes))
        {
            var c = surface.Canvas;
            c.Clear(SKColors.Transparent);
            c.Scale((float)scale);
            Mascot.Draw(c, SKRect.Create(0, 0, (float)LeadW, (float)LeadH), mood, Environment.TickCount64 / 1000.0);
            c.Flush();
        }
        _mascot.InvalidateVisual();
    }

    string Main => !_showText ? "" : string.IsNullOrEmpty(_label) ? "Claude — idle" : _label;

    string TimerText()
    {
        if (_startedAt <= 0) return "";
        var s = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _startedAt);
        return $"{s / 60}m {s % 60}s";
    }

    void UpdateTimer()
    {
        var t = TimerText();
        if (_timer.Text != t) _timer.Text = t;
        _timer.IsVisible = t.Length > 0;
    }

    void Relayout()
    {
        _main.Text = Main;
        // A long status ("Editing SomeVeryLongFileName.cs") ends in "…" at the Windows widths; hover shows it whole.
        _main.MaxWidth = string.IsNullOrEmpty(_project) ? 210 : 150;
        _main.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(_shell, string.IsNullOrEmpty(_label) ? "Claude — idle" : string.IsNullOrEmpty(_project) ? _label : $"{_label}  ·  {_project}");
        _main.IsVisible = Main.Length > 0;
        _main.Foreground = _permission ? new SolidColorBrush(Amber) : TextBrush;
        _proj.Text = _showText && !string.IsNullOrEmpty(_project) ? "  ·  " + _project : "";
        _proj.IsVisible = _proj.Text.Length > 0;
        UpdateTimer();

        bool iconOnly = !_main.IsVisible && !_proj.IsVisible && !_timer.IsVisible, sprout = _mood != null;
        _icon.IsVisible = !sprout;
        _mascot.IsVisible = sprout;
        _icon.Margin = _mascot.Margin = iconOnly ? new Thickness(0) : new Thickness(0, 0, sprout ? 5 : 7, 0);
        _shell.Width = !iconOnly ? double.NaN : sprout ? LeadW + 12 : H; // icon-only: a circle (Sprout: a short capsule)
        _shell.Padding = iconOnly ? new Thickness(0) : new Thickness(sprout ? 6 : 10, 0, 13, 0);
        ((StackPanel)_shell.Child!).HorizontalAlignment = iconOnly ? HorizontalAlignment.Center : HorizontalAlignment.Left;

        if (_active) { if (!_glow.IsEnabled) { _phase = 0; _glow.Start(); } }
        else _glow.Stop();
        Restyle();
    }

    /// <summary>Ring + glow + hover lift. Glow breathes on a sine: slow and soft while working, faster and
    /// brighter while waiting on a permission (the state that needs you).</summary>
    void Restyle()
    {
        var ring = _permission ? Amber : _active ? Brand : IdleRing;
        byte ringA = _hover ? (byte)255 : ring.A == 255 ? (byte)190 : ring.A;
        _shell.BorderBrush = new SolidColorBrush(Color.FromArgb(ringA, ring.R, ring.G, ring.B));
        _shell.BorderThickness = new Thickness(_hover ? 1.6 : 1.3);
        _shell.Opacity = _hover ? 1.0 : 0.94;

        if (!_active) { _shell.BoxShadow = default; return; }
        var (c, period, lo, hi) = _permission ? (Amber, 1.4, 0.30, 0.85) : (Brand, 2.6, 0.10, 0.38);
        double a = lo + (hi - lo) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * _phase / period));
        if (_hover) a = Math.Min(1, a + 0.15);
        _shell.BoxShadow = new BoxShadows(new BoxShadow
        {
            Blur = _permission ? 14 : 12, Spread = 0.5,
            Color = Color.FromArgb((byte)(a * 255), c.R, c.G, c.B),
        });
    }

    // ---- placement: restore, else bottom-right; grow away from the nearest screen edge ----
    // X11 reports a moved window's Position asynchronously, so we track where we put it (_pos) instead of
    // reading Position back: reading it right after a move returns the stale value and the pill drifts.
    PixelPoint _pos;
    int? _rightEdge; // set while parked on the right half: growth extends leftwards, never off-screen

    int WidthPx(Size s) => (int)Math.Round(s.Width * DesktopScaling);

    void MoveTo(PixelPoint p, Size size)
    {
        _pos = p;
        if (Position != p) Position = p;
        var scr = Screens.ScreenFromPoint(p) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
        int w = WidthPx(size);
        _rightEdge = scr != null && p.X + w / 2 > scr.WorkingArea.X + scr.WorkingArea.Width / 2 ? p.X + w : null;
    }

    // Set before Show (so the WM maps it there) and again once open (some WMs place new windows themselves).
    void Place()
    {
        var size = IsVisible ? ClientSize : MeasureSelf();
        var p = new PixelPoint(_cfg.OverlayX, _cfg.OverlayY);
        if (_cfg.OverlayX >= 0 && Screens.All.Any(s => s.Bounds.Contains(p))) MoveTo(p, size);
        else if ((Screens.Primary ?? Screens.All.FirstOrDefault()) is { } scr) // no "primary" on some X setups
        {
            var sz = PixelSize.FromSize(size, DesktopScaling);
            MoveTo(new PixelPoint(scr.WorkingArea.Right - sz.Width - 4, scr.WorkingArea.Bottom - sz.Height - 4), size);
        }
        else Log.Write("pill: no screens reported; leaving placement to the window manager");
    }

    Size MeasureSelf() { _shell.Measure(Size.Infinity); return _shell.DesiredSize; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ClientSizeProperty || !_placed || _down || _rightEdge is not int right) return;
        _pos = new PixelPoint(right - WidthPx(change.GetNewValue<Size>()), _pos.Y);
        Position = _pos;
    }

    // ---- drag vs click ----
    // Past a 3px threshold the window manager runs the drag (smooth on X11/XWayland, no feedback from stale
    // coordinates); where it lands arrives as a PositionChanged we didn't cause, which we remember and save.
    void OnPressed(object? s, PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        if (pt.Properties.IsRightButtonPressed) { Clicked?.Invoke(); return; }
        if (!pt.Properties.IsLeftButtonPressed) return;
        _down = true;
        _press = e;
        _pressAt = e.GetPosition(this);
    }

    void OnMoved(object? s, PointerEventArgs e)
    {
        if (!_down || _press == null) return;
        var d = e.GetPosition(this) - _pressAt;
        if (Math.Abs(d.X) <= 3 && Math.Abs(d.Y) <= 3) return;
        _down = false;
        BeginMoveDrag(_press);
    }

    void OnReleased(object? s, PointerReleasedEventArgs e)
    {
        if (!_down) return;
        _down = false;
        Clicked?.Invoke();
    }

    void OnPositionChanged(object? s, PixelPointEventArgs e)
    {
        if (!_placed || e.Point == _pos) return; // our own move echoing back
        MoveTo(e.Point, ClientSize);             // re-anchor to whichever half it landed on
        _cfg.OverlayX = e.Point.X; _cfg.OverlayY = e.Point.Y; _cfg.Save();
    }

    /// <summary>Show a menu at the pointer. Activates the pill first so the menu light-dismisses on an outside
    /// click (a never-activated owner can leave it stuck open).</summary>
    public void ShowMenu(ContextMenu menu)
    {
        Activate();
        menu.Open(_shell);
    }
}

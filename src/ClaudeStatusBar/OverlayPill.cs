using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClaudeStatusBar;

/// <summary>Always-on-top, owner-drawn "pill" showing the live animated icon + status. No-activate tool
/// window that re-asserts top-most every second so it never hides. Drag to move, click for the menu,
/// click the × to dismiss.</summary>
public class OverlayPill : Form
{
    static readonly Color Bg = Color.FromArgb(24, 24, 27);
    static readonly Color Brand = Color.FromArgb(0xD9, 0x77, 0x57);
    static readonly Color Amber = Color.FromArgb(0xF2, 0xBA, 0x2E);
    static readonly Color TextCol = Color.FromArgb(236, 236, 238);
    static readonly Color DimCol = Color.FromArgb(150, 150, 156);

    readonly AppSettings _cfg;
    readonly Font _font = new("Segoe UI", 9.5f, FontStyle.Regular);
    readonly Font _mono = MonoFont(8.75f);   // terminal-style face for the timer (tabular digits → no per-second jitter)
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    string _label = "", _project = "";
    long _startedAt;
    bool _permission;
    bool _hover;
    Bitmap? _icon;

    Point _dragStartScreen, _formStartLoc;
    bool _down, _moved;

    /// <summary>Click on the pill body (not a drag, not the ×).</summary>
    public Action? Clicked;
    /// <summary>Click on the × — dismiss the overlay.</summary>
    public Action? CloseRequested;

    int IconSz => _font.Height + 4;
    Rectangle CloseRect => new(Width - 22, 0, 22, Height);

    public OverlayPill(AppSettings cfg)
    {
        _cfg = cfg;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        BackColor = Bg;
        Opacity = 0.92;
        AccessibleName = "Claude status";
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.ResizeRedraw, true);

        MouseDown += OnDown;
        MouseMove += OnMoveMouse;
        MouseUp += OnUp;
        _tick.Tick += (_, _) => { ReassertTopmost(); Render(); };
        _tick.Start();

        Render();
        RestorePosition();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080 | 0x08000000 | 0x00000008; // TOOLWINDOW | NOACTIVATE | TOPMOST
            return cp;
        }
    }
    protected override bool ShowWithoutActivation => true;

    public void SetText(string label, string project, long startedAt, bool permission)
    {
        _label = label; _project = project; _startedAt = startedAt; _permission = permission;
        Render();
    }

    /// <summary>Set the live icon shown on the left (caller hands over ownership; we dispose the previous).</summary>
    public void SetIcon(Bitmap bmp)
    {
        var old = _icon; _icon = bmp; old?.Dispose();
        Invalidate();
    }

    string Main => string.IsNullOrEmpty(_label) ? "Claude — idle" : _label;
    string TimerText()
    {
        if (_startedAt <= 0) return "";
        var s = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _startedAt);
        return $"{s / 60}m {s % 60}s";
    }

    void Render()
    {
        int mainW = TextRenderer.MeasureText(Main, _font, Size.Empty, TextFormatFlags.NoPadding).Width;
        int projW = string.IsNullOrEmpty(_project) ? 0 : TextRenderer.MeasureText("  ·  " + _project, _font, Size.Empty, TextFormatFlags.NoPadding).Width;
        var t = TimerText();
        // Reserve a fixed mono slot (≥ "00m 00s") so ticking digits never resize/reflow the pill.
        int timerW = t.Length == 0 ? 0 : Math.Max(
            TextRenderer.MeasureText("  " + t, _mono, Size.Empty, TextFormatFlags.NoPadding).Width,
            TextRenderer.MeasureText("  00m 00s", _mono, Size.Empty, TextFormatFlags.NoPadding).Width);
        int w = 10 + IconSz + 6 + mainW + projW + timerW + 6 + 22;
        ClientSize = new Size(Math.Max(w, 70), Math.Max(_font.Height + 10, 26));
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // Shape the window to the FULL-height stadium so the visible pill == the client area (was capped at
        // 24px and top-anchored, which clipped the bottom edge and pushed text low). Uses the same helper as
        // the ring so the two stay concentric.
        using var path = Stadium(new Rectangle(0, 0, Width, Height));
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Bg);

        int y = (Height - _font.Height) / 2;
        int x = 10;

        // live icon (or fallback spark glyph)
        if (_icon != null) g.DrawImage(_icon, x, (Height - IconSz) / 2, IconSz, IconSz);
        else TextRenderer.DrawText(g, "✻", _font, new Point(x, y), _permission ? Amber : Brand, TextFormatFlags.NoPadding);
        x += IconSz + 6;

        var mainColor = _permission ? Amber : TextCol;
        TextRenderer.DrawText(g, Main, _font, new Point(x, y), mainColor, TextFormatFlags.NoPadding);
        x += TextRenderer.MeasureText(Main, _font, Size.Empty, TextFormatFlags.NoPadding).Width;

        if (!string.IsNullOrEmpty(_project))
        {
            var proj = "  ·  " + _project;
            TextRenderer.DrawText(g, proj, _font, new Point(x, y), DimCol, TextFormatFlags.NoPadding);
            x += TextRenderer.MeasureText(proj, _font, Size.Empty, TextFormatFlags.NoPadding).Width;
        }
        var t = TimerText();
        if (t.Length > 0)
        {
            int my = (Height - _mono.Height) / 2; // mono metrics differ slightly — recentre vertically
            TextRenderer.DrawText(g, "  " + t, _mono, new Point(x, my), DimCol, TextFormatFlags.NoPadding);
        }

        // close × — only on hover, and brighter than the dim meta so it reads as actionable
        if (_hover)
            TextRenderer.DrawText(g, "×", _font, new Point(Width - 18, y), TextCol, TextFormatFlags.NoPadding);

        // state ring: orange = working, amber = permission, faint = idle. Brightens on hover (the lift cue).
        DrawStateRing(g);
    }

    void DrawStateRing(Graphics g)
    {
        var c = RingColor();
        int a = _hover ? 255 : (c.A == 255 ? 190 : c.A);
        using var pen = new Pen(Color.FromArgb(a, c.R, c.G, c.B), _hover ? 1.6f : 1.3f);
        // 1px inset all round so the full stroke (incl. the bottom) lands inside the window region.
        using var path = Stadium(new Rectangle(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2)));
        g.DrawPath(pen, path);
    }

    Color RingColor()
    {
        if (_permission) return Amber;
        if (!string.IsNullOrEmpty(_label)) return Brand;   // anything non-idle has a label
        return Color.FromArgb(75, 140, 140, 148);          // idle: faint definition only
    }

    static GraphicsPath Stadium(Rectangle r)
    {
        var p = new GraphicsPath();
        int d = Math.Min(r.Height, r.Width); // full-height round caps = a true stadium (not capped at 24)
        p.AddArc(r.X, r.Y, d, d, 90, 180);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        p.CloseFigure();
        return p;
    }

    // Prefer Cascadia Mono (ships with Win11 / Windows Terminal), then Cascadia Code, then Consolas (always present).
    static Font MonoFont(float size)
    {
        foreach (var name in new[] { "Cascadia Mono", "Cascadia Code", "Consolas" })
        {
            try { new FontFamily(name).Dispose(); return new Font(name, size, FontStyle.Regular); }
            catch (ArgumentException) { /* family not installed — try the next */ }
        }
        return new Font(FontFamily.GenericMonospace, size);
    }

    void RestorePosition()
    {
        // Validate against full screen Bounds (NOT WorkingArea) so a position the user dragged ONTO
        // the taskbar is kept instead of being bounced back above it on every relaunch.
        var p = new Point(_cfg.OverlayX, _cfg.OverlayY);
        if (_cfg.OverlayX >= 0 && Screen.AllScreens.Any(s => s.Bounds.Contains(p))) { Location = p; return; }

        // Default: snug just ABOVE the taskbar, bottom-right — always visible. (A floating window can't
        // reliably sit ON the taskbar; the taskbar is always-on-top and would cover it.)
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Right - Width - 12, wa.Bottom - Height);
    }

    void OnDown(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _down = true; _moved = false;
        _dragStartScreen = Control.MousePosition; _formStartLoc = Location;
    }
    void OnMoveMouse(object? s, MouseEventArgs e)
    {
        if (!_down) return;
        var d = Control.MousePosition;
        int dx = d.X - _dragStartScreen.X, dy = d.Y - _dragStartScreen.Y;
        if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3) _moved = true;
        if (_moved) Location = new Point(_formStartLoc.X + dx, _formStartLoc.Y + dy);
    }
    void OnUp(object? s, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { Clicked?.Invoke(); _down = false; return; }
        if (_down && !_moved && e.Button == MouseButtons.Left)
        {
            if (CloseRect.Contains(e.Location)) CloseRequested?.Invoke();
            else Clicked?.Invoke();
        }
        _down = false;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true; Opacity = 1.0; Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_down) return;                       // stay lifted while dragging
        _hover = false; Opacity = 0.92; Invalidate();
    }

    /// <summary>Show a context menu anchored at the cursor. Foregrounds this window first so the menu
    /// dismisses when you click elsewhere (a no-activate owner otherwise leaves it stuck open).</summary>
    public void ShowMenu(ContextMenuStrip menu)
    {
        SetForegroundWindow(Handle);
        menu.Show(Control.MousePosition);
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        if (_cfg.OverlayX == Location.X && _cfg.OverlayY == Location.Y) return;
        _cfg.OverlayX = Location.X; _cfg.OverlayY = Location.Y; _cfg.Save();
    }

    void ReassertTopmost()
    {
        if (!Visible || !IsHandleCreated) return;
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _mono.Dispose(); _font.Dispose(); _tick.Dispose(); _icon?.Dispose(); }
        base.Dispose(disposing);
    }
}

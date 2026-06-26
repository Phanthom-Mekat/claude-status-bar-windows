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
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    string _label = "", _project = "";
    long _startedAt;
    bool _permission;
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
        int timerW = t.Length == 0 ? 0 : TextRenderer.MeasureText("  " + t, _font, Size.Empty, TextFormatFlags.NoPadding).Width;
        int w = 10 + IconSz + 6 + mainW + projW + timerW + 6 + 22;
        ClientSize = new Size(Math.Max(w, 70), Math.Max(_font.Height + 10, 26));
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var path = new GraphicsPath();
        int d = Math.Min(Height, 24);
        path.AddArc(0, 0, d, d, 90, 180);
        path.AddArc(Width - d, 0, d, d, 270, 180);
        path.CloseFigure();
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
            TextRenderer.DrawText(g, "  " + t, _font, new Point(x, y), DimCol, TextFormatFlags.NoPadding);

        // close ×
        TextRenderer.DrawText(g, "×", _font, new Point(Width - 18, y), DimCol, TextFormatFlags.NoPadding);
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
}

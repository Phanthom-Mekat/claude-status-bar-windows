using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClaudeStatusBar;

/// <summary>Windows light/dark detection + a Windows 11-style menu renderer (follows the app theme, rounded
/// highlight, brand-colored checks, dim headers and right-aligned detail text, DWM rounded corners).</summary>
public static class Theme
{
    const string Personalize = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Taskbar/tray theme (what the tray icon sits on).</summary>
    public static bool TaskbarIsLight() => ReadFlag("SystemUsesLightTheme");
    /// <summary>App theme (what menus should follow).</summary>
    public static bool AppsAreLight() => ReadFlag("AppsUseLightTheme");

    static bool ReadFlag(string name)
    {
        try { return Registry.GetValue(Personalize, name, 0) is int i && i == 1; }
        catch { return false; }
    }

    /// <summary>Ask DWM for Windows 11 rounded corners on a popup (no-op on Windows 10).</summary>
    public static void RoundCorners(IntPtr hwnd)
    {
        try { int round = 2; DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int)); }
        catch { }
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public sealed class Palette
    {
        public Color Bg, Border, Hover, Text, Dim, Separator;
        public static Palette Current() => AppsAreLight()
            ? new Palette { Bg = Color.FromArgb(249, 249, 249), Border = Color.FromArgb(222, 222, 222), Hover = Color.FromArgb(234, 234, 234),
                            Text = Color.FromArgb(26, 26, 26), Dim = Color.FromArgb(112, 112, 112), Separator = Color.FromArgb(229, 229, 229) }
            : new Palette { Bg = Color.FromArgb(44, 44, 44), Border = Color.FromArgb(62, 62, 62), Hover = Color.FromArgb(61, 61, 61),
                            Text = Color.FromArgb(242, 242, 242), Dim = Color.FromArgb(158, 158, 158), Separator = Color.FromArgb(64, 64, 64) };
    }

    /// <summary>Marks a menu item as a section header (small, dim, not clickable).</summary>
    public const string HeaderTag = "header";
    /// <summary>Marks the live status line: not clickable, but drawn at full strength.</summary>
    public const string StatusTag = "status";
    /// <summary>Marks a session row; its text after <see cref="RowSplit"/> (project · branch) is drawn dim.</summary>
    public const string RowTag = "row";
    public const string RowSplit = "   ";

    public sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public readonly Palette P;
        public MenuRenderer(Palette p) : base(new Colors(p)) { P = p; RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var b = new SolidBrush(P.Bg);
            e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(P.Border);
            var r = e.AffectedBounds; r.Width -= 1; r.Height -= 1;
            e.Graphics.DrawRectangle(pen, r);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { /* flat: no grey gutter */ }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            using var path = Rounded(r, 4);
            using var b = new SolidBrush(P.Hover);
            g.FillPath(b, path);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var pen = new Pen(P.Separator);
            e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }

        // Drawn by hand: the base renderer forces GrayText on disabled items, which would dim the status line.
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            bool header = Equals(e.Item.Tag, HeaderTag);
            bool detail = e.Item is ToolStripMenuItem mi && !string.IsNullOrEmpty(mi.ShortcutKeyDisplayString)
                          && e.Text == mi.ShortcutKeyDisplayString;
            var color = Equals(e.Item.Tag, StatusTag) ? P.Text
                      : header || detail || !e.Item.Enabled ? P.Dim : P.Text;
            var flags = e.TextFormat | TextFormatFlags.NoPrefix;
            var text = e.Text ?? "";

            // Session rows: "title<RowSplit>where" -> title in full color, where dim.
            int cut = !detail && Equals(e.Item.Tag, RowTag) ? text.IndexOf(RowSplit, StringComparison.Ordinal) : -1;
            if (cut > 0)
            {
                var head = text[..cut];
                int w = TextRenderer.MeasureText(e.Graphics, head, e.TextFont, Size.Empty, flags).Width;
                TextRenderer.DrawText(e.Graphics, head, e.TextFont, e.TextRectangle, color, flags);
                var rest = new Rectangle(e.TextRectangle.X + w, e.TextRectangle.Y, Math.Max(0, e.TextRectangle.Width - w), e.TextRectangle.Height);
                TextRenderer.DrawText(e.Graphics, text[cut..], e.TextFont, rest, P.Dim, flags);
                return;
            }
            TextRenderer.DrawText(e.Graphics, text, e.TextFont, e.TextRectangle, color, flags);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            using var pen = new Pen(IconRenderer.Brand, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            float x = r.X + r.Width * 0.18f, y = r.Y + r.Height * 0.52f;
            g.DrawLines(pen, new[] { new PointF(x, y), new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.76f), new PointF(r.Right - r.Width * 0.14f, r.Y + r.Height * 0.26f) });
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = P.Dim;
            base.OnRenderArrow(e);
        }

        static GraphicsPath Rounded(Rectangle r, int rad)
        {
            var p = new GraphicsPath(); int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        sealed class Colors : ProfessionalColorTable
        {
            readonly Palette _p;
            public Colors(Palette p) { _p = p; UseSystemColors = false; }
            public override Color ToolStripDropDownBackground => _p.Bg;
            public override Color MenuBorder => _p.Border;
            public override Color MenuItemBorder => Color.Transparent;
            public override Color MenuItemSelected => _p.Hover;
            public override Color ImageMarginGradientBegin => _p.Bg;
            public override Color ImageMarginGradientMiddle => _p.Bg;
            public override Color ImageMarginGradientEnd => _p.Bg;
            public override Color SeparatorDark => _p.Separator;
            public override Color SeparatorLight => _p.Separator;
            public override Color CheckBackground => Color.Transparent;
            public override Color CheckSelectedBackground => Color.Transparent;
            public override Color CheckPressedBackground => Color.Transparent;
        }
    }
}

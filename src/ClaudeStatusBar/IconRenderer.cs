using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ClaudeStatusBar;

public enum AnimKind { Web, Code, Crab }

/// <summary>Builds frames for each animation style as Bitmaps (for the pill) and Icons (for the tray):
/// Spark (tinted PNG frames), Claude Code (glyph spinner), Crab Walking (full-colour pixel frames).</summary>
public class IconRenderer
{
    static readonly Color Brand = Color.FromArgb(0xD9, 0x77, 0x57);
    static readonly Color Amber = Color.FromArgb(0xF2, 0xBA, 0x2E);

    static readonly string[] CodeGlyphs = { "✻", "✽", "✶", "✳", "✢" };
    const int CodeSub = 6;

    readonly List<Bitmap> _spark = new();
    readonly List<Bitmap> _crab = new();
    Bitmap? _logo;

    public IconRenderer()
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var n in asm.GetManifestResourceNames().Where(n => n.Contains("spark-")).OrderBy(n => n)) _spark.Add(Load(asm, n));
        foreach (var n in asm.GetManifestResourceNames().Where(n => n.Contains("crab-")).OrderBy(n => n)) _crab.Add(Load(asm, n));
        var logo = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("logo.png"));
        if (logo != null) _logo = Load(asm, logo);
    }

    static Bitmap Load(Assembly asm, string name)
    {
        using var s = asm.GetManifestResourceStream(name)!;
        using var tmp = new Bitmap(s);
        return new Bitmap(tmp);
    }

    public static AnimKind ParseStyle(string s) => s switch { "code" => AnimKind.Code, "crab" => AnimKind.Crab, _ => AnimKind.Web };

    public int FrameCount(AnimKind k) => k switch
    {
        AnimKind.Crab => Math.Max(1, _crab.Count),
        AnimKind.Code => CodeGlyphs.Length * CodeSub,
        _ => Math.Max(1, _spark.Count),
    };

    public int IntervalMs(AnimKind k) => k switch { AnimKind.Crab => 80, AnimKind.Code => 120, _ => 110 };

    // ---- public: Icons for the tray, Bitmaps for the pill ----
    public Icon Frame(AnimKind k, int i, bool system, int size) { using var b = BuildFrame(k, i, system, size); return ToIcon(b); }
    public Bitmap FrameBitmap(AnimKind k, int i, bool system, int size) => BuildFrame(k, i, system, size);
    public Icon Resting(AnimKind k, bool system, int size) { using var b = BuildResting(k, system, size); return ToIcon(b); }
    public Bitmap RestingBitmap(AnimKind k, bool system, int size) => BuildResting(k, system, size);
    public Icon Dot(int size) { using var b = BuildDot(size); return ToIcon(b); }
    public Bitmap DotBitmap(int size) => BuildDot(size);

    // ---- builders (return a fresh Bitmap the caller owns) ----
    Bitmap BuildFrame(AnimKind k, int i, bool system, int size) => k switch
    {
        AnimKind.Crab => BuildCrab(i, size),
        AnimKind.Code => BuildCode(i, system, size),
        _ => BuildTint(_spark.Count > 0 ? _spark[i % _spark.Count] : _logo, system, size),
    };

    Bitmap BuildResting(AnimKind k, bool system, int size) =>
        k == AnimKind.Crab ? BuildCrab(0, size) : BuildTint(_logo ?? _spark.FirstOrDefault(), system, size);

    Bitmap BuildDot(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int d = (int)(size * 0.5), o = (size - d) / 2;
        using var br = new SolidBrush(Amber);
        g.FillEllipse(br, o, o, d, d);
        return bmp;
    }

    Bitmap BuildCrab(int i, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        if (_crab.Count > 0)
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor; // pixel-art stays crisp
            var src = _crab[i % _crab.Count];
            float ar = (float)src.Width / src.Height;
            int w = ar >= 1 ? size : (int)(size * ar), h = ar >= 1 ? (int)(size / ar) : size;
            g.DrawImage(src, (size - w) / 2, (size - h) / 2, w, h);
        }
        return bmp;
    }

    Bitmap BuildCode(int i, bool system, int size)
    {
        var glyph = CodeGlyphs[(i / CodeSub) % CodeGlyphs.Length];
        float local = ((i % CodeSub) + 0.5f) / CodeSub;
        float env = local < 0.3f ? Smooth(local / 0.3f) : local > 0.7f ? Smooth((1 - local) / 0.3f) : 1f;
        float scale = 0.5f + 0.5f * env;
        var color = Resolve(system);
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe UI Symbol", Math.Max(4f, size * scale * 0.9f), GraphicsUnit.Pixel);
        var sz = g.MeasureString(glyph, font);
        using var br = new SolidBrush(color);
        g.DrawString(glyph, font, br, (size - sz.Width) / 2f, (size - sz.Height) / 2f);
        return bmp;
    }

    static float Smooth(float u) => u * u * (3 - 2 * u);

    Bitmap BuildTint(Bitmap? src, bool system, int size)
    {
        var color = Resolve(system);
        var outBmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        if (src == null) return outBmp;
        using var scaled = new Bitmap(size, size);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, new Rectangle(0, 0, size, size));
        }
        var rect = new Rectangle(0, 0, size, size);
        var sd = scaled.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var od = outBmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int bytes = Math.Abs(sd.Stride) * size;
            var sbuf = new byte[bytes]; var obuf = new byte[bytes];
            Marshal.Copy(sd.Scan0, sbuf, 0, bytes);
            for (int p = 0; p < bytes; p += 4)
            {
                byte a = sbuf[p + 3];
                obuf[p + 0] = color.B; obuf[p + 1] = color.G; obuf[p + 2] = color.R; obuf[p + 3] = a;
            }
            Marshal.Copy(obuf, 0, od.Scan0, bytes);
        }
        finally { scaled.UnlockBits(sd); outBmp.UnlockBits(od); }
        return outBmp;
    }

    static Color Resolve(bool system)
    {
        if (!system) return Brand;
        try
        {
            var v = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
            if (v is int i && i == 1) return Color.Black;
        }
        catch { }
        return Color.White;
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool DestroyIcon(IntPtr handle);
    static Icon ToIcon(Bitmap bmp)
    {
        IntPtr h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }
}

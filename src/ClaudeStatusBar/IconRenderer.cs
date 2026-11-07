using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ClaudeStatusBar;

public enum AnimKind { Web, Code, Crab, Orbit }

/// <summary>Builds frames for each animation style as Bitmaps (pill, menu rows) and Icons (tray): Spark (tinted
/// PNG frames), Unicode (glyph spinner), Clawd (full-colour pixel crab), Orbit (dot strips with a spark
/// crossfade in/out). Everything is built once and cached; callers must NOT dispose what they get back.
/// Call <see cref="ClearCache"/> when the style, color or system theme changes.</summary>
public class IconRenderer : IDisposable
{
    public static readonly Color Brand = Color.FromArgb(0xD9, 0x77, 0x57);
    public static readonly Color Amber = Color.FromArgb(0xF2, 0xBA, 0x2E);

    static readonly string[] CodeGlyphs = { "✻", "✽", "✶", "✳", "✢" };
    const int CodeSub = 6;

    // Orbit (macOS "mark"): 48px frames in vertical strips, authored at 30 fps and played at 15 (step 2).
    // Sequence = entry, orbit loop ×2, breathe loop ×4, exit. Playback loops the body after the entry and
    // plays the exit as an outro when work stops. The spark crossfades over the first/last few frames.
    const int OrbitPx = 48, OrbitFade = 4;
    const float OrbitDotScale = 1.08f, OrbitSparkScale = 0.92f;
    static readonly (string strip, int reps)[] OrbitPlan = { ("en110", 1), ("loop110", 2), ("loop40", 4), ("ex40", 1) };

    readonly List<Bitmap> _spark = new();
    readonly List<Bitmap> _crab = new();
    readonly Dictionary<string, Bitmap> _orbit = new();
    readonly List<(string strip, int frame)> _orbitSeq = new();
    readonly int _orbitEntry, _orbitExit;
    Bitmap? _logo;

    readonly Dictionary<(AnimKind k, int i, bool sys, int size), Bitmap> _bmpCache = new();
    readonly Dictionary<(AnimKind k, int i, bool sys, int size), Icon> _icoCache = new();
    readonly Dictionary<(int argb, int size), Bitmap> _dotCache = new();
    Color? _systemColor;

    public IconRenderer()
    {
        var asm = Assembly.GetExecutingAssembly();
        var names = asm.GetManifestResourceNames();
        foreach (var n in names.Where(n => n.Contains("spark-")).OrderBy(n => n)) _spark.Add(Load(asm, n));
        foreach (var n in names.Where(n => n.Contains("crab-")).OrderBy(n => n)) _crab.Add(Load(asm, n));
        var logo = names.FirstOrDefault(n => n.EndsWith("logo.png"));
        if (logo != null) _logo = Load(asm, logo);

        foreach (var (strip, reps) in OrbitPlan)
        {
            var res = names.FirstOrDefault(n => n.EndsWith($"orbit-{strip}.png"));
            if (res == null) continue;
            var bmp = Load(asm, res);
            _orbit[strip] = bmp;
            for (int r = 0; r < reps; r++)
                for (int f = 0; f < bmp.Height / OrbitPx; f++) _orbitSeq.Add((strip, f));
        }
        _orbitEntry = _orbit.TryGetValue("en110", out var en) ? en.Height / OrbitPx : 0;
        _orbitExit = _orbit.TryGetValue("ex40", out var ex) ? ex.Height / OrbitPx : 0;
    }

    static Bitmap Load(Assembly asm, string name)
    {
        using var s = asm.GetManifestResourceStream(name)!;
        using var tmp = new Bitmap(s);
        return new Bitmap(tmp);
    }

    public static AnimKind ParseStyle(string s) => s switch
    {
        "code" => AnimKind.Code, "crab" => AnimKind.Crab, "mark" => AnimKind.Orbit, _ => AnimKind.Web,
    };

    // ---- playback shape ----
    /// <summary>Frames in the looping body (for Orbit: everything but the exit).</summary>
    public int FrameCount(AnimKind k) => k switch
    {
        AnimKind.Crab => Math.Max(1, _crab.Count),
        AnimKind.Code => CodeGlyphs.Length * CodeSub,
        AnimKind.Orbit => Math.Max(1, _orbitSeq.Count - _orbitExit),
        _ => Math.Max(1, _spark.Count),
    };
    /// <summary>Where the body loops back to (Orbit skips its entry on repeat).</summary>
    public int LoopStart(AnimKind k) => k == AnimKind.Orbit ? _orbitEntry : 0;
    /// <summary>Total frames including an outro (Orbit's exit), i.e. the index the outro ends at.</summary>
    public int SequenceLength(AnimKind k) => k == AnimKind.Orbit ? _orbitSeq.Count : FrameCount(k);
    public bool HasOutro(AnimKind k) => k == AnimKind.Orbit && _orbitExit > 0;
    public int Step(AnimKind k) => k == AnimKind.Orbit ? 2 : 1;
    public int IntervalMs(AnimKind k) => k switch { AnimKind.Crab => 80, AnimKind.Code => 120, AnimKind.Orbit => 66, _ => 110 };

    /// <summary>Advance a frame index one tick, wrapping within the body.</summary>
    public int Next(AnimKind k, int i)
    {
        i += Step(k);
        int count = FrameCount(k), loop = LoopStart(k);
        if (i >= count) i = loop + (i - loop) % Math.Max(1, count - loop);
        return i;
    }

    // ---- public, cached: Icons for the tray, Bitmaps for the pill/menu ----
    public Icon Frame(AnimKind k, int i, bool system, int size) => Ico((k, i, system, size));
    public Bitmap FrameBitmap(AnimKind k, int i, bool system, int size) => Bmp((k, i, system, size));
    public Icon Resting(AnimKind k, bool system, int size) => Ico((k, -1, system, size));
    public Bitmap RestingBitmap(AnimKind k, bool system, int size) => Bmp((k, -1, system, size));
    public Icon Dot(int size) => Ico((AnimKind.Web, -2, false, size));
    public Bitmap DotBitmap(int size) => Bmp((AnimKind.Web, -2, false, size));

    /// <summary>A small filled status dot for menu rows.</summary>
    public Bitmap StatusDot(Color c, int size)
    {
        if (_dotCache.TryGetValue((c.ToArgb(), size), out var hit)) return hit;
        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = size * 0.5f, o = (size - d) / 2f;
            using var br = new SolidBrush(c);
            g.FillEllipse(br, o, o, d, d);
        }
        return _dotCache[(c.ToArgb(), size)] = bmp;
    }

    public void ClearCache()
    {
        foreach (var b in _bmpCache.Values) b.Dispose();
        foreach (var i in _icoCache.Values) i.Dispose();
        foreach (var b in _dotCache.Values) b.Dispose();
        _bmpCache.Clear(); _icoCache.Clear(); _dotCache.Clear();
        _systemColor = null;
    }

    Bitmap Bmp((AnimKind k, int i, bool sys, int size) key)
    {
        if (_bmpCache.TryGetValue(key, out var hit)) return hit;
        var b = key.i switch
        {
            -2 => BuildDot(key.size),
            -1 => BuildResting(key.k, key.sys, key.size),
            _ => BuildFrame(key.k, key.i, key.sys, key.size),
        };
        return _bmpCache[key] = b;
    }

    Icon Ico((AnimKind k, int i, bool sys, int size) key)
    {
        if (_icoCache.TryGetValue(key, out var hit)) return hit;
        return _icoCache[key] = ToIcon(Bmp(key));
    }

    // ---- builders (return a fresh Bitmap; the cache owns it) ----
    Bitmap BuildFrame(AnimKind k, int i, bool system, int size) => k switch
    {
        AnimKind.Crab => BuildCrab(i, size),
        AnimKind.Code => BuildCode(i, system, size),
        AnimKind.Orbit => BuildOrbit(i, system, size),
        _ => BuildTint(_spark.Count > 0 ? _spark[i % _spark.Count] : _logo, system, size),
    };

    Bitmap BuildResting(AnimKind k, bool system, int size) => k switch
    {
        AnimKind.Crab => BuildCrab(0, size),
        AnimKind.Orbit => BuildOrbitSpark(system, size),
        _ => BuildTint(_logo ?? _spark.FirstOrDefault(), system, size),
    };

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

    Bitmap BuildOrbit(int i, bool system, int size)
    {
        if (i < 0 || i >= _orbitSeq.Count) return BuildOrbitSpark(system, size);
        var (strip, f) = _orbitSeq[i];
        float sparkA = 0;
        if (strip == "en110" && f < OrbitFade) sparkA = 1 - (f + 1f) / OrbitFade;
        else if (strip == "ex40") { int tail = _orbitExit - f - 1; if (tail < OrbitFade) sparkA = 1 - (float)tail / OrbitFade; }

        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            DrawScaled(g, _orbit[strip], new Rectangle(0, f * OrbitPx, OrbitPx, OrbitPx), size, OrbitDotScale, 1 - sparkA);
            if (sparkA > 0 && _logo != null) DrawScaled(g, _logo, new Rectangle(0, 0, _logo.Width, _logo.Height), size, OrbitSparkScale, sparkA);
        }
        Recolor(bmp, Resolve(system));
        return bmp;
    }

    Bitmap BuildOrbitSpark(bool system, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        if (_logo == null) return bmp;
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            DrawScaled(g, _logo, new Rectangle(0, 0, _logo.Width, _logo.Height), size, OrbitSparkScale, 1);
        }
        Recolor(bmp, Resolve(system));
        return bmp;
    }

    static void DrawScaled(Graphics g, Bitmap src, Rectangle srcRect, int size, float scale, float alpha)
    {
        if (alpha <= 0) return;
        float d = size * scale, o = (size - d) / 2f;
        using var ia = new ImageAttributes();
        ia.SetWrapMode(WrapMode.TileFlipXY); // no bleed from the neighbouring frame in the strip
        if (alpha < 1) ia.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
        g.DrawImage(src, new[] { new PointF(o, o), new PointF(o + d, o), new PointF(o, o + d) },
            srcRect, GraphicsUnit.Pixel, ia);
    }

    static float Smooth(float u) => u * u * (3 - 2 * u);

    Bitmap BuildTint(Bitmap? src, bool system, int size)
    {
        var outBmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        if (src == null) return outBmp;
        using (var g = Graphics.FromImage(outBmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, new Rectangle(0, 0, size, size));
        }
        Recolor(outBmp, Resolve(system));
        return outBmp;
    }

    /// <summary>Paint every pixel <paramref name="color"/>, keeping its alpha (the art is used as a mask).</summary>
    static void Recolor(Bitmap bmp, Color color)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var d = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int bytes = Math.Abs(d.Stride) * bmp.Height;
            var buf = new byte[bytes];
            Marshal.Copy(d.Scan0, buf, 0, bytes);
            for (int p = 0; p < bytes; p += 4) { buf[p] = color.B; buf[p + 1] = color.G; buf[p + 2] = color.R; }
            Marshal.Copy(buf, 0, d.Scan0, bytes);
        }
        finally { bmp.UnlockBits(d); }
    }

    Color Resolve(bool system)
    {
        if (!system) return Brand;
        return _systemColor ??= Theme.TaskbarIsLight() ? Color.Black : Color.White;
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool DestroyIcon(IntPtr handle);
    static Icon ToIcon(Bitmap bmp)
    {
        IntPtr h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }

    public void Dispose()
    {
        ClearCache();
        foreach (var b in _spark.Concat(_crab).Concat(_orbit.Values)) b.Dispose();
        _logo?.Dispose();
    }
}

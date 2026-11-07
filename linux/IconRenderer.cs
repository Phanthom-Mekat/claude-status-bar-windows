using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace ClaudeStatusBar;

public enum AnimKind { Web, Code, Crab, Orbit }

/// <summary>Linux port of the Windows IconRenderer, drawn with Skia (bundled with Avalonia): Spark (tinted PNG
/// frames), Unicode (glyph spinner), Clawd (full-colour pixel crab), Orbit (dot strips with a spark crossfade).
/// Same art, same frame plan, same timings. Everything is built once and cached.</summary>
public sealed class IconRenderer
{
    public static readonly SKColor Brand = new(0xD9, 0x77, 0x57);
    public static readonly SKColor Amber = new(0xF2, 0xBA, 0x2E);

    static readonly string[] CodeGlyphs = { "✻", "✽", "✶", "✳", "✢" };
    const int CodeSub = 6;
    const int OrbitPx = 48, OrbitFade = 4;
    const float OrbitDotScale = 1.08f, OrbitSparkScale = 0.92f;
    static readonly (string strip, int reps)[] OrbitPlan = { ("en110", 1), ("loop110", 2), ("loop40", 4), ("ex40", 1) };
    static readonly SKTypeface GlyphFace = SKFontManager.Default.MatchCharacter('✻') ?? SKTypeface.Default;

    readonly List<SKBitmap> _spark = new(), _crab = new();
    readonly Dictionary<string, SKBitmap> _orbit = new();
    readonly List<(string strip, int frame)> _orbitSeq = new();
    readonly int _orbitEntry, _orbitExit;
    readonly SKBitmap? _logo;

    // ponytail: replaced bitmaps aren't disposed (a control may still be drawing one); style/theme changes are rare.
    readonly Dictionary<(AnimKind k, int i, bool sys, int size), Bitmap> _cache = new();
    readonly Dictionary<Bitmap, WindowIcon> _icons = new();
    readonly Dictionary<(uint argb, int size), Bitmap> _dots = new();

    /// <summary>Set from the desktop's light/dark preference; drives the "System" color.</summary>
    public bool SystemIsLight { get; set; }

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

    static SKBitmap Load(Assembly asm, string name)
    {
        using var s = asm.GetManifestResourceStream(name)!;
        return SKBitmap.Decode(s);
    }

    public static AnimKind ParseStyle(string s) => s switch
    {
        "code" => AnimKind.Code, "crab" => AnimKind.Crab, "mark" => AnimKind.Orbit, _ => AnimKind.Web,
    };

    // ---- playback shape (identical to Windows) ----
    public int FrameCount(AnimKind k) => k switch
    {
        AnimKind.Crab => Math.Max(1, _crab.Count),
        AnimKind.Code => CodeGlyphs.Length * CodeSub,
        AnimKind.Orbit => Math.Max(1, _orbitSeq.Count - _orbitExit),
        _ => Math.Max(1, _spark.Count),
    };
    public int LoopStart(AnimKind k) => k == AnimKind.Orbit ? _orbitEntry : 0;
    public int SequenceLength(AnimKind k) => k == AnimKind.Orbit ? _orbitSeq.Count : FrameCount(k);
    public bool HasOutro(AnimKind k) => k == AnimKind.Orbit && _orbitExit > 0;
    public int Step(AnimKind k) => k == AnimKind.Orbit ? 2 : 1;
    public int IntervalMs(AnimKind k) => k switch { AnimKind.Crab => 80, AnimKind.Code => 120, AnimKind.Orbit => 66, _ => 110 };

    public int Next(AnimKind k, int i)
    {
        i += Step(k);
        int count = FrameCount(k), loop = LoopStart(k);
        if (i >= count) i = loop + (i - loop) % Math.Max(1, count - loop);
        return i;
    }

    // ---- public, cached ----
    public Bitmap Frame(AnimKind k, int i, bool system, int size) => Get((k, i, system, size));
    public Bitmap Resting(AnimKind k, bool system, int size) => Get((k, -1, system, size));
    public Bitmap Dot(int size) => Get((AnimKind.Web, -2, false, size));

    /// <summary>The same bitmap as a tray icon (cached: the tray re-encodes whatever it's handed).</summary>
    public WindowIcon Icon(Bitmap b) => _icons.TryGetValue(b, out var hit) ? hit : _icons[b] = new WindowIcon(b);

    /// <summary>A small filled status dot for menu rows.</summary>
    public Bitmap StatusDot(SKColor c, int size)
    {
        var key = ((uint)c, size);
        if (_dots.TryGetValue(key, out var hit)) return hit;
        return _dots[key] = Render(size, cv => FillDot(cv, size, c));
    }

    public void ClearCache() { _cache.Clear(); _icons.Clear(); }

    Bitmap Get((AnimKind k, int i, bool sys, int size) key)
    {
        if (_cache.TryGetValue(key, out var hit)) return hit;
        var color = key.sys ? (SystemIsLight ? SKColors.Black : SKColors.White) : Brand;
        Action<SKCanvas> draw = key.i switch
        {
            -2 => cv => FillDot(cv, key.size, Amber),
            -1 => key.k switch
            {
                AnimKind.Crab => cv => DrawCrab(cv, 0, key.size),
                AnimKind.Orbit => cv => DrawScaled(cv, _logo, null, key.size, OrbitSparkScale, 1, color),
                _ => cv => DrawScaled(cv, _logo ?? _spark.FirstOrDefault(), null, key.size, 1, 1, color),
            },
            _ => key.k switch
            {
                AnimKind.Crab => cv => DrawCrab(cv, key.i, key.size),
                AnimKind.Code => cv => DrawCode(cv, key.i, key.size, color),
                AnimKind.Orbit => cv => DrawOrbit(cv, key.i, key.size, color),
                _ => cv => DrawScaled(cv, _spark.Count > 0 ? _spark[key.i % _spark.Count] : _logo, null, key.size, 1, 1, color),
            },
        };
        return _cache[key] = Render(key.size, draw);
    }

    static Bitmap Render(int size, Action<SKCanvas> draw)
    {
        using var bmp = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var cv = new SKCanvas(bmp)) { cv.Clear(SKColors.Transparent); draw(cv); }
        using var img = SKImage.FromBitmap(bmp);
        using var png = img.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(png.ToArray());
        return new Bitmap(ms);
    }

    static void FillDot(SKCanvas cv, int size, SKColor c)
    {
        using var p = new SKPaint { Color = c, IsAntialias = true };
        cv.DrawCircle(size / 2f, size / 2f, size * 0.25f, p);
    }

    void DrawCrab(SKCanvas cv, int i, int size)
    {
        if (_crab.Count == 0) return;
        var src = _crab[i % _crab.Count];
        float ar = (float)src.Width / src.Height;
        float w = ar >= 1 ? size : size * ar, h = ar >= 1 ? size / ar : size;
        using var p = new SKPaint { FilterQuality = SKFilterQuality.None }; // pixel art stays crisp
        cv.DrawBitmap(src, SKRect.Create((size - w) / 2, (size - h) / 2, w, h), p);
    }

    static void DrawCode(SKCanvas cv, int i, int size, SKColor color)
    {
        var glyph = CodeGlyphs[(i / CodeSub) % CodeGlyphs.Length];
        float local = ((i % CodeSub) + 0.5f) / CodeSub;
        float env = local < 0.3f ? Smooth(local / 0.3f) : local > 0.7f ? Smooth((1 - local) / 0.3f) : 1f;
        float scale = 0.5f + 0.5f * env;
        using var p = new SKPaint { Color = color, IsAntialias = true, Typeface = GlyphFace, TextSize = Math.Max(4f, size * scale * 0.95f) };
        var ink = new SKRect();
        p.MeasureText(glyph, ref ink);
        cv.DrawText(glyph, size / 2f - ink.MidX, size / 2f - ink.MidY, p);
    }

    void DrawOrbit(SKCanvas cv, int i, int size, SKColor color)
    {
        if (i < 0 || i >= _orbitSeq.Count) { DrawScaled(cv, _logo, null, size, OrbitSparkScale, 1, color); return; }
        var (strip, f) = _orbitSeq[i];
        float sparkA = 0;
        if (strip == "en110" && f < OrbitFade) sparkA = 1 - (f + 1f) / OrbitFade;
        else if (strip == "ex40") { int tail = _orbitExit - f - 1; if (tail < OrbitFade) sparkA = 1 - (float)tail / OrbitFade; }
        DrawScaled(cv, _orbit[strip], SKRectI.Create(0, f * OrbitPx, OrbitPx, OrbitPx), size, OrbitDotScale, 1 - sparkA, color);
        if (sparkA > 0) DrawScaled(cv, _logo, null, size, OrbitSparkScale, sparkA, color);
    }

    /// <summary>Draw (a region of) the art centred at <paramref name="scale"/>, used as an alpha mask for
    /// <paramref name="color"/>. A strip region is drawn from an extracted subset so filtering can't bleed in
    /// the neighbouring frame.</summary>
    static void DrawScaled(SKCanvas cv, SKBitmap? src, SKRectI? region, int size, float scale, float alpha, SKColor color)
    {
        if (src == null || alpha <= 0) return;
        SKBitmap art = src;
        SKBitmap? sub = null;
        if (region is { } r) { sub = new SKBitmap(); if (src.ExtractSubset(sub, r)) art = sub; }
        try
        {
            float d = size * scale, o = (size - d) / 2f;
            using var tint = SKColorFilter.CreateBlendMode(color, SKBlendMode.SrcIn);
            using var p = new SKPaint
            {
                ColorFilter = tint, IsAntialias = true, FilterQuality = SKFilterQuality.High,
                Color = SKColors.White.WithAlpha((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1))),
            };
            cv.DrawBitmap(art, SKRect.Create(o, o, d, d), p);
        }
        finally { sub?.Dispose(); }
    }

    static float Smooth(float u) => u * u * (3 - 2 * u);
}

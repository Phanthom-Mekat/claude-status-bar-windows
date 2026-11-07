using SkiaSharp;

namespace ClaudeStatusBar;

public enum MascotMood { Sleeping, Thinking, Working, Asking, Celebrating }

/// <summary>"Sprout", the pill's companion: a line-for-line Skia port of the Windows Mascot.cs (same shapes, sizes,
/// motion and moods) so both platforms show the same character. <see cref="Draw"/> is a pure function of
/// (mood, time); the pill repaints it on a timer. GDI+ and Skia agree on y-down, clockwise arc angles.</summary>
public static class Mascot
{
    static readonly SKColor Body = IconRenderer.Brand;
    static readonly SKColor BodyLight = new(0xEC, 0x9A, 0x7C);
    static readonly SKColor BodyDark = new(0xA8, 0x52, 0x38);
    static readonly SKColor Ink = new(46, 28, 22);
    static readonly SKColor Cheek = new(255, 150, 150, 110);
    static readonly SKColor Glow = new(255, 236, 170);
    static readonly SKTypeface ZFace = SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default;

    /// <summary>Whether this mood moves (the pill only runs its fast timer when it does).</summary>
    public static bool Animated(MascotMood m) => m != MascotMood.Sleeping;

    public static void Draw(SKCanvas g, SKRect r, MascotMood mood, double t)
    {
        float s = Math.Min(r.Width, r.Height);
        float cx = r.MidX, ground = r.Bottom - s * 0.05f;

        // ---- motion ----
        float lift = 0, squash = 1;
        switch (mood)
        {
            case MascotMood.Sleeping: squash = 1 + 0.035f * (float)Math.Sin(t * 1.6); break;                     // breathing
            case MascotMood.Thinking: lift = s * 0.04f * (float)Math.Abs(Math.Sin(t * 2.4)); break;               // gentle float
            case MascotMood.Working:
                lift = s * 0.06f * (float)Math.Abs(Math.Sin(t * 9));                                              // busy bob
                squash = 1 - 0.06f * (float)Math.Abs(Math.Cos(t * 9)); break;
            case MascotMood.Asking: lift = s * 0.09f * (float)Math.Max(0, Math.Sin(t * 6)); break;                 // impatient bounce
            case MascotMood.Celebrating:
                lift = s * 0.16f * (float)Math.Max(0, Math.Sin(t * 7));                                            // hop
                squash = lift < s * 0.02f ? 0.9f : 1.04f; break;
        }

        float bw = s * 0.88f * (2 - squash), bh = s * 0.64f * squash;
        var body = SKRect.Create(cx - bw / 2f, ground - bh - lift, bw, bh);

        // ground shadow shrinks as it rises
        float sh = 1 - lift / (s * 0.3f);
        using (var p = Fill(new SKColor(0, 0, 0, (byte)Math.Clamp(70 * sh, 0, 255))))
            g.DrawOval(SKRect.Create(cx - bw * 0.36f * sh, ground - s * 0.035f, bw * 0.72f * sh, s * 0.07f), p);

        // antenna: a stalk with a little spark that glows while Claude works
        float topY = body.Top + bh * 0.04f, tipY = topY - s * 0.12f, tipX = cx + s * 0.05f;
        using (var stalk = new SKPath())
        using (var p = Stroke(BodyDark, Math.Max(1f, s * 0.05f)))
        {
            stalk.MoveTo(cx, topY);
            stalk.CubicTo(cx, topY - s * 0.06f, tipX, tipY + s * 0.04f, tipX, tipY);
            g.DrawPath(stalk, p);
        }
        var sparkC = mood switch
        {
            MascotMood.Sleeping => BodyDark,
            MascotMood.Asking => IconRenderer.Amber,
            MascotMood.Celebrating => SKColors.White,
            _ => Lerp(Body, Glow, 0.5f + 0.5f * (float)Math.Sin(t * 5)),
        };
        float sparkR = s * (mood is MascotMood.Thinking or MascotMood.Working ? 0.085f + 0.015f * (float)Math.Sin(t * 5) : 0.07f);
        Star(g, tipX, tipY, sparkR, sparkC, (float)(t * (mood == MascotMood.Working ? 90 : 25)));

        // body: a soft top-lit gumdrop
        using (var path = Gumdrop(body))
        {
            using var shader = SKShader.CreateLinearGradient(new SKPoint(0, body.Top - 1), new SKPoint(0, body.Bottom + 1),
                new[] { BodyLight, Body }, SKShaderTileMode.Clamp);
            using (var p = new SKPaint { Shader = shader, IsAntialias = true }) g.DrawPath(path, p);
            using var edge = Stroke(BodyDark.WithAlpha(90), Math.Max(1f, s * 0.025f));
            g.DrawPath(path, edge);
        }

        // face
        float eyeY = body.Top + bh * 0.46f, eyeDx = bw * 0.19f, er = s * 0.085f;
        float lookX = 0, lookY = 0;
        if (mood == MascotMood.Thinking) { lookX = er * 0.35f; lookY = -er * 0.4f; }   // gazing up, pondering
        if (mood == MascotMood.Working) { lookY = er * 0.35f; lookX = er * 0.25f * (float)Math.Sin(t * 1.3); } // reading
        bool blink = mood is MascotMood.Thinking or MascotMood.Working && t % 3.4 < 0.13;

        using var ink = Stroke(Ink, Math.Max(1f, s * 0.045f));
        using var inkFill = Fill(Ink);
        foreach (var side in new[] { -1, 1 })
        {
            float ex = cx + side * eyeDx;
            switch (mood)
            {
                case MascotMood.Sleeping:                          // ‿ closed
                    g.DrawArc(SKRect.Create(ex - er, eyeY - er * 0.6f, er * 2, er * 1.4f), 20, 140, false, ink); break;
                case MascotMood.Celebrating:                       // ^ happy
                    g.DrawArc(SKRect.Create(ex - er, eyeY - er * 0.3f, er * 2, er * 1.6f), 200, 140, false, ink); break;
                default:
                    if (blink) { g.DrawLine(ex - er * 0.8f, eyeY, ex + er * 0.8f, eyeY, ink); break; }
                    float w = mood == MascotMood.Asking ? er * 1.25f : er;   // wide-eyed when asking
                    using (var white = Fill(SKColors.White)) g.DrawOval(SKRect.Create(ex - w, eyeY - w * 1.1f, w * 2, w * 2.2f), white);
                    float pr = mood == MascotMood.Asking ? w * 0.42f : w * 0.58f;
                    g.DrawOval(SKRect.Create(ex + lookX - pr, eyeY + lookY - pr, pr * 2, pr * 2), inkFill);
                    break;
            }
        }

        // cheeks + mouth
        if (mood is MascotMood.Sleeping or MascotMood.Celebrating)
            using (var c = Fill(Cheek))
                foreach (var side in new[] { -1, 1 })
                    g.DrawOval(SKRect.Create(cx + side * bw * 0.32f - er, eyeY + er * 0.9f, er * 2, er * 1.1f), c);
        float my = eyeY + er * 1.7f;
        switch (mood)
        {
            case MascotMood.Celebrating:
                g.DrawArc(SKRect.Create(cx - er * 1.1f, my - er * 1.1f, er * 2.2f, er * 2.2f), 0, 180, true, inkFill); break;
            case MascotMood.Asking:
                g.DrawOval(SKRect.Create(cx - er * 0.45f, my - er * 0.2f, er * 0.9f, er * 1.0f), inkFill); break;
            case MascotMood.Working:
            case MascotMood.Thinking:
                g.DrawArc(SKRect.Create(cx - er * 0.7f, my - er * 0.9f, er * 1.4f, er * 1.2f), 30, 120, false, ink); break;
        }

        // extras floating around the head
        float hx = body.Right - bw * 0.02f, hy = body.Top;
        switch (mood)
        {
            case MascotMood.Sleeping:
            {
                bool big = ((int)t) % 2 == 0;  // repainted once a second while sleeping: the "z" drifts
                using var zp = new SKPaint
                {
                    Color = new SKColor(200, 200, 210, 170), IsAntialias = true, Typeface = ZFace,
                    TextSize = Math.Max(5f, s * (big ? 0.26f : 0.2f)),
                };
                // GDI+ DrawString takes the top-left; Skia takes the baseline.
                g.DrawText("z", hx - s * 0.04f, hy - s * (big ? 0.30f : 0.22f) - zp.FontMetrics.Ascent, zp);
                break;
            }
            case MascotMood.Thinking:
                for (int i = 0; i < 3; i++)
                {
                    float a = (float)Math.Clamp((t * 1.6 % 3) - i * 0.6, 0, 1);   // dots appear one by one
                    using var d = Fill(new SKColor(230, 230, 236, (byte)(230 * a)));
                    float rr = s * (0.035f + i * 0.012f);
                    g.DrawCircle(hx + i * s * 0.06f, hy - i * s * 0.07f, rr, d);
                }
                break;
            case MascotMood.Asking:
            {
                float br = s * 0.17f, bx = hx + s * 0.02f, by = hy - s * 0.02f;
                using (var bb = Fill(IconRenderer.Amber)) g.DrawCircle(bx, by, br, bb);
                using var ip = Stroke(Ink, Math.Max(1f, s * 0.05f));
                g.DrawLine(bx, by - br * 0.5f, bx, by + br * 0.12f, ip);
                g.DrawOval(SKRect.Create(bx - br * 0.13f, by + br * 0.36f, br * 0.26f, br * 0.26f), inkFill);
                break;
            }
            case MascotMood.Celebrating:
                for (int i = 0; i < 3; i++)
                {
                    float tw = 0.5f + 0.5f * (float)Math.Sin(t * 8 + i * 2.1);
                    var (sx, sy) = i switch { 0 => (body.Left - s * 0.08f, body.Top + s * 0.02f), 1 => (hx + s * 0.06f, hy - s * 0.06f), _ => (cx - s * 0.18f, hy - s * 0.2f) };
                    Star(g, sx, sy, s * 0.07f * tw + s * 0.02f, i == 1 ? Glow : SKColors.White, 0);
                }
                break;
        }
    }

    static SKPaint Fill(SKColor c) => new() { Color = c, IsAntialias = true, Style = SKPaintStyle.Fill };
    static SKPaint Stroke(SKColor c, float w) => new()
    {
        Color = c, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = w, StrokeCap = SKStrokeCap.Round,
    };

    // A gumdrop: round top, slightly flattened bottom.
    static SKPath Gumdrop(SKRect r)
    {
        var p = new SKPath();
        float k = r.Height * 0.28f;
        p.MoveTo(r.Left, r.Bottom - k);
        p.CubicTo(r.Left, r.Top - r.Height * 0.1f, r.Right, r.Top - r.Height * 0.1f, r.Right, r.Bottom - k);
        p.CubicTo(r.Right, r.Bottom + k * 0.15f, r.Left, r.Bottom + k * 0.15f, r.Left, r.Bottom - k);
        p.Close();
        return p;
    }

    // A 4-point sparkle.
    static void Star(SKCanvas g, float x, float y, float r, SKColor c, float rotDeg)
    {
        if (r <= 0.3f) return;
        using var path = new SKPath();
        for (int i = 0; i < 8; i++)
        {
            double a = (rotDeg + i * 45) * Math.PI / 180;
            float rr = i % 2 == 0 ? r : r * 0.38f;
            var pt = new SKPoint(x + rr * (float)Math.Cos(a), y + rr * (float)Math.Sin(a));
            if (i == 0) path.MoveTo(pt); else path.LineTo(pt);
        }
        path.Close();
        using var p = Fill(c);
        g.DrawPath(path, p);
    }

    static SKColor Lerp(SKColor a, SKColor b, float u) => new(
        (byte)(a.Red + (b.Red - a.Red) * u), (byte)(a.Green + (b.Green - a.Green) * u), (byte)(a.Blue + (b.Blue - a.Blue) * u));
}

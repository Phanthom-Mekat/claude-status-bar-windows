using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClaudeStatusBar;

public enum MascotMood { Sleeping, Thinking, Working, Asking, Celebrating }

/// <summary>"Sprout": the pill's little companion, drawn in code (no assets) so it stays crisp at any DPI. An
/// orange gumdrop with a spark antenna that acts out Claude's state: asleep when idle, looking up while thinking,
/// bobbing while working, waving a "!" when it needs your approval, hopping when a turn is done.
/// <see cref="Draw"/> is a pure function of (mood, time), so the pill just repaints on a timer.</summary>
public static class Mascot
{
    static readonly Color Body = IconRenderer.Brand;
    static readonly Color BodyLight = Color.FromArgb(0xEC, 0x9A, 0x7C);
    static readonly Color BodyDark = Color.FromArgb(0xA8, 0x52, 0x38);
    static readonly Color Ink = Color.FromArgb(46, 28, 22);
    static readonly Color Cheek = Color.FromArgb(110, 255, 150, 150);
    static readonly Color Glow = Color.FromArgb(255, 236, 170);

    /// <summary>Whether this mood moves (the pill only runs its fast timer when it does).</summary>
    public static bool Animated(MascotMood m) => m != MascotMood.Sleeping;

    public static void Draw(Graphics g, RectangleF r, MascotMood mood, double t)
    {
        var oldSmooth = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Math.Min(r.Width, r.Height);
        float cx = r.X + r.Width / 2f, ground = r.Bottom - s * 0.05f;

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

        // Sized for ~25px (the pill): the body has to carry the read, so it takes most of the box.
        float bw = s * 0.88f * (2 - squash), bh = s * 0.64f * squash;
        var body = new RectangleF(cx - bw / 2f, ground - bh - lift, bw, bh);

        // ground shadow shrinks as it rises
        float sh = 1 - lift / (s * 0.3f);
        using (var b = new SolidBrush(Color.FromArgb((int)(70 * sh), 0, 0, 0)))
            g.FillEllipse(b, cx - bw * 0.36f * sh, ground - s * 0.035f, bw * 0.72f * sh, s * 0.07f);

        // antenna: a stalk with a little spark that glows while Claude works
        float topY = body.Y + bh * 0.04f, tipY = topY - s * 0.12f, tipX = cx + s * 0.05f;
        using (var pen = new Pen(BodyDark, Math.Max(1f, s * 0.05f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawBezier(pen, cx, topY, cx, topY - s * 0.06f, tipX, tipY + s * 0.04f, tipX, tipY);
        Color sparkC = mood switch
        {
            MascotMood.Sleeping => BodyDark,
            MascotMood.Asking => IconRenderer.Amber,
            MascotMood.Celebrating => Color.White,
            _ => Lerp(Body, Glow, 0.5f + 0.5f * (float)Math.Sin(t * 5)),
        };
        float sparkR = s * (mood is MascotMood.Thinking or MascotMood.Working ? 0.085f + 0.015f * (float)Math.Sin(t * 5) : 0.07f);
        Star(g, tipX, tipY, sparkR, sparkC, (float)(t * (mood == MascotMood.Working ? 90 : 25)));

        // body: a soft top-lit gumdrop
        using (var path = Gumdrop(body))
        using (var br = new LinearGradientBrush(new RectangleF(body.X, body.Y - 1, body.Width, body.Height + 2), BodyLight, Body, 90f))
        {
            g.FillPath(br, path);
            using var edge = new Pen(Color.FromArgb(90, BodyDark), Math.Max(1f, s * 0.025f));
            g.DrawPath(edge, path);
        }

        // face
        float eyeY = body.Y + bh * 0.46f, eyeDx = bw * 0.19f, er = s * 0.085f;
        float lookX = 0, lookY = 0;
        if (mood == MascotMood.Thinking) { lookX = er * 0.35f; lookY = -er * 0.4f; }   // gazing up, pondering
        if (mood == MascotMood.Working) { lookY = er * 0.35f; lookX = er * 0.25f * (float)Math.Sin(t * 1.3); } // reading
        bool blink = mood is MascotMood.Thinking or MascotMood.Working && t % 3.4 < 0.13;

        using var ink = new Pen(Ink, Math.Max(1f, s * 0.045f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        foreach (var side in new[] { -1, 1 })
        {
            float ex = cx + side * eyeDx;
            switch (mood)
            {
                case MascotMood.Sleeping:                          // ‿ closed
                    g.DrawArc(ink, ex - er, eyeY - er * 0.6f, er * 2, er * 1.4f, 20, 140); break;
                case MascotMood.Celebrating:                       // ^ happy
                    g.DrawArc(ink, ex - er, eyeY - er * 0.3f, er * 2, er * 1.6f, 200, 140); break;
                default:
                    if (blink) { g.DrawLine(ink, ex - er * 0.8f, eyeY, ex + er * 0.8f, eyeY); break; }
                    float w = mood == MascotMood.Asking ? er * 1.25f : er;   // wide-eyed when asking
                    using (var white = new SolidBrush(Color.White)) g.FillEllipse(white, ex - w, eyeY - w * 1.1f, w * 2, w * 2.2f);
                    float p = mood == MascotMood.Asking ? w * 0.42f : w * 0.58f;
                    using (var pupil = new SolidBrush(Ink)) g.FillEllipse(pupil, ex + lookX - p, eyeY + lookY - p, p * 2, p * 2);
                    break;
            }
        }

        // cheeks + mouth
        if (mood is MascotMood.Sleeping or MascotMood.Celebrating)
            using (var c = new SolidBrush(Cheek))
                foreach (var side in new[] { -1, 1 })
                    g.FillEllipse(c, cx + side * bw * 0.32f - er, eyeY + er * 0.9f, er * 2, er * 1.1f);
        float my = eyeY + er * 1.7f;
        switch (mood)
        {
            case MascotMood.Celebrating:
                using (var m = new SolidBrush(Ink)) g.FillPie(m, cx - er * 1.1f, my - er * 1.1f, er * 2.2f, er * 2.2f, 0, 180); break;
            case MascotMood.Asking:
                using (var m = new SolidBrush(Ink)) g.FillEllipse(m, cx - er * 0.45f, my - er * 0.2f, er * 0.9f, er * 1.0f); break;
            case MascotMood.Working:
            case MascotMood.Thinking:
                g.DrawArc(ink, cx - er * 0.7f, my - er * 0.9f, er * 1.4f, er * 1.2f, 30, 120); break;
        }

        // extras floating around the head
        float hx = body.Right - bw * 0.02f, hy = body.Y;
        switch (mood)
        {
            case MascotMood.Sleeping:
            {
                bool big = ((int)t) % 2 == 0;  // the pill repaints sleeping once a second: the "z" drifts
                using var f = new Font("Segoe UI", Math.Max(5f, s * (big ? 0.26f : 0.2f)), FontStyle.Bold, GraphicsUnit.Pixel);
                using var zb = new SolidBrush(Color.FromArgb(170, 200, 200, 210));
                g.DrawString("z", f, zb, hx - s * 0.04f, hy - s * (big ? 0.30f : 0.22f));
                break;
            }
            case MascotMood.Thinking:
                for (int i = 0; i < 3; i++)
                {
                    float a = (float)Math.Clamp((t * 1.6 % 3) - i * 0.6, 0, 1);   // dots appear one by one
                    using var d = new SolidBrush(Color.FromArgb((int)(230 * a), 230, 230, 236));
                    float rr = s * (0.035f + i * 0.012f);
                    g.FillEllipse(d, hx + i * s * 0.06f - rr, hy - i * s * 0.07f - rr, rr * 2, rr * 2);
                }
                break;
            case MascotMood.Asking:
            {
                float br = s * 0.17f, bx = hx + s * 0.02f, by = hy - s * 0.02f;
                using (var bb = new SolidBrush(IconRenderer.Amber)) g.FillEllipse(bb, bx - br, by - br, br * 2, br * 2);
                using var ip = new Pen(Ink, Math.Max(1f, s * 0.05f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(ip, bx, by - br * 0.5f, bx, by + br * 0.12f);
                using (var dot = new SolidBrush(Ink)) g.FillEllipse(dot, bx - br * 0.13f, by + br * 0.36f, br * 0.26f, br * 0.26f);
                break;
            }
            case MascotMood.Celebrating:
                for (int i = 0; i < 3; i++)
                {
                    float tw = 0.5f + 0.5f * (float)Math.Sin(t * 8 + i * 2.1);
                    var (sx, sy) = i switch { 0 => (body.X - s * 0.08f, body.Y + s * 0.02f), 1 => (hx + s * 0.06f, hy - s * 0.06f), _ => (cx - s * 0.18f, hy - s * 0.2f) };
                    Star(g, sx, sy, s * 0.07f * tw + s * 0.02f, i == 1 ? Glow : Color.White, 0);
                }
                break;
        }
        g.SmoothingMode = oldSmooth;
    }

    // A gumdrop: round top, slightly flattened bottom.
    static GraphicsPath Gumdrop(RectangleF r)
    {
        var p = new GraphicsPath();
        float k = r.Height * 0.28f;
        p.AddBezier(r.X, r.Bottom - k, r.X, r.Y - r.Height * 0.1f, r.Right, r.Y - r.Height * 0.1f, r.Right, r.Bottom - k);
        p.AddBezier(r.Right, r.Bottom - k, r.Right, r.Bottom + k * 0.15f, r.X, r.Bottom + k * 0.15f, r.X, r.Bottom - k);
        p.CloseFigure();
        return p;
    }

    // A 4-point sparkle.
    static void Star(Graphics g, float x, float y, float r, Color c, float rotDeg)
    {
        if (r <= 0.3f) return;
        var pts = new PointF[8];
        for (int i = 0; i < 8; i++)
        {
            double a = (rotDeg + i * 45) * Math.PI / 180;
            float rr = i % 2 == 0 ? r : r * 0.38f;
            pts[i] = new PointF(x + rr * (float)Math.Cos(a), y + rr * (float)Math.Sin(a));
        }
        using var b = new SolidBrush(c);
        g.FillPolygon(b, pts);
    }

    static Color Lerp(Color a, Color b, float u) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * u), (int)(a.G + (b.G - a.G) * u), (int)(a.B + (b.B - a.B) * u));
}

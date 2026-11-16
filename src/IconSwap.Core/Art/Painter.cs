using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;


namespace IconSwap.Core.Art;

/// <summary>
/// Supersampled vector drawing in output-pixel coordinates. The result is box-filtered in
/// premultiplied space, which keeps clean anti-aliased edges on a transparent background.
/// </summary>
public sealed class Painter : IDisposable
{
    public const int S = 4;
    public const string FontFamily = "Segoe UI";

    private readonly Bitmap _bmp;
    public Graphics G { get; }
    public int Width { get; }
    public int Height { get; }

    public Painter(int width, int height)
    {
        Width = width; Height = height;
        _bmp = new Bitmap(width * S, height * S, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        G = Graphics.FromImage(_bmp);
        G.SmoothingMode = SmoothingMode.AntiAlias;
        G.PixelOffsetMode = PixelOffsetMode.HighQuality;
        G.TextRenderingHint = TextRenderingHint.AntiAlias;
        G.CompositingQuality = CompositingQuality.HighQuality;
        G.ScaleTransform(S, S);
    }

    public void Dispose() { G.Dispose(); _bmp.Dispose(); }

    // ------------------------------------------------------------------ primitives

    public void FillCircle(Color c, float cx, float cy, float r)
    {
        using var b = new SolidBrush(c);
        G.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
    }

    public void StrokeCircle(Color c, float cx, float cy, float r, float width)
    {
        using var p = new Pen(c, width);
        G.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2);
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.01f) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Fill(Color c, GraphicsPath path)
    {
        using var b = new SolidBrush(c);
        G.FillPath(b, path);
    }

    public void Stroke(Color c, float width, GraphicsPath path)
    {
        using var p = new Pen(c, width) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        G.DrawPath(p, path);
    }

    /// <summary>
    /// Draws text whose ink (not the font box) is centred in <paramref name="box"/>, scaled so
    /// its ink height equals <paramref name="inkHeight"/> (shrunk if it would be too wide).
    /// </summary>
    public void Text(string text, Color c, RectangleF box, float inkHeight, FontStyle style = FontStyle.Bold, float squeeze = 1f)
    {
        using var path = TextPath(text, style);
        var ink = path.GetBounds();
        float scale = inkHeight / ink.Height;
        if (ink.Width * scale * squeeze > box.Width) scale = box.Width / (ink.Width * squeeze);
        using var m = new Matrix();
        m.Translate(box.X + box.Width / 2, box.Y + box.Height / 2);
        m.Scale(scale * squeeze, scale);
        m.Translate(-(ink.X + ink.Width / 2), -(ink.Y + ink.Height / 2));
        path.Transform(m);
        Fill(c, path);
    }

    public static float TextWidth(string text, float inkHeight, FontStyle style = FontStyle.Bold)
    {
        using var path = TextPath(text, style);
        var ink = path.GetBounds();
        return ink.Width * inkHeight / ink.Height;
    }

    private static GraphicsPath TextPath(string text, FontStyle style)
    {
        var path = new GraphicsPath();
        using var ff = new FontFamily(FontFamily);
        path.AddString(text, ff, (int)style, 100f, PointF.Empty, StringFormat.GenericTypographic);
        return path;
    }

    // ------------------------------------------------------------------ Xbox glyphs

    /// <summary>Xbox "Menu" button glyph: three horizontal bars.</summary>
    public void MenuGlyph(Color c, RectangleF box)
    {
        float s = Math.Min(box.Width, box.Height);
        float x0 = box.X + (box.Width - s) / 2, y0 = box.Y + (box.Height - s) / 2;
        float bar = s * 0.17f;
        using var p = new Pen(c, bar) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (int i = 0; i < 3; i++)
        {
            float y = y0 + s * (0.18f + 0.32f * i);
            G.DrawLine(p, x0 + s * 0.12f, y, x0 + s * 0.88f, y);
        }
    }

    /// <summary>Xbox "View" button glyph: two overlapping windows.</summary>
    public void ViewGlyph(Color c, Color background, RectangleF box)
    {
        float s = Math.Min(box.Width, box.Height);
        float x0 = box.X + (box.Width - s) / 2, y0 = box.Y + (box.Height - s) / 2;
        float w = s * 0.13f;
        var back = new RectangleF(x0 + s * 0.06f, y0 + s * 0.06f, s * 0.6f, s * 0.6f);
        var front = new RectangleF(x0 + s * 0.34f, y0 + s * 0.34f, s * 0.6f, s * 0.6f);
        using (var path = RoundRect(back, s * 0.08f)) Stroke(c, w, path);
        using (var path = RoundRect(front, s * 0.08f)) { Fill(background, path); Stroke(c, w, path); }
    }

    /// <summary>The Xbox sphere logo: a filled disc with an X cut by two curved strokes.</summary>
    public void XboxLogo(Color sphere, Color cut, float cx, float cy, float r)
    {
        FillCircle(sphere, cx, cy, r);
        float w = r * 0.30f;
        using var p = new Pen(cut, w) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
        var state = G.Save();
        G.SetClip(new RectangleF(cx - r, cy - r, r * 2, r * 2));
        using (var clip = new GraphicsPath())
        {
            clip.AddEllipse(cx - r, cy - r, r * 2, r * 2);
            G.SetClip(clip);
        }
        // Each arm bows outward, like the logo's "sphere with an X".
        G.DrawBezier(p, cx - r * 0.95f, cy - r * 0.95f, cx - r * 0.05f, cy - r * 0.30f, cx + r * 0.05f, cy + r * 0.30f, cx + r * 0.95f, cy + r * 0.95f);
        G.DrawBezier(p, cx + r * 0.95f, cy - r * 0.95f, cx + r * 0.05f, cy - r * 0.30f, cx - r * 0.05f, cy + r * 0.30f, cx - r * 0.95f, cy + r * 0.95f);
        G.Restore(state);
        // Thin ring: the logo sphere edge.
        StrokeCircle(sphere, cx, cy, r - r * 0.04f, r * 0.08f);
    }

    /// <summary>Line drawing of an Xbox One controller fitted into <paramref name="box"/>.</summary>
    public void XboxController(Color c, RectangleF box, float stroke)
    {
        // Design space 100 x 66.
        const float DW = 100, DH = 66;
        float scale = Math.Min(box.Width / DW, box.Height / DH);
        float ox = box.X + (box.Width - DW * scale) / 2, oy = box.Y + (box.Height - DH * scale) / 2;
        PointF P(float x, float y) => new(ox + x * scale, oy + y * scale);

        using var body = new GraphicsPath();
        // right half, top centre -> bottom centre
        body.AddBezier(P(50, 7), P(62, 7), P(72, 3), P(81, 7));
        body.AddBezier(P(81, 7), P(91, 11), P(96, 24), P(98, 40));
        body.AddBezier(P(98, 40), P(100, 55), P(98, 64), P(89, 64));
        body.AddBezier(P(89, 64), P(81, 64), P(76, 54), P(70, 48));
        body.AddBezier(P(70, 48), P(64, 43), P(57, 44), P(50, 44));
        // left half, bottom centre -> top centre
        body.AddBezier(P(50, 44), P(43, 44), P(36, 43), P(30, 48));
        body.AddBezier(P(30, 48), P(24, 54), P(19, 64), P(11, 64));
        body.AddBezier(P(11, 64), P(2, 64), P(0, 55), P(2, 40));
        body.AddBezier(P(2, 40), P(4, 24), P(9, 11), P(19, 7));
        body.AddBezier(P(19, 7), P(28, 3), P(38, 7), P(50, 7));
        body.CloseFigure();
        Stroke(c, stroke, body);

        void Circle(float x, float y, float r)
        {
            using var g = new GraphicsPath();
            var p = P(x, y);
            g.AddEllipse(p.X - r * scale, p.Y - r * scale, r * 2 * scale, r * 2 * scale);
            Stroke(c, stroke, g);
        }

        Circle(27, 23, 7.5f); Circle(27, 23, 4.2f);      // left stick
        Circle(63, 37, 7.5f); Circle(63, 37, 4.2f);      // right stick
        Circle(50, 15, 4.2f);                             // guide
        Circle(42.5f, 24, 1.8f); Circle(57.5f, 24, 1.8f); // view / menu
        foreach (var (x, y) in new[] { (74f, 17f), (74f, 29f), (68f, 23f), (80f, 23f) }) Circle(x, y, 2.8f); // ABXY

        using var dpad = new GraphicsPath();                // d-pad cross outline
        float cx = 37, cy = 37, a = 2.6f, l = 7f;
        dpad.AddPolygon(new[]
        {
            P(cx - a, cy - l), P(cx + a, cy - l), P(cx + a, cy - a), P(cx + l, cy - a), P(cx + l, cy + a), P(cx + a, cy + a),
            P(cx + a, cy + l), P(cx - a, cy + l), P(cx - a, cy + a), P(cx - l, cy + a), P(cx - l, cy - a), P(cx - a, cy - a),
        });
        Stroke(c, stroke, dpad);
    }

    // ------------------------------------------------------------------ output

    /// <summary>Downsamples the drawing (premultiplied box filter).</summary>
    public RgbaImage Result()
    {
        var big = RgbaImage.FromBitmap(_bmp);
        var img = new RgbaImage(Width, Height);
        int bw = big.Width;
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            double r = 0, g = 0, b = 0, a = 0;
            for (int sy = 0; sy < S; sy++)
            for (int sx = 0; sx < S; sx++)
            {
                int o = ((y * S + sy) * bw + x * S + sx) * 4;
                double al = big.Pixels[o + 3];
                r += big.Pixels[o] * al; g += big.Pixels[o + 1] * al; b += big.Pixels[o + 2] * al; a += al;
            }
            int d = (y * Width + x) * 4;
            if (a > 0)
            {
                img.Pixels[d] = (byte)Math.Round(r / a);
                img.Pixels[d + 1] = (byte)Math.Round(g / a);
                img.Pixels[d + 2] = (byte)Math.Round(b / a);
            }
            img.Pixels[d + 3] = (byte)Math.Round(a / (S * S));
        }
        return img;
    }
}

public static class ImageOps
{
    public static int Lum(byte[] p, int o) => (p[o] * 299 + p[o + 1] * 587 + p[o + 2] * 114) / 1000;

    /// <summary>Bounding box of pixels with alpha above the threshold.</summary>
    public static Rectangle AlphaBounds(RgbaImage img, int threshold = 64)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < img.Height; y++)
        for (int x = 0; x < img.Width; x++)
            if (img.Pixels[(y * img.Width + x) * 4 + 3] > threshold)
            {
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
            }
        return x1 < 0 ? new Rectangle(0, 0, img.Width, img.Height) : Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
    }

    /// <summary>Average colour of opaque pixels that pass the luminance filter.</summary>
    public static Color AverageColor(RgbaImage img, Func<int, bool> lumFilter, Rectangle? area = null, int minAlpha = 200)
    {
        var a = area ?? new Rectangle(0, 0, img.Width, img.Height);
        long r = 0, g = 0, b = 0, n = 0;
        for (int y = a.Top; y < a.Bottom; y++)
        for (int x = a.Left; x < a.Right; x++)
        {
            int o = (y * img.Width + x) * 4;
            if (img.Pixels[o + 3] < minAlpha || !lumFilter(Lum(img.Pixels, o))) continue;
            r += img.Pixels[o]; g += img.Pixels[o + 1]; b += img.Pixels[o + 2]; n++;
        }
        return n == 0 ? Color.Empty : Color.FromArgb(255, (int)(r / n), (int)(g / n), (int)(b / n));
    }

    /// <summary>Straight-alpha "over" composition of <paramref name="top"/> onto <paramref name="bottom"/>.</summary>
    public static RgbaImage Over(RgbaImage top, RgbaImage bottom)
    {
        var o = new RgbaImage(bottom.Width, bottom.Height);
        for (int i = 0; i < o.Pixels.Length; i += 4)
        {
            double ta = top.Pixels[i + 3] / 255.0, ba = bottom.Pixels[i + 3] / 255.0;
            double a = ta + ba * (1 - ta);
            for (int c = 0; c < 3; c++)
                o.Pixels[i + c] = a <= 0 ? (byte)0 : (byte)Math.Round((top.Pixels[i + c] * ta + bottom.Pixels[i + c] * ba * (1 - ta)) / a);
            o.Pixels[i + 3] = (byte)Math.Round(a * 255);
        }
        return o;
    }
}

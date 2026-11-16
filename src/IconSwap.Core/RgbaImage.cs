using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace IconSwap.Core;

/// <summary>Straight-alpha RGBA8 image (byte order R,G,B,A).</summary>
public sealed class RgbaImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public RgbaImage(int w, int h) { Width = w; Height = h; Pixels = new byte[w * h * 4]; }
    public RgbaImage(int w, int h, byte[] px) { Width = w; Height = h; Pixels = px; }

    public static RgbaImage FromBitmap(Bitmap src)
    {
        using var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
        }
        var img = new RgbaImage(bmp.Width, bmp.Height);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[bmp.Width * 4];
            for (int y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < bmp.Width; x++)
                {
                    int o = (y * bmp.Width + x) * 4;
                    img.Pixels[o] = row[x * 4 + 2];
                    img.Pixels[o + 1] = row[x * 4 + 1];
                    img.Pixels[o + 2] = row[x * 4];
                    img.Pixels[o + 3] = row[x * 4 + 3];
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return img;
    }

    public Bitmap ToBitmap()
    {
        var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[Width * 4];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int o = (y * Width + x) * 4;
                    row[x * 4] = Pixels[o + 2];
                    row[x * 4 + 1] = Pixels[o + 1];
                    row[x * 4 + 2] = Pixels[o];
                    row[x * 4 + 3] = Pixels[o + 3];
                }
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }

    public static RgbaImage Load(string path)
    {
        // Read through a memory stream so the file is not kept locked by GDI+.
        using var ms = new MemoryStream(File.ReadAllBytes(path));
        using var bmp = new Bitmap(ms);
        return FromBitmap(bmp);
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var bmp = ToBitmap();
        bmp.Save(path, ImageFormat.Png);
    }

    public RgbaImage Resize(int w, int h)
    {
        if (w == Width && h == Height) return this;
        using var src = ToBitmap();
        using var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingMode = CompositingMode.SourceCopy;
            using var attr = new ImageAttributes();
            attr.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, Width, Height, GraphicsUnit.Pixel, attr);
        }
        return FromBitmap(dst);
    }

    /// <summary>
    /// Gives fully transparent pixels the colour of the nearest visible pixel, so bilinear
    /// filtering of a straight-alpha texture does not pull dark fringes into the edges.
    /// </summary>
    public RgbaImage BleedTransparentColors()
    {
        var px = (byte[])Pixels.Clone();
        var known = new bool[Width * Height];
        var frontier = new List<int>();
        for (int i = 0; i < known.Length; i++)
        {
            if (px[i * 4 + 3] != 0) known[i] = true;
        }
        bool any = known.Any(k => k);
        if (!any) return new RgbaImage(Width, Height, px);
        bool changed = true;
        while (changed)
        {
            changed = false;
            frontier.Clear();
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                if (known[i]) continue;
                int r = 0, g = 0, b = 0, n = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                    int j = ny * Width + nx;
                    if (!known[j]) continue;
                    r += px[j * 4]; g += px[j * 4 + 1]; b += px[j * 4 + 2]; n++;
                }
                if (n == 0) continue;
                px[i * 4] = (byte)(r / n); px[i * 4 + 1] = (byte)(g / n); px[i * 4 + 2] = (byte)(b / n);
                frontier.Add(i);
            }
            foreach (var i in frontier) known[i] = true;
            changed = frontier.Count > 0;
        }
        return new RgbaImage(Width, Height, px);
    }

    /// <summary>Composites over a solid colour (for previews).</summary>
    public RgbaImage Flatten(byte r, byte g, byte b)
    {
        var px = new byte[Pixels.Length];
        for (int i = 0; i < px.Length; i += 4)
        {
            int a = Pixels[i + 3];
            px[i] = (byte)((Pixels[i] * a + r * (255 - a) + 127) / 255);
            px[i + 1] = (byte)((Pixels[i + 1] * a + g * (255 - a) + 127) / 255);
            px[i + 2] = (byte)((Pixels[i + 2] * a + b * (255 - a) + 127) / 255);
            px[i + 3] = 255;
        }
        return new RgbaImage(Width, Height, px);
    }
}

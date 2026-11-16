using System.Drawing;
using IconSwap.Core;
using IconSwap.Core.Art;

namespace IconBuilder;

internal static class Preview
{
    /// <summary>Side-by-side sheet: original | Xbox, on a grey background.</summary>
    public static void Write(EbArchive eb, string prefix, List<(string Rel, RgbaImage Img)> made, string dir)
    {
        Directory.CreateDirectory(dir);
        const int cell = 96, cols = 4;
        int rows = (made.Count + cols - 1) / cols;
        using var sheet = new Bitmap(cols * cell * 2 + cols * 8, rows * cell, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(70, 70, 70));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        for (int i = 0; i < made.Count; i++)
        {
            var (rel, img) = made[i];
            var orig = GnfTexture.Parse(eb.Read(eb.Find(prefix + rel + ".gnf")!)).Decode();
            int x = (i % cols) * (cell * 2 + 8), y = (i / cols) * cell;
            foreach (var (im, dx) in new[] { (orig, 0), (img, cell) })
            {
                float s = Math.Min(1f * (cell - 6) / im.Width, 1f * (cell - 6) / im.Height);
                s = s >= 1 ? (float)Math.Floor(s) : s;
                using var bmp = im.ToBitmap();
                g.DrawImage(bmp, x + dx + (cell - im.Width * s) / 2, y + (cell - im.Height * s) / 2, im.Width * s, im.Height * s);
            }
        }
        sheet.Save(Path.Combine(dir, "preview.png"), System.Drawing.Imaging.ImageFormat.Png);
    }
}

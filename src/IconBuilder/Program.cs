using System.Diagnostics;
using IconSwap.Core;
using IconSwap.Core.Art;

namespace IconBuilder;

/// <summary>
/// Developer tool: extracts the PS4 button textures of a game, draws the Xbox versions and
/// writes them as PNG files into the patcher's Icons folder.
///   IconBuilder selftest &lt;game&gt;
///   IconBuilder extract  &lt;game&gt; &lt;outDir&gt;
///   IconBuilder build    &lt;game&gt; &lt;profileDir&gt; [previewDir]
/// </summary>
internal static class Program
{
    private const string Archive = "databigs/inxp_ui_40.big";
    private const string Prefix = "samplegame/ui/kettle_assets/";

    private static int Main(string[] args)
    {
        try
        {
            switch (args.FirstOrDefault())
            {
                case "selftest": return SelfTest(args[1]);
                case "diag": Diag.Run(args[1], args[2]); return 0;
                case "swftest": return SwfTest.Run(args[1]);
                case "patchtest": return PatchTest.Run(args[1], args[2]);
                case "icon": return MakeIcon(args[1]);
                case "zoom": Zoom.Run(args[1], int.Parse(args[2]), args.Skip(3)); return 0;
                case "diag2": Diag2.Run(args[1], args[2], args[3]); return 0;
                case "extract": return Extract(args[1], args[2]);
                case "build": return Build(args[1], args[2], args.Length > 3 ? args[3] : null);
                default:
                    Console.WriteLine("usage: IconBuilder selftest|extract|build <game> [out]");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static IEnumerable<EbEntry> ButtonEntries(EbArchive eb) =>
        eb.Entries.Where(e => e.Name.EndsWith(".gnf") &&
                              (e.FullPath.Contains("/skins/buttons/") || e.FullPath.Contains("/career/training/buttonicons/") ||
                               e.FullPath.Contains("/skins/settings/controller/")));

    private static int Extract(string game, string outDir)
    {
        var eb = ArchivePatcher.OpenOriginal(Path.Combine(game, Archive));
        foreach (var e in ButtonEntries(eb))
        {
            var img = GnfTexture.Parse(eb.Read(e)).Decode();
            img.Save(Path.Combine(outDir, e.FullPath[Prefix.Length..^4] + ".png"));
        }
        return 0;
    }

    private static int SelfTest(string game)
    {
        var eb = ArchivePatcher.OpenOriginal(Path.Combine(game, Archive));
        var enc = new Bc7Encoder();
        int count = 0; double worst = 99; string worstName = "";
        var sw = Stopwatch.StartNew();
        foreach (var e in ButtonEntries(eb))
        {
            var raw = eb.ReadRaw(e);
            var file = ChunkZip.Unpack(raw);
            if (ChunkZip.IsChunkZip(raw))
            {
                // chunkzip round trip
                var again = ChunkZip.Unpack(ChunkZip.Pack(file));
                if (!again.AsSpan().SequenceEqual(file)) throw new Exception("chunkzip round trip failed: " + e.FullPath);
            }
            var gnf = GnfTexture.Parse(file);
            if (!gnf.IsSupported(out var why)) { Console.WriteLine($"skip {e.FullPath}: {why}"); continue; }
            var img = gnf.Decode();
            // Re-encode the decoded image and decode it again: measures encoder quality and
            // proves the tiling layout is self-consistent with the original file size.
            var re = GnfTexture.Parse(gnf.Encode(img, enc));
            if (re.Data.Length != gnf.Data.Length) throw new Exception("size changed: " + e.FullPath);
            var back = re.Decode();
            double psnr = Psnr(img, back);
            if (psnr < worst) { worst = psnr; worstName = e.FullPath; }
            count++;
        }
        Console.WriteLine($"{count} textures re-encoded in {sw.Elapsed.TotalSeconds:F1}s, worst PSNR {worst:F2} dB ({worstName})");
        return 0;
    }

    internal static double Psnr(RgbaImage a, RgbaImage b)
    {
        double se = 0;
        // Colour error is weighted by visibility: the colour of (nearly) transparent texels
        // does not show, and the encoder bleeds neighbouring colours into them.
        for (int i = 0; i < a.Pixels.Length; i += 4)
        {
            // Premultiplied comparison = what is actually composited on screen.
            for (int c = 0; c < 4; c++)
            {
                double d = c < 3
                    ? (a.Pixels[i + c] * a.Pixels[i + 3] - b.Pixels[i + c] * b.Pixels[i + 3]) / 255.0
                    : a.Pixels[i + c] - b.Pixels[i + c];
                se += d * d;
            }
        }
        double mse = se / a.Pixels.Length;
        return mse == 0 ? 99 : 10 * Math.Log10(255.0 * 255.0 / mse);
    }

    private static int Build(string game, string profileDir, string? previewDir)
    {
        var eb = ArchivePatcher.OpenOriginal(Path.Combine(game, Archive));
        var art = new XboxArt(eb, Prefix);
        var outRoot = Path.Combine(profileDir, "inxp_ui_40");
        if (Directory.Exists(outRoot)) Directory.Delete(outRoot, true);
        var made = art.RenderAll();
        foreach (var (rel, img) in made)
            img.Save(Path.Combine(outRoot, rel + ".png"));
        Console.WriteLine($"{made.Count} icons written to {outRoot}");
        if (previewDir != null) Preview.Write(eb, Prefix, made, previewDir);
        return 0;
    }


    /// <summary>Application icon: an Xbox controller outline on a green tile.</summary>
    internal static int MakeIcon(string path)
    {
        var sizes = new[] { 256, 64, 48, 32, 16 };
        var pngs = new List<byte[]>();
        foreach (int s in sizes)
        {
            using var p = new Painter(s, s);
            using (var tile = Painter.RoundRect(new System.Drawing.RectangleF(0.5f, 0.5f, s - 1, s - 1), s * 0.2f))
                p.Fill(System.Drawing.Color.FromArgb(16, 124, 16), tile);
            float m = s * 0.12f;
            p.XboxController(System.Drawing.Color.White, new System.Drawing.RectangleF(m, m, s - 2 * m, s - 2 * m), Math.Max(1f, s / 22f));
            using var bmp = p.Result().ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngs.Add(ms.ToArray());
        }
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
            w.Write(pngs[i].Length); w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs) w.Write(png);
        return 0;
    }
}

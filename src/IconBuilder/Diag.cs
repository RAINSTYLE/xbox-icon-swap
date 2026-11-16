using IconSwap.Core;
using IconSwap.Core.Art;
namespace IconBuilder;
internal static class Diag
{
    public static void Run(string game, string path)
    {
        var eb = EbArchive.Open(Path.Combine(game, "databigs/inxp_ui_40.big"));
        var e = eb.Find(path)!;
        var img = GnfTexture.Parse(eb.Read(e)).Decode().BleedTransparentColors();
        foreach (int? mode in new int?[] { 6, 5, 7, 1, null })
        {
            var enc = new Bc7Encoder { ForceMode = mode };
            long pred = 0, real = 0; int bad = 0;
            var blk = new byte[64]; var outb = new byte[16]; var dec = new byte[64];
            for (int by = 0; by < (img.Height + 3) / 4; by++)
            for (int bx = 0; bx < (img.Width + 3) / 4; bx++)
            {
                for (int i = 0; i < 16; i++)
                {
                    int x = Math.Min(bx * 4 + (i & 3), img.Width - 1), y = Math.Min(by * 4 + (i >> 2), img.Height - 1);
                    Buffer.BlockCopy(img.Pixels, (y * img.Width + x) * 4, blk, i * 4, 4);
                }
                enc.EncodeBlock(blk, outb);
                Bc7.DecodeBlock(outb, dec);
                long r = 0;
                for (int i = 0; i < 64; i++) { long d = dec[i] - blk[i]; r += d * d; }
                if (enc.LastPredictedError != long.MaxValue && r != enc.LastPredictedError) bad++;
                pred += enc.LastPredictedError == long.MaxValue ? 0 : enc.LastPredictedError; real += r;
            }
            Console.WriteLine($"mode {mode?.ToString() ?? "auto"}: predicted {pred}, real {real}, mismatching blocks {bad}");
        }
    }
}
internal static class Diag2
{
    public static void Run(string game, string path, string outDir)
    {
        var eb = EbArchive.Open(Path.Combine(game, "databigs/inxp_ui_40.big"));
        var gnf = GnfTexture.Parse(eb.Read(eb.Find(path)!));
        Console.WriteLine($"{gnf.Width}x{gnf.Height} pitch {gnf.Pitch} data {gnf.Data.Length}");
        var img = gnf.Decode();
        var re = GnfTexture.Parse(gnf.Encode(img, new Bc7Encoder()));
        var back = re.Decode();
        Console.WriteLine($"psnr {Program.Psnr(img, back):F2}");
        img.Flatten(60,60,60).Resize(img.Width*6,img.Height*6).Save(Path.Combine(outDir,"d_orig.png"));
        back.Flatten(60,60,60).Resize(img.Width*6,img.Height*6).Save(Path.Combine(outDir,"d_back.png"));
        int diff=0; for (int i=0;i<img.Pixels.Length;i+=4) if (Math.Abs(img.Pixels[i+3]-back.Pixels[i+3])>40) diff++;
        Console.WriteLine($"alpha-diff pixels {diff}");
    }
}
internal static class Zoom
{
    public static void Run(string output, int z, IEnumerable<string> pngs)
    {
        var imgs = pngs.Select(RgbaImage.Load).ToList();
        int w = imgs.Sum(i => i.Width * z + 10), h = imgs.Max(i => i.Height * z);
        var sheet = new RgbaImage(w, h);
        int x0 = 0;
        foreach (var im in imgs)
        {
            var f = im.Flatten(90, 90, 90);
            for (int y = 0; y < im.Height * z; y++)
            for (int x = 0; x < im.Width * z; x++)
                Buffer.BlockCopy(f.Pixels, ((y / z) * im.Width + x / z) * 4, sheet.Pixels, (y * w + x0 + x) * 4, 4);
            x0 += im.Width * z + 10;
        }
        sheet.Save(output);
    }
}
internal static class PatchTest
{
    public static int Run(string archive, string profileDir)
    {
        string Sha(string p) { using var s = File.OpenRead(p); return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s)); }
        var origHash = Sha(archive); long origLen = new FileInfo(archive).Length;
        var profile = GameProfile.LoadAll(Path.GetDirectoryName(Path.GetFullPath(profileDir))!).First(p => Path.GetFullPath(p.Folder) == Path.GetFullPath(profileDir));
        var spec = profile.Archives[0];
        var reps = profile.Replacements(spec).Select(r => (r.ArchivePath, RgbaImage.Load(r.Png))).ToList();

        var before = EbArchive.Open(archive);
        var rawBefore = before.Entries.ToDictionary(e => e.Index, e => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(before.ReadRaw(e))));

        for (int round = 1; round <= 2; round++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = ArchivePatcher.Patch(archive, reps, Console.WriteLine);
            Console.WriteLine($"round {round}: replaced {res.Replaced}, warnings {res.Warnings.Count}, {sw.Elapsed.TotalSeconds:F1}s, length {new FileInfo(archive).Length - origLen:+#;-#;0} bytes");
            foreach (var w in res.Warnings) Console.WriteLine("  W: " + w);
        }
        var after = EbArchive.Open(archive);
        var replaced = new HashSet<string>(reps.Select(r => r.ArchivePath), StringComparer.OrdinalIgnoreCase);
        int same = 0, changed = 0; double worst = 99;
        foreach (var e in after.Entries)
        {
            if (replaced.Contains(e.FullPath))
            {
                var img = GnfTexture.Parse(after.Read(e)).Decode();
                var want = reps.First(r => string.Equals(r.ArchivePath, e.FullPath, StringComparison.OrdinalIgnoreCase)).Item2;
                worst = Math.Min(worst, Program.Psnr(want, img));
                changed++;
            }
            else if (Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(after.ReadRaw(e))) == rawBefore[e.Index]) same++;
            else Console.WriteLine("UNEXPECTED CHANGE: " + e.FullPath);
        }
        Console.WriteLine($"replaced entries {changed} (worst PSNR {worst:F1} dB), untouched entries identical: {same}/{after.Entries.Count - changed}");
        ArchivePatcher.Restore(archive);
        bool ok = Sha(archive) == origHash && !ArchivePatcher.IsPatched(archive);
        Console.WriteLine(ok ? "RESTORE OK: file is byte-identical to the original" : "RESTORE MISMATCH");
        return ok ? 0 : 1;
    }
}
internal static class SwfTest
{
    public static int Run(string swfPath)
    {
        var orig = File.ReadAllBytes(swfPath);
        var swapped = SwfSymbolSwap.Apply(orig, "_ps_", "_xb_", out int pairs);
        var back = SwfSymbolSwap.Apply(swapped, "_ps_", "_xb_", out _);
        Console.WriteLine($"{Path.GetFileName(swfPath)}: pairs {pairs}, size {orig.Length}->{swapped.Length}");
        // swapping twice must give the original content back (compare decompressed bodies)
        static byte[] Body(byte[] s) { using var z = new System.IO.Compression.ZLibStream(new MemoryStream(s, 8, s.Length - 8), System.IO.Compression.CompressionMode.Decompress); var m = new MemoryStream(); z.CopyTo(m); return m.ToArray(); }
        var a = Body(orig); var b = Body(swapped); var c = Body(back);
        int diff = 0; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) diff++;
        Console.WriteLine($"  bodies {a.Length}/{b.Length} bytes, changed bytes {diff}, double swap identical: {a.AsSpan().SequenceEqual(c)}");
        return 0;
    }
}

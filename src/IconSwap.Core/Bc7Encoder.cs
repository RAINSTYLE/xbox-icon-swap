namespace IconSwap.Core;

/// <summary>
/// BC7 block encoder for UI artwork. Tries mode 6 (one RGBA subset), mode 5 (RGB + separate
/// alpha), mode 7 (two RGBA subsets) and, for opaque blocks, mode 1 (two RGB subsets), each
/// with PCA/bounding-box starts and least-squares endpoint refinement, and keeps the result
/// with the lowest squared error.
/// </summary>
public sealed class Bc7Encoder
{
    /// <summary>How many of the best-estimated partitions get a full refinement.</summary>
    public int PartitionCandidates { get; init; } = 6;

    /// <summary>Diagnostics: only this mode (1, 5, 6 or 7) is tried.</summary>
    public int? ForceMode { get; init; }

    /// <summary>Diagnostics: squared error the encoder expected for the last block.</summary>
    public long LastPredictedError { get; private set; }

    private sealed class Fit
    {
        public int[,] Q = new int[2, 4];   // stored endpoint values (without p-bit)
        public int[] P = new int[2];       // p-bit per endpoint (shared mode: P[0]==P[1])
        public int[] Idx = new int[16];
        public long Err;
    }

    private sealed class Candidate
    {
        public int Mode, Partition;
        public Fit[] Sub = Array.Empty<Fit>();
        public Fit? Alpha;                 // mode 5 only
        public long Err;
    }

    public void EncodeBlock(ReadOnlySpan<byte> rgba, Span<byte> output)
    {
        var px = new int[64];
        bool opaque = true;
        for (int i = 0; i < 64; i++)
        {
            px[i] = rgba[i];
            if ((i & 3) == 3 && rgba[i] != 255) opaque = false;
        }

        var all = new bool[16];
        Array.Fill(all, true);
        var chRgba = new[] { 0, 1, 2, 3 };
        var chRgb = new[] { 0, 1, 2 };
        var chA = new[] { 3 };

        // Mode 6
        var best = new Candidate { Mode = 6, Sub = new[] { FitSubset(px, all, chRgba, 7, 1, 4, true) } };
        best.Err = best.Sub[0].Err;
        if (ForceMode is int fm && fm != 6) best.Err = long.MaxValue;
        if (best.Err <= GoodEnough && ForceMode is null) { Finish(best, output); return; }

        // Mode 5
        if (ForceMode is null or 5)
        {
            var c = FitSubset(px, all, chRgb, 7, 0, 2, true);
            var a = FitSubset(px, all, chA, 8, 0, 2, true);
            long err = c.Err + a.Err;
            if (err < best.Err) best = new Candidate { Mode = 5, Sub = new[] { c }, Alpha = a, Err = err };
        }

        // Two-subset modes
        foreach (int mode in opaque ? new[] { 7, 1 } : new[] { 7 })
        {
            if (ForceMode is int f && f != mode) continue;
            if (best.Err <= GoodEnough * 4) break;
            int[] ch = mode == 7 ? chRgba : chRgb;
            int bits = mode == 7 ? 5 : 6, pmode = mode == 7 ? 1 : 2, ib = mode == 7 ? 2 : 3;
            var estimates = new List<(long err, int part)>(64);
            for (int part = 0; part < 64; part++)
            {
                var m0 = Masks[part, 0];
                var m1 = Masks[part, 1];
                long e = FitSubset(px, m0, ch, bits, pmode, ib, false).Err + FitSubset(px, m1, ch, bits, pmode, ib, false).Err;
                estimates.Add((e, part));
            }
            estimates.Sort((a, b) => a.err.CompareTo(b.err));
            for (int k = 0; k < Math.Min(PartitionCandidates, estimates.Count); k++)
            {
                int part = estimates[k].part;
                var s0 = FitSubset(px, Masks[part, 0], ch, bits, pmode, ib, true);
                var s1 = FitSubset(px, Masks[part, 1], ch, bits, pmode, ib, true);
                long err = s0.Err + s1.Err;
                if (err < best.Err) best = new Candidate { Mode = mode, Partition = part, Sub = new[] { s0, s1 }, Err = err };
            }
        }
        Finish(best, output);
    }

    private void Finish(Candidate best, Span<byte> output)
    {
        LastPredictedError = best.Err;
        Pack(best, output);
    }

    /// <summary>Squared block error (sum over 64 values) treated as visually lossless.</summary>
    private const long GoodEnough = 16;

    private static readonly bool[,][] Masks = BuildMasks();

    private static bool[,][] BuildMasks()
    {
        var masks = new bool[64, 2][];
        for (int part = 0; part < 64; part++)
            for (int s = 0; s < 2; s++)
            {
                var m = new bool[16];
                for (int i = 0; i < 16; i++) m[i] = Bc7.Subset(2, part, i) == s;
                masks[part, s] = m;
            }
        return masks;
    }

    // ---------------------------------------------------------------- fitting

    /// <param name="pmode">0 = no p-bit, 1 = one per endpoint, 2 = one shared by both endpoints.</param>
    private static Fit FitSubset(int[] px, bool[] mask, int[] ch, int bits, int pmode, int ib, bool refine)
    {
        int n = 0;
        for (int i = 0; i < 16; i++) if (mask[i]) n++;
        var best = new Fit { Err = long.MaxValue };
        if (n == 0) { best.Err = 0; return best; }

        Span<double> lo = stackalloc double[4], hi = stackalloc double[4];
        foreach (var start in new[] { 0, 1 })
        {
            if (start == 0) PcaEndpoints(px, mask, ch, lo, hi);
            else BoxEndpoints(px, mask, ch, lo, hi);
            var fit = QuantizeAndAssign(px, mask, ch, bits, pmode, ib, lo, hi);
            if (fit.Err < best.Err) best = fit;
            if (!refine || best.Err == 0) { if (!refine) break; continue; }

            for (int iter = 0; iter < 3; iter++)
            {
                if (!LeastSquares(px, mask, ch, ib, fit.Idx, lo, hi)) break;
                var next = QuantizeAndAssign(px, mask, ch, bits, pmode, ib, lo, hi);
                if (next.Err >= fit.Err) break;
                fit = next;
                if (fit.Err < best.Err) best = fit;
            }
        }
        return best;
    }

    private static void BoxEndpoints(int[] px, bool[] mask, int[] ch, Span<double> lo, Span<double> hi)
    {
        foreach (int c in ch) { lo[c] = 255; hi[c] = 0; }
        for (int i = 0; i < 16; i++)
        {
            if (!mask[i]) continue;
            foreach (int c in ch) { lo[c] = Math.Min(lo[c], px[i * 4 + c]); hi[c] = Math.Max(hi[c], px[i * 4 + c]); }
        }
    }

    private static void PcaEndpoints(int[] px, bool[] mask, int[] ch, Span<double> lo, Span<double> hi)
    {
        Span<double> mean = stackalloc double[4];
        int n = 0;
        for (int i = 0; i < 16; i++)
        {
            if (!mask[i]) continue;
            foreach (int c in ch) mean[c] += px[i * 4 + c];
            n++;
        }
        foreach (int c in ch) mean[c] /= n;

        Span<double> cov = stackalloc double[16];
        for (int i = 0; i < 16; i++)
        {
            if (!mask[i]) continue;
            foreach (int a in ch)
                foreach (int b in ch)
                    cov[a * 4 + b] += (px[i * 4 + a] - mean[a]) * (px[i * 4 + b] - mean[b]);
        }

        Span<double> axis = stackalloc double[4];
        BoxEndpoints(px, mask, ch, lo, hi);
        foreach (int c in ch) axis[c] = hi[c] - lo[c] + 1e-3;
        Span<double> tmp = stackalloc double[4];
        for (int it = 0; it < 8; it++)
        {
            double len = 0;
            foreach (int a in ch)
            {
                double s = 0;
                foreach (int b in ch) s += cov[a * 4 + b] * axis[b];
                tmp[a] = s;
            }
            foreach (int a in ch) len += tmp[a] * tmp[a];
            len = Math.Sqrt(len);
            if (len < 1e-9) break;
            foreach (int a in ch) axis[a] = tmp[a] / len;
        }
        double norm = 0;
        foreach (int c in ch) norm += axis[c] * axis[c];
        norm = Math.Sqrt(norm);
        if (norm < 1e-9) { foreach (int c in ch) { lo[c] = hi[c] = mean[c]; } return; }
        foreach (int c in ch) axis[c] /= norm;

        double tmin = double.MaxValue, tmax = double.MinValue;
        for (int i = 0; i < 16; i++)
        {
            if (!mask[i]) continue;
            double t = 0;
            foreach (int c in ch) t += (px[i * 4 + c] - mean[c]) * axis[c];
            tmin = Math.Min(tmin, t); tmax = Math.Max(tmax, t);
        }
        foreach (int c in ch)
        {
            lo[c] = Math.Clamp(mean[c] + tmin * axis[c], 0, 255);
            hi[c] = Math.Clamp(mean[c] + tmax * axis[c], 0, 255);
        }
    }

    private static bool LeastSquares(int[] px, bool[] mask, int[] ch, int ib, int[] idx, Span<double> lo, Span<double> hi)
    {
        var w = Bc7.Weights(ib);
        double A = 0, B = 0, C = 0;
        Span<double> x0 = stackalloc double[4], x1 = stackalloc double[4];
        for (int i = 0; i < 16; i++)
        {
            if (!mask[i]) continue;
            double t = w[idx[i]] / 64.0, s = 1 - t;
            A += s * s; B += s * t; C += t * t;
            foreach (int c in ch) { x0[c] += s * px[i * 4 + c]; x1[c] += t * px[i * 4 + c]; }
        }
        double det = A * C - B * B;
        if (Math.Abs(det) < 1e-9) return false;
        foreach (int c in ch)
        {
            lo[c] = Math.Clamp((C * x0[c] - B * x1[c]) / det, 0, 255);
            hi[c] = Math.Clamp((A * x1[c] - B * x0[c]) / det, 0, 255);
        }
        return true;
    }

    private static Fit QuantizeAndAssign(int[] px, bool[] mask, int[] ch, int bits, int pmode, int ib, ReadOnlySpan<double> lo, ReadOnlySpan<double> hi)
    {
        Fit? best = null;
        int combos = pmode == 1 ? 4 : pmode == 2 ? 2 : 1;
        for (int combo = 0; combo < combos; combo++)
        {
            int p0 = pmode == 1 ? combo & 1 : pmode == 2 ? combo : 0;
            int p1 = pmode == 1 ? combo >> 1 : pmode == 2 ? combo : 0;
            var f = new Fit();
            f.P[0] = p0; f.P[1] = p1;
            foreach (int c in ch)
            {
                f.Q[0, c] = QuantizeChannel(lo[c], bits, pmode != 0, p0);
                f.Q[1, c] = QuantizeChannel(hi[c], bits, pmode != 0, p1);
            }
            Assign(px, mask, ch, bits, pmode != 0, ib, f);
            if (best == null || f.Err < best.Err) best = f;
        }
        return best!;
    }

    private static int Dequant(int q, int bits, bool hasP, int p) =>
        hasP ? Bc7.Expand((q << 1) | p, bits + 1) : Bc7.Expand(q, bits);

    private static int QuantizeChannel(double v, int bits, bool hasP, int p)
    {
        int max = (1 << bits) - 1;
        int total = bits + (hasP ? 1 : 0);
        double scaled = v / 255.0 * ((1 << total) - 1);
        int q = hasP ? (int)Math.Round((scaled - p) / 2) : (int)Math.Round(scaled);
        q = Math.Clamp(q, 0, max);
        int bestQ = q; double bestE = double.MaxValue;
        for (int d = -1; d <= 1; d++)
        {
            int cand = q + d;
            if (cand < 0 || cand > max) continue;
            double e = Math.Abs(Dequant(cand, bits, hasP, p) - v);
            if (e < bestE) { bestE = e; bestQ = cand; }
        }
        return bestQ;
    }

    private static void Assign(int[] px, bool[] mask, int[] ch, int bits, bool hasP, int ib, Fit f)
    {
        int levels = 1 << ib;
        Span<int> e0 = stackalloc int[4], e1 = stackalloc int[4];
        foreach (int c in ch)
        {
            e0[c] = Dequant(f.Q[0, c], bits, hasP, f.P[0]);
            e1[c] = Dequant(f.Q[1, c], bits, hasP, f.P[1]);
        }
        Span<int> pal = stackalloc int[16 * 4];
        for (int k = 0; k < levels; k++)
            foreach (int c in ch) pal[k * 4 + c] = Bc7.Interpolate(e0[c], e1[c], k, ib);
        long err = 0;
        for (int i = 0; i < 16; i++)
        {
            if (!mask[i]) continue;
            long bestE = long.MaxValue; int bestK = 0;
            for (int k = 0; k < levels; k++)
            {
                long e = 0;
                foreach (int c in ch) { long d = pal[k * 4 + c] - px[i * 4 + c]; e += d * d; }
                if (e < bestE) { bestE = e; bestK = k; }
            }
            f.Idx[i] = bestK;
            err += bestE;
        }
        f.Err = err;
    }

    // ---------------------------------------------------------------- packing

    private ref struct BitWriter
    {
        private readonly Span<byte> _o;
        private int _pos;
        public BitWriter(Span<byte> o) { _o = o; _o[..16].Clear(); _pos = 0; }
        public void Put(int value, int n)
        {
            for (int i = 0; i < n; i++, _pos++)
                if (((value >> i) & 1) != 0) _o[_pos >> 3] |= (byte)(1 << (_pos & 7));
        }
        public int Position => _pos;
    }

    private static void SwapEndpoints(Fit f, int[] ch, bool swapP)
    {
        foreach (int c in ch) (f.Q[0, c], f.Q[1, c]) = (f.Q[1, c], f.Q[0, c]);
        if (swapP) (f.P[0], f.P[1]) = (f.P[1], f.P[0]);
    }

    private static void Pack(Candidate cand, Span<byte> output)
    {
        var bw = new BitWriter(output);
        int mode = cand.Mode;
        bw.Put(1 << mode, mode + 1);
        var rgba = new[] { 0, 1, 2, 3 };
        var rgb = new[] { 0, 1, 2 };

        switch (mode)
        {
            case 6:
            {
                var f = cand.Sub[0];
                if (f.Idx[0] >= 8) { SwapEndpoints(f, rgba, true); for (int i = 0; i < 16; i++) f.Idx[i] = 15 - f.Idx[i]; }
                for (int c = 0; c < 4; c++) { bw.Put(f.Q[0, c], 7); bw.Put(f.Q[1, c], 7); }
                bw.Put(f.P[0], 1); bw.Put(f.P[1], 1);
                for (int i = 0; i < 16; i++) bw.Put(f.Idx[i], i == 0 ? 3 : 4);
                break;
            }
            case 5:
            {
                var f = cand.Sub[0];
                var a = cand.Alpha!;
                if (f.Idx[0] >= 2) { SwapEndpoints(f, rgb, false); for (int i = 0; i < 16; i++) f.Idx[i] = 3 - f.Idx[i]; }
                if (a.Idx[0] >= 2) { (a.Q[0, 3], a.Q[1, 3]) = (a.Q[1, 3], a.Q[0, 3]); for (int i = 0; i < 16; i++) a.Idx[i] = 3 - a.Idx[i]; }
                bw.Put(0, 2); // rotation
                for (int c = 0; c < 3; c++) { bw.Put(f.Q[0, c], 7); bw.Put(f.Q[1, c], 7); }
                bw.Put(a.Q[0, 3], 8); bw.Put(a.Q[1, 3], 8);
                for (int i = 0; i < 16; i++) bw.Put(f.Idx[i], i == 0 ? 1 : 2);
                for (int i = 0; i < 16; i++) bw.Put(a.Idx[i], i == 0 ? 1 : 2);
                break;
            }
            case 7:
            case 1:
            {
                int part = cand.Partition;
                int ib = mode == 7 ? 2 : 3, bits = mode == 7 ? 5 : 6;
                int half = 1 << (ib - 1), max = (1 << ib) - 1;
                var ch = mode == 7 ? rgba : rgb;
                // Each subset keeps the indices of its own pixels.
                var idx = new int[16];
                for (int i = 0; i < 16; i++) idx[i] = cand.Sub[Bc7.Subset(2, part, i)].Idx[i];
                for (int s = 0; s < 2; s++)
                {
                    int anchor = s == 0 ? 0 : Bc7.Anchor2[part];
                    if (idx[anchor] < half) continue;
                    SwapEndpoints(cand.Sub[s], ch, mode == 7);
                    for (int i = 0; i < 16; i++) if (Bc7.Subset(2, part, i) == s) idx[i] = max - idx[i];
                }
                bw.Put(part, 6);
                foreach (int c in ch)
                    for (int s = 0; s < 2; s++) { bw.Put(cand.Sub[s].Q[0, c], bits); bw.Put(cand.Sub[s].Q[1, c], bits); }
                if (mode == 7)
                    for (int s = 0; s < 2; s++) { bw.Put(cand.Sub[s].P[0], 1); bw.Put(cand.Sub[s].P[1], 1); }
                else
                    for (int s = 0; s < 2; s++) bw.Put(cand.Sub[s].P[0], 1);
                for (int i = 0; i < 16; i++) bw.Put(idx[i], Bc7.IsAnchor(2, part, i) ? ib - 1 : ib);
                break;
            }
            default:
                throw new InvalidOperationException($"BC7 mode {mode} is not produced by this encoder");
        }
        if (bw.Position != 128) throw new InvalidOperationException($"BC7 mode {mode}: wrote {bw.Position} bits");
    }
}

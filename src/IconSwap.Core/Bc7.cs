namespace IconSwap.Core;

/// <summary>BC7 tables and block decoder.</summary>
public static class Bc7
{
    internal static readonly ushort[] Partition2 =
    {
        0xCCCC,0x8888,0xEEEE,0xECC8,0xC880,0xFEEC,0xFEC8,0xEC80,0xC800,0xFFEC,0xFE80,0xE800,0xFFE8,0xFF00,0xFFF0,0xF000,
        0xF710,0x008E,0x7100,0x08CE,0x008C,0x7310,0x3100,0x8CCE,0x088C,0x3110,0x6666,0x366C,0x17E8,0x0FF0,0x718E,0x399C,
        0xAAAA,0xF0F0,0x5A5A,0x33CC,0x3C3C,0x55AA,0x9696,0xA55A,0x73CE,0x13C8,0x324C,0x3BDC,0x6996,0xC33C,0x9966,0x0660,
        0x0272,0x04E4,0x4E40,0x2720,0xC936,0x936C,0x39C6,0x639C,0x9336,0x9CC6,0x817E,0xE718,0xCCF0,0x0FCC,0x7744,0xEE22,
    };

    private static readonly string[] Partition3 =
    {
        "0011001122112221","0001001122112221","0000200122112211","0222002200110111","0000000011221122","0011001100220022","0022002211111111","0011001122112211",
        "0000000011112222","0000111111112222","0000111122222222","0012001200120012","0112011201120112","0122012201220122","0011011211221222","0011200122002220",
        "0001001101121122","0111001120012200","0000112211221122","0022002200221111","0111011102220222","0001000122212221","0000001101220122","0000110022102210",
        "0122012200110000","0012001211222222","0110122112210110","0000011012211221","0022110211020022","0110011020022222","0011012201220011","0000200022112221",
        "0000000211221222","0222002200120011","0011001200220222","0120012001200120","0000111122220000","0120120120120120","0120201212010120","0011220011220011",
        "0011112222000011","0101010122222222","0000000021212121","0022112200221122","0022001100220011","0220122102201221","0101222222220101","0000212121212121",
        "0101010101012222","0222011102220111","0002111200021112","0000211221122112","0222011101110222","0002111211121112","0110011001102222","0000000021122112",
        "0110011022222222","0022001100110022","0022112211220022","0000000000002112","0002000100020001","0222122202221222","0101222222222222","0111201122012220",
    };

    internal static readonly byte[] Anchor2 =
    {
        15,15,15,15,15,15,15,15,15,15,15,15,15,15,15,15,15,2,8,2,2,8,8,15,2,8,2,2,8,8,2,2,
        15,15,6,8,2,8,15,15,2,8,2,2,2,15,15,6,6,2,6,8,15,15,2,2,15,15,15,15,15,2,2,15,
    };
    private static readonly byte[] Anchor3a =
    {
        3,3,15,15,8,3,15,15,8,8,6,6,6,5,3,3,3,3,8,15,3,3,6,10,5,8,8,6,8,5,15,15,
        8,15,3,5,6,10,8,15,15,3,15,5,15,15,15,15,3,15,5,5,5,8,5,10,5,10,8,13,15,12,3,3,
    };
    private static readonly byte[] Anchor3b =
    {
        15,8,8,3,15,15,3,8,15,15,15,15,15,15,15,8,15,8,15,3,15,8,15,8,3,15,6,10,15,15,10,8,
        15,3,15,10,10,8,9,10,6,15,8,15,3,6,6,8,15,3,15,15,15,15,15,15,15,15,15,15,3,15,15,8,
    };

    internal static readonly int[] W2 = { 0, 21, 43, 64 };
    internal static readonly int[] W3 = { 0, 9, 18, 27, 37, 46, 55, 64 };
    internal static readonly int[] W4 = { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 };

    internal static int[] Weights(int bits) => bits == 2 ? W2 : bits == 3 ? W3 : W4;

    // subsets, partitionBits, rotationBits, indexSelBits, colorBits, alphaBits, endpointPBits, sharedPBits, indexBits, index2Bits
    internal static readonly int[,] Modes =
    {
        { 3, 4, 0, 0, 4, 0, 1, 0, 3, 0 },
        { 2, 6, 0, 0, 6, 0, 0, 1, 3, 0 },
        { 3, 6, 0, 0, 5, 0, 0, 0, 2, 0 },
        { 2, 6, 0, 0, 7, 0, 1, 0, 2, 0 },
        { 1, 0, 2, 1, 5, 6, 0, 0, 2, 3 },
        { 1, 0, 2, 0, 7, 8, 0, 0, 2, 2 },
        { 1, 0, 0, 0, 7, 7, 1, 0, 4, 0 },
        { 2, 6, 0, 0, 5, 5, 1, 0, 2, 0 },
    };

    internal static int Subset(int subsets, int partition, int pixel) => subsets switch
    {
        1 => 0,
        2 => (Partition2[partition] >> pixel) & 1,
        _ => Partition3[partition][pixel] - '0',
    };

    internal static bool IsAnchor(int subsets, int partition, int pixel) =>
        pixel == 0 ||
        (subsets == 2 && pixel == Anchor2[partition]) ||
        (subsets == 3 && (pixel == Anchor3a[partition] || pixel == Anchor3b[partition]));

    internal static int Interpolate(int a, int b, int index, int bits)
    {
        int w = Weights(bits)[index];
        return ((64 - w) * a + w * b + 32) >> 6;
    }

    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _b;
        private int _pos;
        public BitReader(ReadOnlySpan<byte> b) { _b = b; _pos = 0; }
        public int Get(int n)
        {
            int v = 0;
            for (int i = 0; i < n; i++, _pos++)
                v |= ((_b[_pos >> 3] >> (_pos & 7)) & 1) << i;
            return v;
        }
    }

    /// <summary>Decodes one 16-byte block to 16 RGBA pixels (64 bytes, row-major).</summary>
    public static void DecodeBlock(ReadOnlySpan<byte> block, Span<byte> rgba)
    {
        var br = new BitReader(block);
        int mode = 0;
        while (mode < 8 && br.Get(1) == 0) mode++;
        if (mode == 8) { rgba[..64].Clear(); return; }

        int ns = Modes[mode, 0], pb = Modes[mode, 1], rb = Modes[mode, 2], isb = Modes[mode, 3];
        int cb = Modes[mode, 4], ab = Modes[mode, 5], epb = Modes[mode, 6], spb = Modes[mode, 7];
        int ib = Modes[mode, 8], ib2 = Modes[mode, 9];

        int part = pb > 0 ? br.Get(pb) : 0;
        int rot = rb > 0 ? br.Get(rb) : 0;
        int idxSel = isb > 0 ? br.Get(isb) : 0;
        int ne = ns * 2;
        Span<int> ep = stackalloc int[6 * 4];
        for (int e = 0; e < ne; e++) ep[e * 4 + 3] = 255;
        for (int c = 0; c < 3; c++)
            for (int e = 0; e < ne; e++) ep[e * 4 + c] = br.Get(cb);
        if (ab > 0)
            for (int e = 0; e < ne; e++) ep[e * 4 + 3] = br.Get(ab);
        int channels = ab > 0 ? 4 : 3;
        if (epb > 0)
            for (int e = 0; e < ne; e++)
            {
                int p = br.Get(1);
                for (int c = 0; c < channels; c++) ep[e * 4 + c] = (ep[e * 4 + c] << 1) | p;
            }
        if (spb > 0)
            for (int s = 0; s < ns; s++)
            {
                int p = br.Get(1);
                for (int e = s * 2; e < s * 2 + 2; e++)
                    for (int c = 0; c < channels; c++) ep[e * 4 + c] = (ep[e * 4 + c] << 1) | p;
            }
        int cbits = cb + (epb + spb > 0 ? 1 : 0);
        int abits = ab > 0 ? ab + (epb + spb > 0 ? 1 : 0) : 0;
        for (int e = 0; e < ne; e++)
        {
            for (int c = 0; c < 3; c++) ep[e * 4 + c] = Expand(ep[e * 4 + c], cbits);
            if (ab > 0) ep[e * 4 + 3] = Expand(ep[e * 4 + 3], abits);
        }

        Span<int> idx = stackalloc int[16];
        Span<int> idx2 = stackalloc int[16];
        for (int i = 0; i < 16; i++)
            idx[i] = br.Get(IsAnchor(ns, part, i) ? ib - 1 : ib);
        if (ib2 > 0)
            for (int i = 0; i < 16; i++) idx2[i] = br.Get(i == 0 ? ib2 - 1 : ib2);

        for (int i = 0; i < 16; i++)
        {
            int s = Subset(ns, part, i);
            int e0 = s * 8, e1 = s * 8 + 4;
            int r, g, b, a;
            if (ib2 > 0)
            {
                int ci = idxSel == 0 ? idx[i] : idx2[i], cbi = idxSel == 0 ? ib : ib2;
                int ai = idxSel == 0 ? idx2[i] : idx[i], abi = idxSel == 0 ? ib2 : ib;
                r = Interpolate(ep[e0], ep[e1], ci, cbi);
                g = Interpolate(ep[e0 + 1], ep[e1 + 1], ci, cbi);
                b = Interpolate(ep[e0 + 2], ep[e1 + 2], ci, cbi);
                a = Interpolate(ep[e0 + 3], ep[e1 + 3], ai, abi);
            }
            else
            {
                r = Interpolate(ep[e0], ep[e1], idx[i], ib);
                g = Interpolate(ep[e0 + 1], ep[e1 + 1], idx[i], ib);
                b = Interpolate(ep[e0 + 2], ep[e1 + 2], idx[i], ib);
                a = Interpolate(ep[e0 + 3], ep[e1 + 3], idx[i], ib);
            }
            switch (rot)
            {
                case 1: (r, a) = (a, r); break;
                case 2: (g, a) = (a, g); break;
                case 3: (b, a) = (a, b); break;
            }
            rgba[i * 4] = (byte)r; rgba[i * 4 + 1] = (byte)g; rgba[i * 4 + 2] = (byte)b; rgba[i * 4 + 3] = (byte)a;
        }
    }

    internal static int Expand(int v, int bits) => bits >= 8 ? v : (v << (8 - bits)) | (v >> (2 * bits - 8));
}

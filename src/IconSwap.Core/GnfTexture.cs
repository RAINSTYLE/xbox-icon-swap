using System.Buffers.Binary;

namespace IconSwap.Core;

/// <summary>
/// PS4 GNF texture with a single 2D surface. Header: "GNF " u32 headerSize, u8 version,
/// u8 textureCount, u8 alignment, u8 pad, u32 fileSize, then the 32-byte T# descriptor.
/// Pixel data starts at headerSize + 8.
/// </summary>
public sealed class GnfTexture
{
    public const int DataFormatBc7 = 41;

    public byte[] Header { get; }
    public byte[] Data { get; }
    public int Width { get; }
    public int Height { get; }
    public int Pitch { get; }
    public int DataFormat { get; }
    public int NumberFormat { get; }
    public int TileMode { get; }
    public int MipLevels { get; }

    private GnfTexture(byte[] header, byte[] data, uint[] t)
    {
        Header = header;
        Data = data;
        DataFormat = (int)((t[1] >> 20) & 0x3F);
        NumberFormat = (int)((t[1] >> 26) & 0xF);
        Width = (int)(t[2] & 0x3FFF) + 1;
        Height = (int)((t[2] >> 14) & 0x3FFF) + 1;
        TileMode = (int)((t[3] >> 20) & 0x1F);
        MipLevels = (int)(((t[3] >> 16) & 0xF) - ((t[3] >> 12) & 0xF)) + 1;
        Pitch = (int)((t[4] >> 13) & 0x3FFF) + 1;
    }

    public static GnfTexture Parse(byte[] file)
    {
        if (file.Length < 0x30 || file[0] != 'G' || file[1] != 'N' || file[2] != 'F' || file[3] != ' ')
            throw new InvalidDataException("Не GNF-текстура");
        int headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4)) + 8;
        if (file[9] != 1) throw new InvalidDataException($"GNF содержит {file[9]} текстур(ы), поддерживается 1");
        var t = new uint[8];
        for (int i = 0; i < 8; i++) t[i] = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x10 + i * 4));
        return new GnfTexture(file[..headerSize], file[headerSize..], t);
    }

    public bool IsSupported(out string reason)
    {
        reason = "";
        if (DataFormat != DataFormatBc7) reason = $"формат данных {DataFormat} (ожидался BC7)";
        else if (TileMode != 13) reason = $"режим тайлинга {TileMode} (ожидался 13)";
        else if (MipLevels != 1) reason = $"{MipLevels} мип-уровней (ожидался 1)";
        return reason.Length == 0;
    }

    private int BlocksWide => (Width + 3) / 4;
    private int BlocksHigh => (Height + 3) / 4;
    private int TilesWide => ((Math.Max(Pitch, Width) + 3) / 4 + 7) / 8;

    /// <summary>
    /// Byte offset of a 4x4 block in "1D thin" tiled memory: 8x8-block micro tiles in Morton
    /// order (x0 y0 x1 y1 x2 y2), the micro tiles stored row by row across the pitch.
    /// </summary>
    private int BlockOffset(int bx, int by)
    {
        int tile = (by >> 3) * TilesWide + (bx >> 3);
        int x = bx & 7, y = by & 7;
        int morton = (x & 1) | ((y & 1) << 1) | ((x & 2) << 1) | ((y & 2) << 2) | ((x & 4) << 2) | ((y & 4) << 3);
        return (tile * 64 + morton) * 16;
    }

    public RgbaImage Decode()
    {
        if (!IsSupported(out var why)) throw new NotSupportedException(why);
        var img = new RgbaImage(Width, Height);
        var block = new byte[64];
        for (int by = 0; by < BlocksHigh; by++)
        for (int bx = 0; bx < BlocksWide; bx++)
        {
            int off = BlockOffset(bx, by);
            if (off + 16 > Data.Length) throw new InvalidDataException("GNF: данные короче ожидаемого");
            Bc7.DecodeBlock(Data.AsSpan(off, 16), block);
            for (int i = 0; i < 16; i++)
            {
                int x = bx * 4 + (i & 3), y = by * 4 + (i >> 2);
                if (x >= Width || y >= Height) continue;
                Buffer.BlockCopy(block, i * 4, img.Pixels, (y * Width + x) * 4, 4);
            }
        }
        return img;
    }

    /// <summary>
    /// Builds a new GNF file with the same header/layout and the image re-encoded.
    /// Padding blocks outside the image keep their original bytes.
    /// </summary>
    public byte[] Encode(RgbaImage image, Bc7Encoder encoder)
    {
        if (!IsSupported(out var why)) throw new NotSupportedException(why);
        if (image.Width != Width || image.Height != Height)
            image = image.Resize(Width, Height);
        var src = image.BleedTransparentColors();
        var data = (byte[])Data.Clone();
        Parallel.For(0, BlocksHigh, by =>
        {
            var block = new byte[64];
            var encoded = new byte[16];
            for (int bx = 0; bx < BlocksWide; bx++)
            {
                for (int i = 0; i < 16; i++)
                {
                    // Edge blocks repeat the last row/column.
                    int x = Math.Min(bx * 4 + (i & 3), Width - 1), y = Math.Min(by * 4 + (i >> 2), Height - 1);
                    Buffer.BlockCopy(src.Pixels, (y * Width + x) * 4, block, i * 4, 4);
                }
                encoder.EncodeBlock(block, encoded);
                Buffer.BlockCopy(encoded, 0, data, BlockOffset(bx, by), 16);
            }
        });
        var file = new byte[Header.Length + data.Length];
        Buffer.BlockCopy(Header, 0, file, 0, Header.Length);
        Buffer.BlockCopy(data, 0, file, Header.Length, data.Length);
        return file;
    }
}

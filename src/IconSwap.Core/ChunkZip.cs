using System.Buffers.Binary;
using System.IO.Compression;

namespace IconSwap.Core;

/// <summary>
/// EA "chunkzip" container (big-endian):
///   "chunkzip" u32 version(2) u32 uncompressedSize u32 chunkSize u32 chunkCount u32 alignment
///   per chunk, placed so that its data starts on `alignment`: u32 packedSize u32 method, data
///   method 1 = raw deflate.
/// </summary>
public static class ChunkZip
{
    private static ReadOnlySpan<byte> Magic => "chunkzip"u8;
    private const int DefaultChunk = 0x20000;
    private const int DefaultAlign = 16;

    public static bool IsChunkZip(ReadOnlySpan<byte> data) => data.Length >= 28 && data[..8].SequenceEqual(Magic);

    public static byte[] Unpack(byte[] data)
    {
        if (!IsChunkZip(data)) return data;
        var s = data.AsSpan();
        uint version = BinaryPrimitives.ReadUInt32BigEndian(s[8..]);
        int usize = checked((int)BinaryPrimitives.ReadUInt32BigEndian(s[12..]));
        int count = checked((int)BinaryPrimitives.ReadUInt32BigEndian(s[20..]));
        int align = checked((int)BinaryPrimitives.ReadUInt32BigEndian(s[24..]));
        if (version != 2 || align <= 0) throw new InvalidDataException($"chunkzip: версия {version}, выравнивание {align}");

        var output = new byte[usize];
        int outPos = 0, pos = 28;
        for (int c = 0; c < count; c++)
        {
            while ((pos + 8) % align != 0) pos++;
            int packed = checked((int)BinaryPrimitives.ReadUInt32BigEndian(s[pos..]));
            uint method = BinaryPrimitives.ReadUInt32BigEndian(s[(pos + 4)..]);
            pos += 8;
            if (method == 1)
            {
                using var ds = new DeflateStream(new MemoryStream(data, pos, packed), CompressionMode.Decompress);
                int n;
                while (outPos < usize && (n = ds.Read(output, outPos, usize - outPos)) > 0) outPos += n;
            }
            else if (method == 0)
            {
                Buffer.BlockCopy(data, pos, output, outPos, packed);
                outPos += packed;
            }
            else throw new InvalidDataException($"chunkzip: неизвестный метод сжатия {method}");
            pos += packed;
        }
        if (outPos != usize) throw new InvalidDataException($"chunkzip: распаковано {outPos} из {usize} байт");
        return output;
    }

    public static byte[] Pack(byte[] data)
    {
        int count = Math.Max(1, (data.Length + DefaultChunk - 1) / DefaultChunk);
        using var ms = new MemoryStream();
        var u32 = new byte[4];
        void W(uint v) { BinaryPrimitives.WriteUInt32BigEndian(u32, v); ms.Write(u32); }

        ms.Write(Magic);
        W(2); W((uint)data.Length); W(DefaultChunk); W((uint)count); W(DefaultAlign);
        for (int c = 0; c < count; c++)
        {
            int start = c * DefaultChunk;
            int len = Math.Min(DefaultChunk, data.Length - start);
            byte[] packed;
            using (var cs = new MemoryStream())
            {
                using (var ds = new DeflateStream(cs, CompressionLevel.SmallestSize, leaveOpen: true))
                    ds.Write(data, start, len);
                packed = cs.ToArray();
            }
            while ((ms.Position + 8) % DefaultAlign != 0) ms.WriteByte(0);
            W((uint)packed.Length); W(1);
            ms.Write(packed);
        }
        return ms.ToArray();
    }
}

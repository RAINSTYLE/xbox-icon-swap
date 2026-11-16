using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace IconSwap.Core;

/// <summary>
/// Scaleform/Flash UIs often ship both platform variants of a symbol, e.g.
/// "parts.buttons.Btn_start_ps_mc" and "parts.buttons.Btn_start_xb_mc", and pick one by
/// platform at run time. Swapping the character IDs of each pair in the SymbolClass and
/// ExportAssets tags makes the PS name show the Xbox artwork (and vice versa). Only 2-byte
/// IDs change, so the file keeps its exact size and structure.
/// </summary>
public static class SwfSymbolSwap
{
    private const int TagExportAssets = 56;
    private const int TagSymbolClass = 76;

    public static byte[] Apply(byte[] swf, string from, string to, out int pairs)
    {
        if (swf.Length < 8) throw new InvalidDataException("SWF слишком короткий");
        char kind = (char)swf[0];
        if (swf[1] != 'W' || swf[2] != 'S' || (kind != 'F' && kind != 'C'))
            throw new InvalidDataException(kind == 'Z' ? "SWF со сжатием LZMA не поддерживается" : "Не SWF-файл");

        byte[] body; // everything after the 8-byte header, uncompressed
        if (kind == 'C')
        {
            using var zs = new ZLibStream(new MemoryStream(swf, 8, swf.Length - 8), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            zs.CopyTo(ms);
            body = ms.ToArray();
        }
        else body = swf[8..];

        // name -> (character id, positions of that id in the body)
        var symbols = new Dictionary<string, (ushort Id, List<int> Pos)>(StringComparer.Ordinal);
        int nbits = body[0] >> 3;
        int pos = (5 + nbits * 4 + 7) / 8 + 4; // RECT + frame rate + frame count
        while (pos + 2 <= body.Length)
        {
            int code = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(pos));
            int type = code >> 6, len = code & 0x3F;
            pos += 2;
            if (len == 0x3F) { len = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(pos)); pos += 4; }
            if (type == TagExportAssets || type == TagSymbolClass)
            {
                int p = pos, count = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(p));
                p += 2;
                for (int i = 0; i < count; i++)
                {
                    int idPos = p;
                    ushort id = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(p));
                    int end = Array.IndexOf(body, (byte)0, p + 2);
                    string name = Encoding.UTF8.GetString(body, p + 2, end - p - 2);
                    p = end + 1;
                    if (!symbols.TryGetValue(name, out var s)) symbols[name] = s = (id, new List<int>());
                    s.Pos.Add(idPos);
                }
            }
            if (type == 0) break;
            pos += len;
        }

        pairs = 0;
        foreach (var (name, a) in symbols)
        {
            if (!name.Contains(from, StringComparison.Ordinal)) continue;
            if (!symbols.TryGetValue(name.Replace(from, to, StringComparison.Ordinal), out var b)) continue;
            foreach (var p in a.Pos) BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(p), b.Id);
            foreach (var p in b.Pos) BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(p), a.Id);
            pairs++;
        }

        using var output = new MemoryStream();
        output.Write(swf, 0, 8); // same signature, version and uncompressed length
        if (kind == 'C')
        {
            using var zs = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true);
            zs.Write(body);
        }
        else output.Write(body);
        return output.ToArray();
    }
}

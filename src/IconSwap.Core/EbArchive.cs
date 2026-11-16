using System.Buffers.Binary;
using System.Text;

namespace IconSwap.Core;

/// <summary>One file inside an EA "EB" v3 archive (.big).</summary>
public sealed class EbEntry
{
    public int Index { get; init; }
    /// <summary>Byte offset of the data (already multiplied by the archive alignment).</summary>
    public long Offset { get; set; }
    public uint Size { get; set; }
    public uint NameHash { get; init; }
    public string Name { get; init; } = "";
    public string Folder { get; init; } = "";
    public string FullPath => Folder.Length == 0 ? Name : Folder + "/" + Name;
}

/// <summary>
/// Reader for EA Sports "EB" (version 3) archives, used by the Ignite-era PS4 games
/// (UFC 2/3, FIFA, Madden, NHL...). All values are big-endian.
///
///   0x00 "EB"  u16 version(3)  u32 fileCount  u16 flags  u8 alignShift  u8 pad
///   0x0C u32 namesOffset  u32 namesSize  u8 nameLen  u8 folderLen  u16 folderCount
///   0x18 u32 0  u32 archiveSize(low 32 bits)  16 bytes zero
///   0x30 fileCount x { u32 offset>>alignShift, u32 0, u32 size, u32 nameHash }
///   ... one byte per file ...
///   namesOffset: fileCount x { u16 folderIndex, char name[nameLen-2] }, padded to 16,
///                folderCount x char folder[folderLen]
/// </summary>
public sealed class EbArchive
{
    public const int EntryTableStart = 0x30;
    public const int EntrySize = 16;

    public string FilePath { get; }
    public int FileCount { get; private set; }
    public int AlignShift { get; private set; }
    public List<EbEntry> Entries { get; } = new();

    /// <summary>Size of the region that a patch rewrites: header + entry table.</summary>
    public int TableLength => EntryTableStart + FileCount * EntrySize;

    private EbArchive(string path) { FilePath = path; }

    public static bool LooksLikeEb(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> h = stackalloc byte[4];
            return fs.Read(h) == 4 && h[0] == (byte)'E' && h[1] == (byte)'B' && h[2] == 0 && h[3] == 3;
        }
        catch { return false; }
    }

    public static EbArchive Open(string path) => Open(path, null);

    /// <param name="originalTable">Header + entry table to use instead of the file's own
    /// (the original table kept in a patch backup).</param>
    public static EbArchive Open(string path, byte[]? originalTable)
    {
        var a = new EbArchive(path);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        a.Load(fs, originalTable);
        return a;
    }

    private void Load(FileStream fs, byte[]? originalTable)
    {
        var head = originalTable != null ? originalTable[..EntryTableStart] : ReadExact(fs, 0, EntryTableStart);
        if (head[0] != 'E' || head[1] != 'B') throw new InvalidDataException($"{FilePath}: не архив EB");
        int version = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(2));
        if (version != 3) throw new InvalidDataException($"{FilePath}: неподдерживаемая версия EB {version}");
        FileCount = checked((int)BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(4)));
        AlignShift = head[0x0A];
        uint namesOffset = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0x0C));
        uint namesSize = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0x10));
        int nameLen = head[0x14];
        int folderLen = head[0x15];
        int folderCount = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(0x16));

        var table = originalTable != null
            ? originalTable[EntryTableStart..(EntryTableStart + FileCount * EntrySize)]
            : ReadExact(fs, EntryTableStart, FileCount * EntrySize);
        var names = ReadExact(fs, namesOffset, (int)namesSize);

        var folders = new string[folderCount];
        int folderBase = (FileCount * nameLen + 15) & ~15;
        for (int i = 0; i < folderCount; i++)
        {
            int o = folderBase + i * folderLen;
            folders[i] = o + folderLen <= names.Length ? CString(names, o, folderLen) : "";
        }

        for (int i = 0; i < FileCount; i++)
        {
            var e = table.AsSpan(i * EntrySize, EntrySize);
            int rec = i * nameLen;
            int folderIdx = BinaryPrimitives.ReadUInt16BigEndian(names.AsSpan(rec));
            Entries.Add(new EbEntry
            {
                Index = i,
                Offset = (long)BinaryPrimitives.ReadUInt32BigEndian(e) << AlignShift,
                Size = BinaryPrimitives.ReadUInt32BigEndian(e[8..]),
                NameHash = BinaryPrimitives.ReadUInt32BigEndian(e[12..]),
                Name = CString(names, rec + 2, nameLen - 2),
                Folder = folderIdx < folders.Length ? folders[folderIdx] : "",
            });
        }
    }

    public byte[] ReadRaw(EbEntry e)
    {
        using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return ReadExact(fs, e.Offset, checked((int)e.Size));
    }

    /// <summary>Entry data with chunkzip compression removed.</summary>
    public byte[] Read(EbEntry e) => ChunkZip.Unpack(ReadRaw(e));

    public EbEntry? Find(string fullPath) =>
        Entries.FirstOrDefault(e => string.Equals(e.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));

    internal static byte[] ReadExact(Stream s, long offset, int count)
    {
        var buf = new byte[count];
        s.Seek(offset, SeekOrigin.Begin);
        int got = 0;
        while (got < count)
        {
            int n = s.Read(buf, got, count - got);
            if (n <= 0) throw new EndOfStreamException($"Неожиданный конец файла по смещению {offset + got}");
            got += n;
        }
        return buf;
    }

    private static string CString(byte[] b, int start, int max)
    {
        int end = start;
        while (end < start + max && end < b.Length && b[end] != 0) end++;
        return Encoding.Latin1.GetString(b, start, end - start);
    }
}

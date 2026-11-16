using System.Buffers.Binary;
using System.Security.Cryptography;

namespace IconSwap.Core;

/// <summary>
/// Replaces textures inside an EB archive without touching any existing data byte:
/// the new textures are appended at the end of the file and only the header and the entry
/// table (offset/size) are rewritten. A small backup next to the archive keeps the original
/// table and length, so "restore" writes the table back and cuts the file to its old size.
/// </summary>
public static class ArchivePatcher
{
    private static ReadOnlySpan<byte> Magic => "XBICONS1"u8;
    public const string BackupSuffix = ".xboxicons.bak";

    public static string BackupPath(string archive) => archive + BackupSuffix;
    public static bool IsPatched(string archive) => File.Exists(BackupPath(archive));

    private sealed record Backup(long Length, byte[] Table);

    private static void WriteBackup(string archive)
    {
        using var fs = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        var head = EbArchive.ReadExact(fs, 0, EbArchive.EntryTableStart);
        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(4));
        var table = EbArchive.ReadExact(fs, 0, EbArchive.EntryTableStart + count * EbArchive.EntrySize);
        var hash = SHA256.HashData(table);

        var tmp = BackupPath(archive) + ".tmp";
        using (var bs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        {
            Span<byte> b8 = stackalloc byte[8];
            bs.Write(Magic);
            BinaryPrimitives.WriteInt64LittleEndian(b8, fs.Length); bs.Write(b8);
            BinaryPrimitives.WriteInt32LittleEndian(b8, table.Length); bs.Write(b8[..4]);
            bs.Write(table);
            bs.Write(hash);
            bs.Flush(true);
        }
        File.Move(tmp, BackupPath(archive), overwrite: true);
    }

    private static Backup ReadBackup(string archive)
    {
        var b = File.ReadAllBytes(BackupPath(archive));
        if (b.Length < 20 || !b.AsSpan(0, 8).SequenceEqual(Magic)) throw new InvalidDataException("Повреждённая резервная копия");
        long length = BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(8));
        int tableLen = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(16));
        if (tableLen <= 0 || 20 + tableLen + 32 != b.Length) throw new InvalidDataException("Повреждённая резервная копия");
        var table = b.AsSpan(20, tableLen).ToArray();
        if (!SHA256.HashData(table).AsSpan().SequenceEqual(b.AsSpan(20 + tableLen, 32)))
            throw new InvalidDataException("Резервная копия не прошла проверку контрольной суммы");
        return new Backup(length, table);
    }

    /// <summary>
    /// Opens the archive as it was before patching (the original data is never overwritten,
    /// so the original table from the backup still points at it).
    /// </summary>
    public static EbArchive OpenOriginal(string archive) =>
        IsPatched(archive) ? EbArchive.Open(archive, ReadBackup(archive).Table) : EbArchive.Open(archive);

    /// <summary>Puts the archive back exactly as it was before patching.</summary>
    public static void Restore(string archive, bool deleteBackup = true)
    {
        var backup = ReadBackup(archive);
        using (var fs = new FileStream(archive, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            if (fs.Length < backup.Length)
                throw new InvalidDataException("Архив короче, чем был до установки — похоже, файл игры заменили. Резервная копия не применена.");
            var head = EbArchive.ReadExact(fs, 0, EbArchive.EntryTableStart);
            if (!head.AsSpan(0, 8).SequenceEqual(backup.Table.AsSpan(0, 8)))
                throw new InvalidDataException("Заголовок архива не совпадает с резервной копией. Резервная копия не применена.");
            fs.Seek(0, SeekOrigin.Begin);
            fs.Write(backup.Table);
            fs.SetLength(backup.Length);
            fs.Flush(true);
        }
        if (deleteBackup) File.Delete(BackupPath(archive));
    }

    public sealed record Result(int Replaced, List<string> Warnings);

    /// <summary>
    /// Installs the replacements. If the archive was patched before, it is first restored,
    /// so running the patch again never grows the file further.
    /// </summary>
    public static Result Patch(string archive, IReadOnlyList<(string ArchivePath, RgbaImage Image)> replacements,
                               Action<string>? log = null, Action<int, int>? progress = null)
    {
        var encoder = new Bc7Encoder();
        var changes = replacements.Select(r => new Change(r.ArchivePath, (byte[] file, List<string> warn) =>
        {
            var gnf = GnfTexture.Parse(file);
            if (!gnf.IsSupported(out var why)) { warn.Add($"{r.ArchivePath}: {why}"); return null; }
            if (r.Image.Width != gnf.Width || r.Image.Height != gnf.Height)
                warn.Add($"{r.ArchivePath}: размер PNG {r.Image.Width}x{r.Image.Height} приведён к {gnf.Width}x{gnf.Height}");
            return gnf.Encode(r.Image, encoder);
        })).ToList();
        return Patch(archive, changes, log, progress);
    }

    /// <summary>
    /// A change to one file inside the archive: gets the original (decompressed) file and
    /// returns the new content, or null to leave it as is (with a warning added).
    /// </summary>
    public sealed record Change(string ArchivePath, Func<byte[], List<string>, byte[]?> Transform);

    public static Result Patch(string archive, IReadOnlyList<Change> changes,
                               Action<string>? log = null, Action<int, int>? progress = null)
    {
        var warnings = new List<string>();

        // Fail early (before writing anything) if the game holds the file open.
        using (new FileStream(archive, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }

        if (IsPatched(archive))
        {
            log?.Invoke("Откат предыдущей установки...");
            Restore(archive, deleteBackup: false);
        }
        else
        {
            WriteBackup(archive);
        }

        var eb = EbArchive.Open(archive);
        var blobs = new List<(EbEntry Entry, byte[] Data)>();
        int done = 0;
        foreach (var change in changes)
        {
            progress?.Invoke(done++, changes.Count);
            var path = change.ArchivePath;
            var entry = eb.Find(path);
            if (entry == null) { warnings.Add($"Нет в архиве: {path}"); continue; }
            var raw = eb.ReadRaw(entry);
            bool packed = ChunkZip.IsChunkZip(raw);
            byte[]? file;
            try { file = change.Transform(ChunkZip.Unpack(raw), warnings); }
            catch (Exception ex) { warnings.Add($"{path}: {ex.Message}"); continue; }
            if (file == null) continue;
            blobs.Add((entry, packed ? ChunkZip.Pack(file) : file));
        }
        progress?.Invoke(done, changes.Count);

        var tableBytes = new byte[eb.TableLength];
        using (var fs = new FileStream(archive, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            EbArchive.ReadExact(fs, 0, tableBytes.Length).CopyTo(tableBytes, 0);
            int align = 1 << eb.AlignShift;
            long pos = (fs.Length + align - 1) / align * align;
            foreach (var (entry, data) in blobs)
            {
                fs.Seek(pos, SeekOrigin.Begin);
                fs.Write(data);
                long slot = pos >> eb.AlignShift;
                if (slot > uint.MaxValue) throw new IOException("Архив слишком велик");
                var e = tableBytes.AsSpan(EbArchive.EntryTableStart + entry.Index * EbArchive.EntrySize, EbArchive.EntrySize);
                BinaryPrimitives.WriteUInt32BigEndian(e, (uint)slot);
                BinaryPrimitives.WriteUInt32BigEndian(e[8..], (uint)data.Length);
                pos = (pos + data.Length + align - 1) / align * align;
            }
            fs.SetLength(pos);
            fs.Flush(true);
            // Header keeps the archive size (low 32 bits) at 0x1C.
            BinaryPrimitives.WriteUInt32BigEndian(tableBytes.AsSpan(0x1C), (uint)(pos & 0xFFFFFFFF));
            fs.Seek(0, SeekOrigin.Begin);
            fs.Write(tableBytes);
            fs.Flush(true);
        }
        log?.Invoke($"{Path.GetFileName(archive)}: заменено файлов — {blobs.Count}");
        return new Result(blobs.Count, warnings);
    }
}

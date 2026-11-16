using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IconSwap.Core;

public sealed class ArchiveSpec
{
    /// <summary>Archive path relative to the game folder, e.g. "databigs/inxp_ui_40.big".</summary>
    [JsonPropertyName("file")] public string File { get; set; } = "";
    /// <summary>Folder (inside the profile folder) holding this archive's replacement PNGs.</summary>
    [JsonPropertyName("iconFolder")] public string IconFolder { get; set; } = "";
    /// <summary>Prefix added to a PNG's relative path to get the path inside the archive.</summary>
    [JsonPropertyName("pathPrefix")] public string PathPrefix { get; set; } = "";
    /// <summary>
    /// Built-in drawer that creates the PNGs from the game's own textures when the icon
    /// folder is empty (so no game artwork has to be distributed). Known: "ufc3".
    /// </summary>
    [JsonPropertyName("generator")] public string? Generator { get; set; }
    /// <summary>Flash UIs that carry both PS and Xbox button symbols.</summary>
    [JsonPropertyName("swfSwaps")] public List<SwfSwapSpec> SwfSwaps { get; set; } = new();
}

public sealed class SwfSwapSpec
{
    /// <summary>SWF paths inside the archive.</summary>
    [JsonPropertyName("files")] public List<string> Files { get; set; } = new();
    /// <summary>Name part of the symbols shown now, e.g. "_ps_".</summary>
    [JsonPropertyName("from")] public string From { get; set; } = "_ps_";
    /// <summary>Name part of the symbols to show instead, e.g. "_xb_".</summary>
    [JsonPropertyName("to")] public string To { get; set; } = "_xb_";
}

public sealed class GameProfile
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("titleIds")] public List<string> TitleIds { get; set; } = new();
    [JsonPropertyName("archives")] public List<ArchiveSpec> Archives { get; set; } = new();
    /// <summary>INI files of the port's launcher whose "game=" line points to the game folder.</summary>
    [JsonPropertyName("launcherConfigs")] public List<string> LauncherConfigs { get; set; } = new();
    /// <summary>Folders to try when nothing else is known.</summary>
    [JsonPropertyName("defaultPaths")] public List<string> DefaultPaths { get; set; } = new();

    [JsonIgnore] public string Folder { get; private set; } = "";
    [JsonIgnore] public string Id => Path.GetFileName(Folder);

    public static List<GameProfile> LoadAll(string iconsRoot)
    {
        var list = new List<GameProfile>();
        if (!Directory.Exists(iconsRoot)) return list;
        foreach (var dir in Directory.GetDirectories(iconsRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var json = Path.Combine(dir, "profile.json");
            if (!File.Exists(json)) continue;
            var p = JsonSerializer.Deserialize<GameProfile>(File.ReadAllText(json), new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) ?? throw new InvalidDataException($"Пустой профиль {json}");
            p.Folder = dir;
            list.Add(p);
        }
        return list;
    }

    /// <summary>PNG replacements: (path inside the archive, PNG file).</summary>
    public IEnumerable<(string ArchivePath, string Png)> Replacements(ArchiveSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.IconFolder)) yield break;
        var root = Path.Combine(Folder, spec.IconFolder);
        if (!Directory.Exists(root)) yield break;
        foreach (var png in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(root, png).Replace('\\', '/');
            yield return (spec.PathPrefix + rel[..^4] + ".gnf", png);
        }
    }

    /// <summary>Every change this profile makes to one archive (textures from PNG + SWF symbol swaps).</summary>
    public List<ArchivePatcher.Change> BuildChanges(ArchiveSpec spec, List<string> warnings)
    {
        var changes = new List<ArchivePatcher.Change>();
        var encoder = new Bc7Encoder();
        foreach (var (path, png) in Replacements(spec))
        {
            RgbaImage image;
            try { image = RgbaImage.Load(png); }
            catch (Exception ex) { warnings.Add($"{Path.GetFileName(png)}: не удалось прочитать PNG ({ex.Message})"); continue; }
            changes.Add(new ArchivePatcher.Change(path, (file, warn) =>
            {
                var gnf = GnfTexture.Parse(file);
                if (!gnf.IsSupported(out var why)) { warn.Add($"{path}: {why}"); return null; }
                if (image.Width != gnf.Width || image.Height != gnf.Height)
                    warn.Add($"{path}: размер PNG {image.Width}x{image.Height} приведён к {gnf.Width}x{gnf.Height}");
                return gnf.Encode(image, encoder);
            }));
        }
        foreach (var swap in spec.SwfSwaps)
            foreach (var swf in swap.Files)
                changes.Add(new ArchivePatcher.Change(swf, (file, warn) =>
                {
                    var result = SwfSymbolSwap.Apply(file, swap.From, swap.To, out int pairs);
                    if (pairs == 0) { warn.Add($"{swf}: нет пар символов {swap.From}/{swap.To}"); return null; }
                    return result;
                }));
        return changes;
    }

    /// <summary>The game folder itself, or a sub-folder of it, that this profile can patch.</summary>
    public string? ResolveGameFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        if (Matches(folder)) return folder;
        try
        {
            // e.g. a port folder with CUSAxxxxx-merged inside: prefer the merged one.
            foreach (var sub in Directory.GetDirectories(folder)
                         .OrderByDescending(d => d.EndsWith("-merged", StringComparison.OrdinalIgnoreCase)))
                if (Matches(sub)) return sub;
        }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    private bool Matches(string folder)
    {
        if (!Archives.All(a => File.Exists(Path.Combine(folder, a.File)))) return false;
        if (TitleIds.Count == 0) return true;
        var sfo = Path.Combine(folder, "sce_sys", "param.sfo");
        if (!File.Exists(sfo)) return true; // the archives are specific enough
        var text = Encoding.ASCII.GetString(File.ReadAllBytes(sfo));
        return TitleIds.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Best guess of the installed game folder.</summary>
    public string? AutoDetect()
    {
        foreach (var cfg in LauncherConfigs)
        {
            var path = Environment.ExpandEnvironmentVariables(cfg.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) continue;
            foreach (var line in File.ReadAllLines(path))
            {
                var t = line.Trim().TrimStart('﻿');
                if (!t.StartsWith("game=", StringComparison.OrdinalIgnoreCase)) continue;
                var found = ResolveGameFolder(t[5..].Trim());
                if (found != null) return found;
            }
        }
        foreach (var d in DefaultPaths)
        {
            var found = ResolveGameFolder(Environment.ExpandEnvironmentVariables(d));
            if (found != null) return found;
        }
        return null;
    }
}

namespace IconSwap.Core.Art;

/// <summary>
/// Creates a profile's replacement PNGs from the player's own game files. The repository
/// and releases therefore contain only code — never artwork extracted from a game.
/// </summary>
public static class IconGenerator
{
    public static bool CanGenerate(ArchiveSpec spec) =>
        string.Equals(spec.Generator, "ufc3", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(spec.IconFolder);

    public static bool NeedsIcons(GameProfile profile, ArchiveSpec spec) =>
        CanGenerate(spec) && !profile.Replacements(spec).Any();

    /// <summary>Draws the icons into the profile's icon folder; returns how many were written.</summary>
    public static int Generate(GameProfile profile, ArchiveSpec spec, string game)
    {
        if (!CanGenerate(spec)) return 0;
        // Reads the original textures even when the Xbox icons are already installed.
        var eb = ArchivePatcher.OpenOriginal(Path.Combine(game, spec.File));
        var made = new XboxArt(eb, spec.PathPrefix).RenderAll();
        var root = Path.Combine(profile.Folder, spec.IconFolder);
        foreach (var (rel, img) in made)
            img.Save(Path.Combine(root, rel + ".png"));
        return made.Count;
    }

    /// <summary>Generates every missing icon set of the profile.</summary>
    public static void EnsureIcons(GameProfile profile, string game, Action<string> log)
    {
        foreach (var spec in profile.Archives.Where(s => NeedsIcons(profile, s)))
        {
            log($"{spec.File}: рисую иконки Xbox из текстур игры...");
            int n = Generate(profile, spec, game);
            log($"{spec.File}: создано иконок — {n}");
        }
    }
}

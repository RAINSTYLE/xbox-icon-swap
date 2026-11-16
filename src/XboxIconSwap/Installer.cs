using IconSwap.Core;

namespace XboxIconSwap;

/// <summary>Install / restore / status for one game folder and profile.</summary>
internal static class Installer
{
    public static string IconsRoot => Path.Combine(AppContext.BaseDirectory, "Icons");

    public enum State { NotPatched, Patched, Partial }

    public static State GetState(GameProfile profile, string game)
    {
        int patched = profile.Archives.Count(a => ArchivePatcher.IsPatched(Path.Combine(game, a.File)));
        return patched == 0 ? State.NotPatched : patched == profile.Archives.Count ? State.Patched : State.Partial;
    }

    public static int CountIcons(GameProfile profile) => profile.Archives.Sum(a => profile.Replacements(a).Count());

    public static List<string> Install(GameProfile profile, string game, Action<string> log, Action<int, int> progress)
    {
        var warnings = new List<string>();
        IconSwap.Core.Art.IconGenerator.EnsureIcons(profile, game, log);
        foreach (var spec in profile.Archives)
        {
            var archive = Path.Combine(game, spec.File);
            var changes = profile.BuildChanges(spec, warnings);
            if (changes.Count == 0) { log($"{spec.File}: нечего заменять"); continue; }
            log($"{spec.File}: подготовка {changes.Count} файлов...");
            var result = ArchivePatcher.Patch(archive, changes, log, progress);
            warnings.AddRange(result.Warnings);
        }
        return warnings;
    }

    public static void Restore(GameProfile profile, string game, Action<string> log)
    {
        foreach (var spec in profile.Archives)
        {
            var archive = Path.Combine(game, spec.File);
            if (!ArchivePatcher.IsPatched(archive)) { log($"{spec.File}: уже оригинальный"); continue; }
            ArchivePatcher.Restore(archive);
            log($"{spec.File}: восстановлен оригинал");
        }
    }

    /// <summary>Turns low-level errors into advice the player can act on.</summary>
    public static string Explain(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Нет доступа к файлам игры. Запустите программу от имени администратора " +
                                       "или проверьте, что файлы не помечены «Только чтение».",
        IOException io when io.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021) =>
            "Файл игры занят другой программой. Закройте игру (и её лаунчер) и повторите.",
        _ => ex.Message,
    };
}

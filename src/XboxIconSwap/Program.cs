using IconSwap.Core;

namespace XboxIconSwap;

internal static class Program
{
    /// <summary>
    /// Без аргументов открывается окно. Для ярлыков/скриптов:
    ///   XboxIconSwap.exe --install "E:\Games\EA Sports UFC 3"
    ///   XboxIconSwap.exe --restore "E:\Games\EA Sports UFC 3"
    /// Код возврата 0 — успех; журнал пишется в XboxIconSwap.log рядом с программой.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 1 && (args[0] == "--install" || args[0] == "--restore"))
            return RunCommandLine(args[0], args.Length > 1 ? args[1] : null);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static int RunCommandLine(string command, string? folder)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "XboxIconSwap.log");
        using var logFile = new StreamWriter(logPath, append: true);
        void Log(string s) { logFile.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {s}"); logFile.Flush(); }
        try
        {
            foreach (var profile in GameProfile.LoadAll(Installer.IconsRoot))
            {
                var game = folder != null ? profile.ResolveGameFolder(folder) : profile.AutoDetect();
                if (game == null) continue;
                Log($"{profile.Name}: {game}");
                if (command == "--install")
                    foreach (var w in Installer.Install(profile, game, Log, (_, _) => { })) Log("Предупреждение: " + w);
                else
                    Installer.Restore(profile, game, Log);
                Log("Готово");
                return 0;
            }
            Log($"Игра не найдена: {folder ?? "(автопоиск)"}");
            return 2;
        }
        catch (Exception ex)
        {
            Log("Ошибка: " + Installer.Explain(ex));
            return 1;
        }
    }
}

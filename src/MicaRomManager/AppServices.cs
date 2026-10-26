using MicaRomManager.Data;
using MicaRomManager.Services;

namespace MicaRomManager;

/// <summary>轻量服务定位器，桌面小工具够用。</summary>
public static class AppServices
{
    public static PathService Paths { get; private set; } = null!;
    public static Database Db { get; private set; } = null!;
    public static GameRepository GameRepo { get; private set; } = null!;
    public static EmulatorRepository EmulatorRepo { get; private set; } = null!;
    public static SettingsRepository Settings { get; private set; } = null!;
    public static CoverStore Covers { get; private set; } = null!;
    public static RomLauncher Launcher { get; private set; } = null!;
    public static EmulatorInstaller Installer { get; private set; } = null!;
    public static EmulatorUpdater Updater { get; private set; } = null!;

    public static void Initialize()
    {
        Paths = new PathService();
        Db = new Database(Paths.DbPath);
        GameRepo = new GameRepository(Db);
        EmulatorRepo = new EmulatorRepository(Db);
        Settings = new SettingsRepository(Db);
        Covers = new CoverStore(Paths);
        Launcher = new RomLauncher(GameRepo);
        Installer = new EmulatorInstaller(Paths);
        Updater = new EmulatorUpdater();
    }
}

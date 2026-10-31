using MicaRomManager.Data;
using MicaRomManager.Services;
using MicaRomManager.Services.Ai;
using MicaRomManager.Services.Devices;
using MicaRomManager.Services.Sending;

namespace MicaRomManager;

/// <summary>轻量服务定位器，桌面小工具够用。</summary>
public static class AppServices
{
    public static PathService Paths { get; private set; } = null!;
    public static Database Db { get; private set; } = null!;
    public static GameRepository GameRepo { get; private set; } = null!;
    public static EmulatorRepository EmulatorRepo { get; private set; } = null!;
    public static SettingsRepository Settings { get; private set; } = null!;
    public static LookupRepository Lookups { get; private set; } = null!;
    public static PlatformRepository Platforms { get; private set; } = null!;
    public static NetworkClientProvider Network { get; private set; } = null!;
    public static AiChatClient Ai { get; private set; } = null!;
    public static AiSearchService Search { get; private set; } = null!;
    public static CoverStore Covers { get; private set; } = null!;
    public static RomLauncher Launcher { get; private set; } = null!;
    public static EmulatorInstaller Installer { get; private set; } = null!;
    public static EmulatorUpdater Updater { get; private set; } = null!;
    public static DeviceProfileStore DeviceProfiles { get; private set; } = null!;
    public static SendTaskQueue SendQueue { get; private set; } = null!;

    public static void Initialize()
    {
        Paths = new PathService();

        // 平台展示名（PS1 → PlayStation）来自预置清单，界面用，不写库
        try
        {
            Models.PlatformDisplayNames.Initialize(PlatformPresetStore.LoadShipped());
        }
        catch
        {
            // 清单缺失不影响启动
        }

        Db = new Database(Paths.DbPath);
        GameRepo = new GameRepository(Db);
        EmulatorRepo = new EmulatorRepository(Db);
        Settings = new SettingsRepository(Db);

        // 主机命名习惯（日式 Famicom / 欧美 NES），只影响界面显示
        Models.PlatformDisplayNames.SetNaming(
            Models.PlatformDisplayNames.ParseNaming(Settings.Get(Models.PlatformDisplayNames.NamingKey)));

        Lookups = new LookupRepository(Db);
        Platforms = new PlatformRepository(Db);
        Network = new NetworkClientProvider(Settings);
        Ai = new AiChatClient(Settings, Network);
        Search = new AiSearchService();
        Covers = new CoverStore(Paths);
        Launcher = new RomLauncher(GameRepo);
        Installer = new EmulatorInstaller(Paths);
        Updater = new EmulatorUpdater(Network);
        DeviceProfiles = new DeviceProfileStore(Paths.DevicesDir, PathService.DeviceProfilesPath);
        SendQueue = new SendTaskQueue();

        // 旧库里的绝对路径统一规范化为 %DATA% 相对形式（幂等）
        try
        {
            EmulatorRepo.NormalizePaths();
        }
        catch
        {
            // 规范化失败不影响启动
        }

        // 早期版本把 API Key 明文存进 settings，启动时就地加密（幂等）
        try
        {
            SecretProtector.Migrate(Settings, AiOptions.ApiKeyKey);
        }
        catch
        {
            // 迁移失败不影响启动，下次保存时仍会加密
        }
    }

    /// <summary>当前库目录（已按便携模式展开为绝对路径）。</summary>
    public static string LibraryRoot => PortablePath.Expand(Settings.Get("library.root") ?? "");

    /// <summary>代理设置变更后重建 HTTP 客户端。</summary>
    public static void RebuildNetwork()
    {
        Network.Rebuild();
        Ai.Rebuild();
        Updater = new EmulatorUpdater(Network);
    }
}

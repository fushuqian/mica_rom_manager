# 修复模拟器下载 + 代理设置 + 列表文字对齐

## 1. 概述

针对老板提出的三个问题：

1. **模拟器下载不能用** → 在程序内直接下载 GitHub Release 的 zip 资产并自动安装（不再只跳浏览器）。
2. **GitHub 访问不畅** → 增加网络代理设置，支持「不使用 / 跟随系统代理 / 手动 HTTP 代理」三种模式，代理同时作用于检查更新与下载。
3. **列表单元格文字高低不一** → 统一 `DataGridTextColumn` 的元素样式，让所有列文字垂直居中。

已确认决策：下载走「应用内直接下载并安装」；代理提供三种模式可切换。

## 2. 现状分析

* [EmulatorUpdater.cs](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Services/EmulatorUpdater.cs#L16-L24)：内部 `new HttpClient()`，不读任何代理设置；只实现了 `CheckAsync`，没有下载能力。
* [EmulatorManagerViewModel.cs](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/ViewModels/EmulatorManagerViewModel.cs#L222-L254)：`CheckUpdateAsync` 成功后仅询问是否用浏览器打开 Release 页面，GitHub 不通时等于「下载不能用」。
* [AppServices.cs](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/AppServices.cs#L19-L30)：启动时一次性构造 `Updater`，没有重建机制。
* [SettingsRepository.cs](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Data/SettingsRepository.cs) 已支持 `Get/Set/GetBool`，可直接存代理键。
* [SettingsWindow.xaml](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Views/SettingsWindow.xaml#L19-L60)：常规 Tab 为固定行网格 + code-behind 读写设置，新增代理区块沿用此模式。
* [PathService.cs](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Services/PathService.cs) 管理数据子目录，可新增 `downloads` 存放下载的安装包。
* [EmulatorInstaller.cs](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Services/EmulatorInstaller.cs#L26-L41)：`InstallFromZip` 已实现解压 → 旧目录移到 `.backup` → 复制 → 找 exe，可直接复用。
* 对齐问题根因：[Theme.xaml](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Themes/Theme.xaml#L92-L95) 有 `DataGridTextStyle`（`VerticalAlignment=Center`），但 [MainWindow.xaml](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/MainWindow.xaml#L134-L165) 只有「标题/标签/文件路径」用了它；其余文本列生成的 `TextBlock` 默认 `Stretch`，在行高 44 下文字贴在顶部。`EmulatorManagerView`、`SharePackWindow`、`ImportPreviewWindow` 的文本列全部没有该样式。

## 3. 改动方案

### 3.1 网络代理基础设施（新增）

**新增 `src/MicaRomManager/Services/NetworkClientProvider.cs`**

* 构造入参：`SettingsRepository`。
* 设置键：
  * `network.proxy.mode`：`none` | `system` | `manual`，默认 `system`。
  * `network.proxy.address`：手动模式地址，默认 `127.0.0.1:7890`。
* 内部字段：`HttpClient _api`（超时 30 秒）、`HttpClient _transfer`（超时 30 分钟，仅下载用）。
* 公共成员：
  * `HttpClient ApiClient => _api;`
  * `HttpClient TransferClient => _transfer;`
  * `void Rebuild()`：释放旧实例并按当前设置重建。
  * `static bool TryParseProxyAddress(string? address, out string host, out int port)`：接受 `127.0.0.1:7890`、`http://127.0.0.1:7890`，端口 1–65535，便于单测。
* 构建规则（`CreateHandler()`）：
  * `none` → `handler.UseProxy = false; handler.Proxy = null;`
  * `system` → `handler.UseProxy = true; handler.Proxy = null;`（.NET 自动读取 Windows 系统代理）
  * `manual` → `handler.UseProxy = true; handler.Proxy = new WebProxy($"http://{host}:{port}");`
  * 地址非法时退回 `system` 并记录到 `Debug.WriteLine`。
* 两个 client 均设置 `DefaultRequestHeaders.UserAgent.ParseAdd("MicaRomManager/0.2")` 与 `Accept: application/vnd.github+json`。

**修改 `AppServices.cs`**

* 新增 `public static NetworkClientProvider Network { get; private set; }`。
* `Initialize()` 中：`Network = new NetworkClientProvider(Settings);`，再把 `Updater = new EmulatorUpdater(Network);`。
* 新增 `public static void RebuildNetwork()`：`Network.Rebuild(); Updater = new EmulatorUpdater(Network);`（设置保存后调用）。

**修改 `PathService.cs`**

* 新增 `public string DownloadsDir => Path.Combine(DataDir, "downloads");`，并在构造函数里 `Directory.CreateDirectory(DownloadsDir);`。

### 3.2 模拟器下载与安装

**修改 `Services/EmulatorUpdater.cs`**

* 构造函数改为 `public EmulatorUpdater(NetworkClientProvider network)`；`CheckAsync` 内改用 `network.ApiClient`（不再持有自己的 HttpClient）。
* `CheckAsync` 的资产挑选保持不变（优先 `.zip`），但返回值保留 `AssetName/AssetUrl` 供下载使用。
* 新增：
  ```csharp
  public async Task<(bool Success, string Message, long Bytes)> DownloadAsync(
      string url, string targetFile, IProgress<double>? progress, CancellationToken ct)
  ```
  * 用 `network.TransferClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)`；
  * 按 `Content-Length` 计算百分比（未知长度时回调 -1，即不确定进度）；
  * 流式写入目标文件（先写 `.part`，完成后 `File.Move` 覆盖），失败删除 `.part` 并返回错误信息；
  * 非 2xx、超时、IO 异常均返回 `Success=false` 与中文提示。

**修改 `ViewModels/EmulatorManagerViewModel.cs`**

* 新增可观察属性：`IsBusy`（默认 false）、`DownloadProgress`（double，0–100）。
* `CheckUpdateAsync` 改为：
  1. 检查成功且存在可用资产（优先 `.zip`，其次 `.7z`）→ 弹窗询问「发现新版本 {tag}，是否下载并安装？」。
  2. 用户选是 → 调 `DownloadAndInstallAsync(emulator, result)`；选否 → 逻辑结束。
  3. 无可安装资产（如只有 .exe/.dmg）→ 沿用现有「打开 Release 页面」兜底。
* 新增私有方法 `DownloadAndInstallAsync`：
  * 目标文件：`Path.Combine(AppServices.Paths.DownloadsDir, $"{SanitizeRepoName(repo)}-{tag}.zip|.7z")`；
  * `Progress<double>` 在 UI 线程构造，回写 `DownloadProgress` 与 `StatusMessage`（形如「下载中 42%」）；
  * 下载成功 → `AppServices.Installer.InstallFromArchive(file, emulator.Name)` → 复用现有 `FinishInstall(emulator, result)`；
  * 安装成功后：`emulator.Version = result.Tag?.TrimStart('v','V') ?? emulator.Version;`，`AppServices.EmulatorRepo.Save(emulator);`，`Reload()`；
  * 下载的安装包保留在 `data\downloads`，状态栏提示路径（不自动删除，符合规则 11）；
  * `finally` 中复位 `IsBusy/DownloadProgress`。

**修改 `Services/EmulatorUpdater.cs`（资产选择）**

* 资产优先顺序：`.zip` → `.7z` → 其它（不自动安装，仅能打开页面）。
* `UpdateCheckResult` 增加 `AssetUrl/AssetName` 即可满足，无需新增字段。

**修改 `Services/EmulatorInstaller.cs` + `MicaRomManager.csproj`（支持 .7z）**

* 新增 NuGet 依赖 `SharpCompress`（MIT，纯托管，用于 7z 解压；版本按 `dotnet add package SharpCompress` 实际解析到的稳定版为准）。
* 新增 `public InstallResult InstallFromArchive(string archivePath, string emulatorName)`：
  * `.zip` → 沿用 `InstallFromZip`；
  * `.7z` → 用 `SharpCompress.Archives.ArchiveFactory.Open` 解到临时目录，逐条 `entry.WriteToDirectory`，保留相对路径并校验解压后路径仍在临时目录内（防目录穿越），再复用 `PrepareTarget + CopyDirectory + FindExeCandidates`；
  * 其它扩展名抛 `NotSupportedException("仅支持 zip / 7z")`。
* `InstallFromZip` 保持原实现，供「安装（zip）」按钮继续使用。

**修改 `Views/EmulatorManagerView.xaml`**

* 第 3 行状态区改为：
  * `TextBlock`（现有 `StatusMessage`）+ `ProgressBar`（`Value="{Binding DownloadProgress}"`、`Maximum=100`、`Visibility="{Binding IsBusy, Converter={StaticResource BoolToVis}}"`、高 4）。
* 按钮文案：`检查 GitHub 更新` → `检查更新 / 下载安装`（仍绑定 `CheckUpdateCommand`）。

### 3.3 代理设置界面

**修改 `Views/SettingsWindow.xaml`（常规 Tab）**

* 在「库目录」与「保存常规设置」之间插入网络代理区块：
  * 标签「网络代理」+ `ComboBox x:Name="ProxyModeCombo"`（不使用代理 / 跟随系统代理 / 手动代理，`SelectionChanged="ProxyMode_Changed"`）；
  * 标签「代理地址」+ `TextBox x:Name="ProxyAddressBox"`（占位提示 `127.0.0.1:7890`），仅手动模式可用；
  * `Button Content="测试 GitHub 连接" Click="TestNetwork_Click"` + `TextBlock x:Name="NetworkTestText"` 显示结果。
* 行定义相应增加 2 行，保存按钮下移。

**修改 `Views/SettingsWindow.xaml.cs`**

* `LoadGeneral()` 读取两个键：mode → 选中索引（none=0/system=1/manual=2），address → 文本框。
* `SaveGeneral_Click()` 写入两键后调用 `AppServices.RebuildNetwork();`，提示文案不变。
* `ProxyMode_Changed()`：手动模式启用地址框，其余禁用。
* `TestNetwork_Click()`（`async void`）：校验手动地址 → 用 `AppServices.Network.ApiClient` 请求 `https://api.github.com/rate_limit`（10 秒超时），成功显示「连接正常（HTTP 200）」，失败显示异常消息。

### 3.4 列表文字垂直居中

**统一做法**：给所有 `DataGridTextColumn` 补 `ElementStyle="{StaticResource DataGridTextStyle}"`（该样式已含 `VerticalAlignment=Center` 与省略号）。

* `MainWindow.xaml`：★、平台、类型、年份、状态、次数、时长、最近游玩、大小（标题/标签/文件路径已有）。
* `Views/EmulatorManagerView.xaml`：名称、平台、默认平台、版本、可执行文件、状态。
* `Views/SharePackWindow.xaml`：游戏、平台、模拟器、备注。
* `Views/ImportPreviewWindow.xaml`：文件名、标题、大小；平台列的 `ComboBox` 模板加 `VerticalAlignment="Center"`。
* `Theme.xaml` 不动（`GameCellStyle` 已设 `VerticalContentAlignment=Center`）。

### 3.5 版本与文档

* `MicaRomManager.csproj` 版本 `0.1.0` → `0.2.0`。
* 更新 `CHANGELOG.md`（新增代理、应用内下载、对齐修复）、`MEMORIES.md`（`WebProxy` 用法与 DataGrid 文本对齐坑）、`QUEST.md`（追加 Q-002 记录本次三项修复）。
* 变更完成后按规则 5 提交并推送到 `https://github.com/fushuqian/mica_rom_manager`（origin/main）。

## 4. 假设与决策

* GitHub Release 资产优先 `.zip`，其次 `.7z`（用 SharpCompress 解压安装）；其它格式（如 .exe/.dmg）不自动安装，提示打开 Release 页面。
* 下载目录固定在 `data\downloads`，安装包保留供复查。
* 代理只作用于本程序的 GitHub API 与下载；浏览器兜底页面由系统/浏览器自身代理决定。
* 「跟随系统代理」依赖 .NET 默认行为（读取 IE/WinINet 代理），不额外解析 PAC。
* 不引入第三方网络库，继续用 `System.Net.Http`。

## 5. 验证步骤

1. `dotnet build MicaRomManager.sln -c Debug`：0 错误、0 警告。
2. 新增单元测试 `ProxyAddressTests`（`TryParseProxyAddress` 合法/非法输入），`dotnet test` 全绿（现有 28 + 新增）。
3. 启动程序 → 设置 → 常规：
   * 选「手动代理」填 `127.0.0.1:7890` → 保存 → 「测试 GitHub 连接」显示结果；
   * 选「不使用代理」→ 保存 → 再测试，确认行为随之变化。
4. 模拟器管理：选 RetroArch → 「检查更新 / 下载安装」→ 无可用资产时给出兜底提示；有 zip/7z 时进度条走动并完成安装，旧目录出现在 `data\emulators\.backup`。
5. 新建一条测试记录（ROM 路径可填任意本地文件）→ 截图核对所有列文字垂直居中、行高一致。
6. 更新文档并提交 git。

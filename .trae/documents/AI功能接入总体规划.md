# Mica ROM Manager AI 功能接入总体规划

## 一、目标与总原则

按老板确认的方向，分 5 个独立可验收的阶段接入 AI，统一走 **云端 OpenAI 兼容接口**（`/chat/completions`，用户自填 BaseUrl / API Key / 模型），不引入新的重型 SDK 或第三方 AI 框架。

总原则：

1. **只发文本，不发文件**：不读取、不上传 ROM 内容、封面、存档；设备识别只上传目录名与扩展名统计。
2. **先预览、后写库**：所有 AI 结果进入预览窗口，用户勾选确认后才落库/改文件。
3. **默认只补空字段**：覆盖已有值必须显式勾选。
4. **Key 只存本地**：存 `settings` 表，界面掩码显示，不写日志、不写仓库文档。
5. **失败不自动重试**：逐条记录原因并跳过，不影响已成功条目与数据库。
6. **费用可控**：单次批量上限 50 条；超过 10 条先弹确认；不做后台自动循环。
7. **AI 不执行危险操作**：不自动启动游戏、不删除文件、不自由生成 SQL/Shell。
8. **未配置 Key 时入口禁用**：按钮置灰并提示去设置页配置。

## 二、现状分析

- UI：WPF + .NET 9，主界面工具栏在 [MainWindow.xaml](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/MainWindow.xaml)，命令集中在 [MainViewModel.cs](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/ViewModels/MainViewModel.cs)（CommunityToolkit `[RelayCommand]`）。
- 设置：`TabControl` 分页在 [SettingsWindow.xaml](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Views/SettingsWindow.xaml)，值读写走 [SettingsRepository.cs](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Data/SettingsRepository.cs)（`settings` 键值表）。
- 网络：已有带三态代理的 [NetworkClientProvider.cs](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Services/NetworkClientProvider.cs)，可直接复用其 `ApiClient`。
- 数据：游戏字段齐全（标题/平台/类型/区域/语言/年份/发行商/开发商/简介/标签），仓储为 [GameRepository.cs](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Data/GameRepository.cs)。
- 可复用流程：批量编辑预览、`CoverStore`、[LibraryOrganizer.cs](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Services/LibraryOrganizer.cs)（安全改名 + 重名避让）、[DeviceProfileStore.cs](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Services/Devices/DeviceProfileStore.cs)（设备 JSON 配置）。
- 现状缺口：没有 AI 设置、没有统一聊天客户端、没有刮削/翻译/自然语言查询/设备识别代码。当前版本 `0.16.5`，路线图已预留「M3 AI 刮削」。

## 三、版本路线总览

| 版本 | 阶段 | 内容 | 优先级 | 对外依赖 |
| --- | --- | --- | --- | --- |
| v0.17.0 | P-A | AI 基础设施 + AI 元数据刮削（含本地文件名解析兜底） | 高 | 用户自备 BaseUrl/Key |
| v0.18.0 | P-B | 自然语言查库 / 受控操作 | 高 | 同上 |
| v0.19.0 | P-C | AI 标签/分类 + 本地相似推荐 | 中 | 同上 |
| v0.20.0 | P-D | AI 标题翻译 / 文件名整理 | 中 | 同上 |
| v0.21.0 | P-E | AI 学习新设备存储结构 | 中低 | 同上 + 目标盘符 |

每阶段独立提交、独立验收；不一次性全部实现。

---

## 四、P-A：AI 基础设施 + 元数据刮削（v0.17.0）

### 4.1 新增 AI 基础设施

新增目录 `src/MicaRomManager/Services/Ai/`：

**`AiOptions.cs`**

```csharp
public sealed class AiOptions
{
    public string BaseUrl { get; init; } = "";      // ai.base_url
    public string ApiKey { get; init; } = "";       // ai.api_key
    public string Model { get; init; } = "";        // ai.model
    public int TimeoutSeconds { get; init; } = 60;  // ai.timeout_seconds
    public int BatchLimit { get; init; } = 50;      // ai.batch_limit
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl)
                             && !string.IsNullOrWhiteSpace(ApiKey)
                             && !string.IsNullOrWhiteSpace(Model);
    public static AiOptions Load(SettingsRepository settings);
}
```

**`AiChatClient.cs`**

- 构造函数注入 `NetworkClientProvider` + `SettingsRepository`；每次调用从设置读取最新配置。
- `Task<AiChatResult> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken token)`。
- 请求体：`{ "model": ..., "messages": [...], "temperature": 0, "max_tokens": 1024 }`。
- 不使用 `response_format`（部分兼容端点不支持）；由 `AiJson.Parse<T>` 容错解析（去掉 ```json 围栏，取首个 `{` 到末个 `}`）。
- 超时按 `ai.timeout_seconds`，用 `CancellationTokenSource` 叠加；不自动重试。
- 错误分类并给出中文提示：401 → Key 无效；404 → BaseUrl/模型名错误；429 → 额度/限流；超时/网络 → 检查代理。
- 不在异常文本与日志中输出 Key。

**`AiJson.cs`**

- 统一 JSON 提取与反序列化；解析失败返回 `null` 和原始片段（截断 200 字）用于预览提示。
- `JsonSerializerOptions`：`PropertyNameCaseInsensitive=true`、允许注释与尾逗号。

**`AiPrompts.cs`**

- 集中存放各阶段 system prompt 与字段约束，便于后续调优；版本号常量 `PromptVersion`。

### 4.2 设置页新增「AI」页

修改 [SettingsWindow.xaml](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Views/SettingsWindow.xaml) 与 [SettingsWindow.xaml.cs](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Views/SettingsWindow.xaml.cs)：

- 新增 `TabItem Header="AI"`，放在「设备」之后、「数据维护」之前。
- 控件：BaseUrl、API Key（`PasswordBox` + 显示/隐藏切换）、模型名、超时秒数、单次批量上限、预设下拉、测试连接按钮、保存按钮。
- 预设（仅填入 BaseUrl 与模型，不填 Key）：
  - DeepSeek：`https://api.deepseek.com/v1` + `deepseek-chat`
  - Kimi：`https://api.moonshot.cn/v1` + `moonshot-v1-8k`
  - OpenAI：`https://api.openai.com/v1` + `gpt-4o-mini`
  - 自定义：留空
- 测试连接：发送一条 `「只回复 OK」` 的最小请求，显示耗时与模型返回；失败显示 4.1 的分类提示。
- 保存键：`ai.base_url`、`ai.api_key`、`ai.model`、`ai.timeout_seconds`、`ai.batch_limit`。
- 提示文案：Key 仅存本地 SQLite；只发送游戏文本信息，不上传 ROM。

### 4.3 刮削数据模型与本地解析

**`src/MicaRomManager/Models/ScrapeResult.cs`**

```csharp
public sealed class ScrapeResult
{
    public long GameId { get; set; }
    public string Title = "";
    public string Genre = "";
    public string Region = "";
    public string Language = "";
    public int? ReleaseYear;
    public string Publisher = "";
    public string Developer = "";
    public string Summary = "";     // 对应 Notes，仅空时写入
    public string Source = "";      // 文件名 / AI
    public string Error = "";       // 失败原因，预览里显示
    public bool Apply { get; set; } = true;
    public bool Changed(Game current) => ...; // 与当前记录比较
}
```

**`src/MicaRomManager/Services/Scraping/FileNameScraper.cs`**（本地兜底，零成本）

- 规则按已有路线图 M1：扩展名去除；`(USA)/(Europe)/(Japan)/(World)/(China)/(Korea)/(Taiwan)/(Hong Kong)` → 区域词典值；`(En,Fr,De)` → 语言词典值；`(1998)` → 年份；`(Rev 1)/(Beta)/(Proto)` → 备注候选；`Disc 1/CD2` 从标题移除；`_`/`.` 转空格。
- 只产生「建议值」；不改库。
- 复用 `lookup_items` 现有词典值做映射。

### 4.4 AI 刮削 Provider

**`src/MicaRomManager/Services/Scraping/AiMetadataProvider.cs`**

- 输入单条 `Game`，输出 `ScrapeResult`。
- 请求只发：文件名、平台、现有标题、现有年份（可选）。**不发路径、不发文件内容。**
- 要求模型返回严格 JSON：
  ```json
  {"title":"","genre":"","region":"","language":"","release_year":1994,
   "publisher":"","developer":"","summary":""}
  ```
- 后处理校验：
  - `title` 必填，去除引号/空白；
  - `release_year` 限定 1970–当前年+2，否则置空；
  - `region/language/genre` 先做常见别名映射（USA→美版、Japan→日版、Action→动作 等），无法映射则保留原文并标记「待确认」；
  - 空字符串统一转 `""`。
- 单次运行上限 `ai.batch_limit`；超过时提示分批，不自动截断后静默继续。

**`src/MicaRomManager/Services/Scraping/ScraperService.cs`**

- 流程：对选中游戏先跑 `FileNameScraper`，再按需跑 AI。
- 合并策略：
  - 「只补空字段」模式：本地结果先填空；AI 只填仍为空的字段。
  - 「覆盖已有值」模式：AI 结果优先，本地结果兜底。
- 并发 2，`SemaphoreSlim` 控制；进度回调 `IProgress<ScrapeProgress>`。
- 单条失败写入 `Error`，继续下一条。

### 4.5 刮削预览窗口

**新增 `Views/ScrapePreviewWindow.xaml` + `.xaml.cs` + `ViewModels/ScrapePreviewViewModel.cs`**

- 顶部：数据源说明（文件名解析 / AI）、模式单选（只补空 / 覆盖）、开始按钮、进度条、已用请求数。
- DataGrid 列：勾选、标题、类型、区域、语言、年份、发行商、开发商、简介、来源、错误。
- 底部：取消、应用；显示「将应用 N 条」。
- 应用：逐条 `GameRepo.Update`，刷新主列表与筛选；失败条目汇总提示。

**入口**：

- 主界面工具栏新增「AI 刮削」按钮（图标 + 文字），右键菜单新增「刮削信息…」。
- 多选可用；未配置 AI 时按钮可用但仍可跑本地文件名解析，AI 部分置灰并提示。
- 在 [MainViewModel.cs](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/ViewModels/MainViewModel.cs) 新增 `ScrapeCommand`。
- 修改 [MainWindow.xaml](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/MainWindow.xaml) 工具栏与右键菜单。

### 4.6 验收点

- 配置 DeepSeek 兼容端点后，`Super Metroid (Japan) (Rev 1).sfc` 能补出中文标题/类型/年份/发行商，区域为「日版」，备注保留版本信息。
- 已有字段默认不被覆盖；勾选覆盖后才改写。
- 未配置 Key：本地解析可用，AI 部分提示去设置。
- 网络失败/401/429：预览中逐条显示原因，数据库不变。

---

## 五、P-B：自然语言查库 / 受控操作（v0.18.0）

### 5.1 设计决策

**不做自由 SQL**。模型只输出受控 JSON 查询计划，由本地代码校验并映射到现有筛选字段，避免注入与误操作。

**`src/MicaRomManager/Models/AiQueryPlan.cs`**

```csharp
public sealed class AiQueryPlan
{
    public string Intent { get; set; } = "filter"; // filter | action
    public AiQueryFilter Filter { get; set; } = new();
    public string SortField { get; set; } = "";     // title/year/rating/play_count
    public string SortDirection { get; set; } = "asc";
    public AiQueryAction? Action { get; set; }
    public string Explanation { get; set; } = "";
}

public sealed class AiQueryFilter
{
    public string Platform = "", Status = "", Region = "", Language = "",
        Genre = "", Developer = "", Publisher = "", Tag = "", Keyword = "";
    public bool? Favorite;
    public bool OnlyMissing;
    public int? YearMin, YearMax;
}

public sealed class AiQueryAction
{
    public string Type = "";   // set_status | toggle_favorite | add_tag | remove_tag
    public string Value = "";
}
```

### 5.2 查询服务

**`src/MicaRomManager/Services/Ai/AiQueryService.cs`**

- 输入自然语言 + 当前库内去重后的可选值（平台、状态、区域、语言、类型、开发商、标签），让模型从中选择，降低幻觉。
- Prompt 要求只返回上述 JSON，不返回 SQL、不返回解释以外的内容。
- 校验规则：
  - `Intent` 只允许 `filter/action`；
  - 筛选值必须在已知词典或允许的自由文本字段内；
  - `Action.Type` 只允许 4 种；`set_status` 的值必须在 `GameStatus.All`；
  - `add_tag/remove_tag` 的值长度 ≤ 20、不含换行；
  - 校验失败直接提示「无法理解，请换个说法」，不执行任何操作。
- 返回给 UI：命中数量、筛选条件摘要、操作摘要。

### 5.3 UI 与接线

**新增 `Views/AiQueryWindow.xaml` + `.xaml.cs` + `ViewModels/AiQueryViewModel.cs`**

- 输入框 + 示例语句（「找出所有未通关的 PS1 游戏」「把 SNES 里没玩过的加标签 待补」）。
- 结果区：解释、筛选摘要、预计影响条数；「应用到列表」/「执行操作」按钮。
- 执行操作前二次确认，显示将影响的条数与范围。

**主界面入口**：

- 搜索框右侧新增「AI 查询」小按钮，点击打开窗口。
- [MainViewModel.cs](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/ViewModels/MainViewModel.cs) 新增 `AiQueryCommand` 与 `ApplyAiFilter(AiQueryPlan)`：
  - 映射到现有 `SelectedPlatform / SelectedStatus / SelectedRegion / SelectedLanguage / SelectedDeveloper / SelectedTag / OnlyFavorites / OnlyMissing / SearchText`；
  - 年份范围与类型若现有筛选没有对应项，则在 `ApplyFilter()` 里增加临时条件字段（仅本次会话有效），不改 UI 布局。
- 操作类任务只作用于**当前筛选结果**，不作用于全库；执行后刷新列表。

### 5.4 验收点

- 「找出所有未通关的 PS1 游戏」→ 列表筛出对应结果，条数正确。
- 「把结果全部标记为已通关」→ 二次确认后写库，列表刷新。
- 输入「删除所有 ROM 文件」→ 明确拒绝，不执行任何文件操作。
- 未配置 AI 时按钮禁用并提示。

---

## 六、P-C：AI 标签/分类 + 本地相似推荐（v0.19.0）

### 6.1 AI 标签

**`src/MicaRomManager/Services/Ai/AiTagService.cs`**

- 输入：标题、平台、类型、现有标签；输出：`{"tags":["…"],"genre":"…"}`。
- 约束：标签 ≤5 个；优先使用 `lookup_items(kind='tag')` 已有值；不重复现有标签；单条长度 ≤12 字。
- 逐条请求，上限同 `ai.batch_limit`；失败跳过并记录。

**新增 `Views/AiTagPreviewWindow.xaml` + VM**

- 列：勾选、游戏、原标题类型、建议标签、建议类型、来源/错误。
- 应用模式：追加（默认）/ 替换。
- 应用后 `GameRepo.Update`，刷新标签筛选项。

**入口**：主界面工具栏/右键菜单「AI 标签…」。

### 6.2 本地相似推荐（不耗 API）

**`src/MicaRomManager/Services/Recommendation/RecommendationService.cs`**

- 纯本地打分，不调用 AI：
  - 同平台 +2；同开发商 +1；同类型 +1；共享标签每个 +1（上限 3）；同区域 +0.5；未玩过 +1；已通关 -1。
- 以当前选中游戏为种子，排除自身，返回前 10 条并给出理由文本。
- 无选中时以「在玩」游戏为种子；仍无则随机展示高分未玩。

**新增 `Views/RecommendWindow.xaml` + VM**：列表展示「游戏 / 平台 / 理由 / 分数」，双击定位到主列表。

**入口**：工具栏「推荐」按钮。

### 6.3 验收点

- 选中一个 RPG，能推荐同平台/同开发商/同标签的未玩游戏，理由可读。
- AI 标签结果默认只追加、不覆盖人工标签；未勾选条目不写库。
- 推荐功能在无网络时仍可用。

---

## 七、P-D：AI 标题翻译 / 文件名整理（v0.20.0）

### 7.1 翻译服务

**`src/MicaRomManager/Services/Ai/AiTitleTranslator.cs`**

- 输入：原标题、平台、区域；输出：`{"title":"中文常用名","note":"…"}`。
- 规则：
  - 优先官方中文名/通用译名；无通用译名时保留原英文并在 `note` 标注；
  - 不输出拼音、不加书名号、不加解释性长句；
  - 保留系列编号与副标题结构。
- 逐条请求，上限同 `ai.batch_limit`；不自动写库。

### 7.2 预览与执行

**新增 `Views/AiRenamePreviewWindow.xaml` + VM**

- 两段式操作，用户在窗口内选择：
  1. **只改标题**：仅写 `Title`，不动文件；
  2. **改标题并整理文件**：写库后调用 `LibraryOrganizer.Organize` 重命名库内文件。
- 列：勾选、原标题、新标题、原文件名、新文件名、状态（可整理 / 同名跳过 / 库外跳过 / 失败）。
- 新文件名统一由 `LibraryOrganizer.BuildFileName` 生成，遵守现有 `标题 [Disc-n] (区域)(语言)(特注).扩展名` 规则；不自行拼接。
- 只重命名位于库目录内的文件（`LibraryOrganizer.IsUnderDirectory`）；链接入库的库外文件只改标题，不动文件。
- 重名由 `ResolveUniqueName` 避让；不覆盖、不删除。
- 执行前二次确认，明确「将重命名 N 个文件，无法自动撤销」。

### 7.3 验收点

- `Super Mario World (USA).sfc` → 标题「超级马里奥世界」，文件名按现有规则整理；区域/语言/特注保持不变。
- 库外链接文件只改标题，文件路径不变。
- 同名冲突显示「同名跳过」，不覆盖已有文件。
- 失败文件单独列出，其余继续。

---

## 八、P-E：AI 学习新设备存储结构（v0.21.0）

### 8.1 目标

连接一个陌生的 U 盘 / 掌机存储后，扫描其目录结构，由 AI 推断 `DeviceProfile`（根目录、目录模板、平台映射），用户预览确认后保存为 `data\devices\*.json`，以后可复用。

### 8.2 扫描器

**`src/MicaRomManager/Services/Devices/DriveTreeScanner.cs`**

- 输入：盘符根路径。
- 只读扫描，限制：
  - 最多 3 层目录、最多 400 个目录节点；
  - 跳过 `$RECYCLE.BIN`、`System Volume Information`、隐藏/系统目录；
  - 只统计每个目录的文件数与前 5 种扩展名；
  - **不读取文件内容、不上传文件名**，只上传目录名与扩展名统计。
- 输出 `DeviceScanSummary`：
  ```csharp
  public sealed class DeviceScanSummary
  {
      public string DriveRoot = "";
      public string VolumeLabel = "";
      public long TotalBytes, FreeBytes;
      public List<ScanNode> Nodes = new(); // RelPath, FileCount, TopExtensions
  }
  ```
- 扫描在后台线程执行，可取消；无权限目录跳过并计数。

### 8.3 AI 推断器

**`src/MicaRomManager/Services/Devices/AiDeviceProfiler.cs`**

- 输入：`DeviceScanSummary` + 现有平台名列表 + `DeviceProfile` JSON 结构说明。
- 输出 JSON：
  ```json
  {"name":"","brand":"","model":"","transport":"drive","rootFolder":"Roms",
   "folderTemplate":"{root}/{platform}","allowUnmappedPlatforms":true,
   "platformFolders":{"PlayStation":"PS"},"notes":"","confidence":"high"}
  ```
- 校验（任何一项失败都不保存，列出原因供手动修正）：
  - `rootFolder` 必须是扫描到的顶层目录之一（忽略大小写）；
  - `folderTemplate` 必须包含 `{root}`，且不含 `..`、盘符、绝对路径；
  - `platformFolders` 的 key 必须在平台词典中，value 必须是扫描到的目录名；
  - `transport` 固定 `drive`（MTP 未支持）；
  - 目录名不得含 `Path.GetInvalidFileNameChars()`。
- 置信度低于 `medium` 时在 UI 顶部显示警示，建议人工核对。
- 只生成建议，不直接写文件。

### 8.4 UI 与入口

- 修改 [DeviceSettingsView.xaml](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Views/DeviceSettingsView.xaml)：新增「AI 识别目录结构…」按钮与盘符选择下拉（`DriveInfo.GetDrives()`）。
- 修改 [DeviceSettingsViewModel.cs](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/ViewModels/DeviceSettingsViewModel.cs)：新增 `AiProfileCommand`，流程：
  1. 选盘 → 扫描（显示进度/取消）；
  2. 调 AI（未配置则提示先去设置）；
  3. 弹出 `Views/AiDeviceProfileWindow.xaml` 预览建议值、扫描摘要、校验警告；
  4. 用户点「应用」→ 填充当前设备编辑表单，再按现有「保存」走 `DeviceProfileStore.Save` 写入用户配置；内置配置不被覆盖文件以外的方式改动。
- 若用户取消或网络失败：不产生任何文件变更。

### 8.5 验收点

- 插入 TrimUI Smart Pro SD 卡，AI 能识别出 `Roms` 根目录与 `Roms/PS`、`Roms/SFC` 等映射，生成配置后可正常发送。
- 插入一个任意 U 盘（无 Roms 目录），AI 返回低置信度或建议自定义根目录，UI 明确提示人工核对。
- 扫描只读，AI 输出非法目录（含 `..`）时被拒绝，不写配置。
- 未配置 AI 时按钮禁用并提示。

---

## 九、通用实现约束

### 9.1 新增/修改文件清单

| 类型 | 文件 |
| --- | --- |
| 新增 | `Services/Ai/AiOptions.cs`、`AiChatClient.cs`、`AiJson.cs`、`AiPrompts.cs`、`AiQueryService.cs`、`AiTagService.cs`、`AiTitleTranslator.cs` |
| 新增 | `Models/ScrapeResult.cs`、`Models/AiQueryPlan.cs`、`Models/DeviceScanSummary.cs` |
| 新增 | `Services/Scraping/FileNameScraper.cs`、`AiMetadataProvider.cs`、`ScraperService.cs` |
| 新增 | `Services/Recommendation/RecommendationService.cs` |
| 新增 | `Services/Devices/DriveTreeScanner.cs`、`AiDeviceProfiler.cs` |
| 新增 | `Views/ScrapePreviewWindow.*`、`AiQueryWindow.*`、`AiTagPreviewWindow.*`、`RecommendWindow.*`、`AiRenamePreviewWindow.*`、`AiDeviceProfileWindow.*` 及对应 VM |
| 修改 | `SettingsWindow.xaml/.cs`（AI 页）、`MainWindow.xaml`（工具栏/右键菜单）、`MainViewModel.cs`（命令与筛选映射）、`DeviceSettingsView.xaml`、`DeviceSettingsViewModel.cs`、`AppServices.cs`（注册 AI 服务） |

### 9.2 配置键

| 键 | 说明 | 默认 |
| --- | --- | --- |
| `ai.base_url` | OpenAI 兼容端点 | 空 |
| `ai.api_key` | API Key | 空 |
| `ai.model` | 模型名 | 空 |
| `ai.timeout_seconds` | 单请求超时 | 60 |
| `ai.batch_limit` | 单次最多处理条数 | 50 |

设备识别的扫描参数（层数/节点数上限）以常量写在 `DriveTreeScanner`，不落设置，避免过度配置。

### 9.3 数据库

- 本规划**不新增数据库列**，沿用现有元数据字段（`title/genre/region/language/release_year/publisher/developer/notes/tags`）。
- 「刮削来源/时间」列暂不做；预览窗口内的「来源」只展示本次结果，不落库。
- 若后续确需审计来源，再单独评估 schema v12。

### 9.4 错误与日志

- AI 请求不写 API Key；如需排查，只记录「BaseUrl 主机 + 模型 + 状态码」，写入 `data\logs\ai.log`（追加，单文件超过 1MB 轮转一次）。
- 用户可见错误统一中文短句，详细异常仅写日志。
- 取消（`CancellationToken`）视为正常结束，不弹错误。

### 9.5 安全

- 自然语言操作只允许白名单动作，绝不执行模型返回的 SQL/命令。
- 文件改名只作用于库内文件，不覆盖、不删除；失败逐条列出。
- 设备配置只写 `data\devices\*.json`，不修改内置 `Resources\DeviceProfiles.json`。
- API Key 不写入 `CHANGELOG.md`、`IMPORTANT.md`、Git 仓库或日志。

## 十、实施顺序

1. **v0.17.0**：AI 基础设施 + 设置 AI 页 + 本地文件名解析 + AI 刮削 + 预览窗口；补单测；发布验收。
2. **v0.18.0**：自然语言查库窗口 + 筛选映射 + 受控操作；补单测。
3. **v0.19.0**：AI 标签预览 + 本地推荐；补单测。
4. **v0.20.0**：标题翻译 + 改名预览 + 复用 `LibraryOrganizer`；补单测。
5. **v0.21.0**：设备目录扫描 + AI 配置推断 + 预览应用；补单测与实机验收。

每个版本完成后固定执行：

1. `dotnet build MicaRomManager.sln -c Release`：0 警告 0 错误；
2. `dotnet test`：全绿；
3. `dotnet publish src\MicaRomManager -c Release -r win-x64 --self-contained true -o publish` 并启动冒烟；
4. 更新 `CHANGELOG.md`、`QUEST.md`、`ROADMAP.md`；经验写 `MEMORIES.md`；
5. 提交并推送 GitHub（本机走 `http://127.0.0.1:7890` 代理）。

## 十一、单元测试计划

| 模块 | 测试内容 |
| --- | --- |
| `AiJson` | 代码围栏、前后缀、尾逗号、非法 JSON 的解析与截断 |
| `FileNameScraper` | 区域/语言/年份/版本/碟号解析，中文标题保留 |
| `ScraperService` | 只补空 / 覆盖两种合并策略；单条失败不影响其他 |
| `AiQueryPlan` 校验 | 非法 Intent/Action/状态/tag 被拒绝；合法条件正确映射 |
| `LibraryOrganizer`（复用） | 翻译后文件名格式、同名避让、库外文件不动 |
| `DriveTreeScanner` | 层数/节点上限、系统目录跳过、无权限跳过 |
| `AiDeviceProfiler` 校验 | `..`/绝对路径/未知平台/不存在目录被拒绝 |
| `RecommendationService` | 打分排序、排除自身、无选中种子回退 |

## 十二、待确认与默认决策

已按老板选择确定：

- 方向：元数据刮削、自然语言查库、标签/推荐、文件名翻译、设备结构学习，全部纳入本规划。
- 模型：云端 OpenAI 兼容接口；BaseUrl 允许填 localhost（不另做本地模型适配代码）。

默认决策（如需调整请在实施前说明）：

- 每阶段独立版本号递增，先做 v0.17.0 与 v0.18.0。
- AI 刮削按「一条一次请求」实现，优先稳定与失败隔离；暂不做多游戏合并请求。
- 自然语言查询不生成 SQL，只输出受控筛选 JSON。
- 推荐先用本地打分，不引入 embedding。
- 设备识别只上传目录名与扩展名统计，不上传文件名与内容。
- 不新增数据库列。

# 平台支持的 ROM 格式

## 摘要

设置 → 平台右侧新增「支持的 ROM 格式」输入框，按平台配置允许的扩展名（如 PS1 填 `iso cue bin chd pbp`）。用途：

1. 扫描该平台子库时只识别这些格式，其它扩展名直接跳过；
2. 导入向导扫描时，若扩展名只匹配一个平台就自动填好平台；
3. 内置平台预填一套常见默认值，自定义平台留空由用户填写。

## 现状分析

- 平台配置表 `platforms(name, emulator_id, folder, updated_at)`，模型 [PlatformSetting.cs](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Models/PlatformSetting.cs) 只有名称、默认模拟器、库内子文件夹。
- 平台页在 [SettingsWindow.xaml](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Views/SettingsWindow.xaml#L167-L183)：默认模拟器、库内子文件夹、说明、保存/扫描/整理按钮。
- 扩展名识别集中在 [ExtensionMap.cs](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Services/ExtensionMap.cs)：`.nes/.sfc/...` 能直接判平台；`.iso/.cue/.bin/.chd` 等返回 `Platform=null, NeedsSpecify=true`；`.pbp` 内置映射为 PSP。
- 扫描入口 [RomImporter.Scan](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Services/RomImporter.cs#L26-L110)：目录扫描时用 `ExtensionMap.IsKnownOrArchive` 过滤未知扩展名，不接受额外白名单。
- 扫描调用点：`SettingsWindow.ScanPlatformLibrary_Click`（平台子库）与 `ImportWizardWindow.ScheduleScan`（导入向导）。
- 内置平台列表见 `PlatformCatalog.Common`；截图里的 `PS1` 是自定义平台，迁移时不会预填，需要手动填一次。

## 改动清单

### 1. 新建 `src/MicaRomManager/Services/PlatformFormats.cs`

```csharp
public static class PlatformFormats
{
    public static IReadOnlyDictionary<string, string[]> Defaults { get; }   // 内置平台 → 默认扩展名
    public static string GetDefault(string platform);                       // 无默认返回 ""
    public static IReadOnlyList<string> Parse(string? formats);            // 返回带点的小写扩展名
    public static string Normalize(string? formats);                        // 统一为空格分隔、小写、无点、去重
    public static bool Supports(string? formats, string extension);
    public static (string Platform, bool NeedsSpecify) Resolve(
        string filePath, ExtensionInfo builtin,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? platformFormats);
}
```

- `Parse` 支持空格、逗号、顿号、分号分隔；忽略前导 `.`；去重（忽略大小写）。
- `Normalize` 输出如 `iso cue bin chd pbp`。
- `Defaults`（按 `PlatformCatalog.Common` 命名）：
  - NES `nes unf`；SNES `sfc smc`；N64 `n64 z64 v64`；Game Boy `gb`；GBC `gbc`；GBA `gba`；NDS `nds`；3DS `3ds cia`；Switch `nsp xci`；
  - GameCube `gcm iso rvz`；Wii `iso wbfs rvz`；Mega Drive `md gen smd bin`；Master System `sms`；Game Gear `gg`；
  - Saturn `iso cue bin chd`；Dreamcast `iso cue bin chd gdi`；PlayStation `iso cue bin chd pbp`；PS2 `iso bin chd`；PS3 `iso pkg`；PSP `iso cso pbp chd`；
  - PC Engine `pce`；Virtual Boy `vb`；街机、其他 留空。
- `Resolve` 规则（导入自动识别平台）：
  1. 压缩包（`.zip/.7z/.rar`）不参与格式匹配，保持 `NeedsSpecify=true`；
  2. 找所有 `formats` 包含该扩展名的平台；
  3. 恰好 1 个 → 用它（即用户配置优先，例如只有 PS1 配了 `pbp` 时，`pbp` 归 PS1 而不是内置的 PSP）；
  4. 多个平台都支持 → 若内置映射的平台在候选里则用内置，否则返回 `未指定 / NeedsSpecify=true`（例如 `iso` 被 PS1、PS2、Saturn 同时支持）；
  5. 一个都没有 → 沿用内置 `ExtensionInfo`。

### 2. `src/MicaRomManager/Data/Database.cs`：schema v7

```csharp
private const string SchemaV7 = """
    ALTER TABLE platforms ADD COLUMN formats TEXT DEFAULT '';
    PRAGMA user_version = 7;
    """;
```

- `Initialize` 中 `version < 7`：执行 SchemaV7，然后遍历 `PlatformFormats.Defaults`，对同名且 `formats` 为空的平台写入默认值（自定义平台不动）。
- 同时把 `PRAGMA user_version` 升到 7。

### 3. 平台数据层

- `PlatformSetting` 增加 `public string Formats { get; set; } = "";`。
- `PlatformRepository`：
  - `ListAll` / `Get`：SELECT 增加 `formats`，空值转 `""`；
  - `Save`：写入 `formats`（`PlatformFormats.Normalize`）；
  - `EnsureExists`：新建平台时若 `PlatformFormats.GetDefault(name)` 非空则写入，否则空。

### 4. `src/MicaRomManager/Services/RomImporter.cs`：扫描过滤与自动识别

`Scan` 增加两个可选参数（默认 null，不影响现有测试）：

```csharp
public static ScanResult Scan(
    IEnumerable<string> inputs, bool recursive,
    IEnumerable<string> existingPaths,
    IReadOnlyCollection<string>? existingHashes = null,
    IReadOnlyCollection<string>? allowedExtensions = null,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>>? platformFormats = null)
```

- `allowedExtensions`：标准化为带点的小写集合；`AddFile` 中若文件扩展名不在集合内：
  - 目录扫描（非显式选择）→ 静默跳过，不计数；
  - 显式选择 → 计入 `excluded`。
- `platformFormats`：候选的平台与 `NeedsSpecify` 改由 `PlatformFormats.Resolve` 计算。
- 原有「路径去重 + 指纹去重」逻辑不变。

调用点更新：

- `ImportWizardWindow`：构造时从 `AppServices.Platforms.ListAll()` 构建 `Dictionary<平台, 扩展名集合>`（只含有格式的平台），传给 `Scan`。
- `SettingsWindow.ScanPlatformLibrary_Click`：`var allowed = PlatformFormats.Parse(setting.Formats);`，非空时传 `allowed`，为空传 null（= 不限制，保持现状）。

### 5. `src/MicaRomManager/Views/SettingsWindow.xaml`：平台页输入框

在「库内子文件夹」输入框之后插入：

```xml
<TextBlock Text="支持的 ROM 格式" Style="{StaticResource FieldLabelStyle}" Margin="0,12,0,4" />
<TextBox x:Name="PlatformFormatsBox" Width="320" HorizontalAlignment="Left"
         ToolTip="空格或逗号分隔，如 iso cue bin chd pbp；留空表示不限制" />
<TextBlock Margin="0,6,0,0" Foreground="{StaticResource TextSecondaryBrush}"
           TextWrapping="Wrap" MaxWidth="380"
           Text="扫描该平台子库时只识别这些扩展名；导入时用于自动识别平台。留空表示不限制。" />
```

「游戏自身绑定模拟器优先…」的说明保持在按钮行上方。

### 6. `src/MicaRomManager/Views/SettingsWindow.xaml.cs`

- `PlatformList_SelectionChanged`：`PlatformFormatsBox.Text = setting.Formats;`
- `SavePlatform_Click`：`setting.Formats = PlatformFormats.Normalize(PlatformFormatsBox.Text);`
- `AddPlatform_Click`：新平台 `Formats` 默认空（自定义平台不预填）。
- 扫描逻辑按上面第 4 节传入 `allowedExtensions`。

### 7. 新增测试

`tests/MicaRomManager.Tests/PlatformFormatsTests.cs`：

- `Parse` 支持空格/逗号/顿号/分号与前导点，去重；
- `Normalize` 输出空格分隔小写；
- `Supports` 大小写不敏感；
- `Defaults` 中 `PlayStation` 含 `pbp`，`PS2` 含 `chd`；
- `Resolve`：
  - 只有 PS1 配置 `pbp` → 平台 = PS1（覆盖内置 PSP）；
  - `iso` 同时被 PS1/PS2 支持 → `未指定` + `NeedsSpecify=true`；
  - `nes` 无平台配置 → 沿用内置 NES；
  - 压缩包 → 需要指定。

`tests/MicaRomManager.Tests/RomImporterFilterTests.cs`（临时目录）：

- 传 `allowedExtensions = { ".chd" }` 时，`.chd` 入候选，`.txt/.iso` 被跳过；
- 传 `platformFormats` 时，唯一匹配的 `.pbp` 自动归到该平台。

## 决策与假设

- **留空 = 不限制**：平台没有格式配置时，扫描与导入均保持现有行为。
- **用户配置优先**：唯一匹配时以平台格式为准，解决 `pbp` 内置映射到 PSP、但用户希望归 PS1 的问题。
- **多平台冲突仍需手选**：如 `iso` 被 PS1/PS2/Saturn 同时支持时，导入向导仍显示「需要指定平台」。
- **压缩包不自动识别**：zip/7z/rar 内容未知，导入时仍需指定平台；若平台格式里写了 zip，扫描该平台子库时仍会接受压缩包文件。
- **cue/bin 不做配对**：与现状一致，分别入库；本次不引入 cue/bin 合并逻辑。
- 迁移只给内置平台预填，自定义平台（截图中的 PS1）需要用户自己填一次。
- 不改 `games` 表结构，不影响便携模式路径逻辑。

## 验证

1. `dotnet build src\MicaRomManager\MicaRomManager.csproj -c Release`：0 警告 0 错误。
2. `dotnet test tests\MicaRomManager.Tests`：72 个 + 新增用例全部通过。
3. `dotnet publish ... -o publish`（单文件），从 `publish\MicaRomManager.exe` 启动。
4. 手工验收：
   - 平台页选中 PS1（自定义）→ 填入 `iso cue bin chd pbp` → 保存 → 重开设置仍在；
   - 向该平台子库放一个 `.txt` 和一个 `.chd` → 扫描只把 `.chd` 收进候选；
   - 平台页选中 PlayStation（内置）→ 格式应已预填 `iso cue bin chd pbp`；
   - 导入向导拖入 `.pbp` 且只有 PS1 支持它 → 平台自动填 PS1；拖入 `.iso` → 仍提示需要指定平台；
   - 数据库 `user_version` 为 7，`platforms.formats` 有值。
5. 更新 `CHANGELOG.md`、`QUEST.md`，提交并推送 GitHub。

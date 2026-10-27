# 平台管理：扫描单平台库目录 + ROM 指纹登记

## 摘要（Summary）

两部分，一起交付：

1. **单平台库扫描**：在「设置 → 平台」页为当前选中平台增加「扫描该平台库目录…」按钮，只扫 `库目录\<该平台子文件夹>\`（不扫整库），把未入库的 ROM 补进库——覆盖「手工拷进库的 ROM」和「从库中移除但文件没删的 ROM」，复用导入预览窗口确认后以链接方式入库。
2. **ROM 指纹（hash）登记**：为每个入库的 ROM 计算内容指纹并写入数据库；扫描与后续所有导入都以「路径 + 指纹」双重去重，避免同一文件换名字重复入库；数据维护页提供「补算缺失指纹」用于存量条目。

## 现状分析（Current State）

- 平台页：[SettingsWindow.xaml](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Views/SettingsWindow.xaml#L125-L177) 已有平台列表、默认模拟器、库内子文件夹与保存按钮；无扫描入口。
- 平台配置：[PlatformRepository.cs](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Data/PlatformRepository.cs) 提供 `ListAll / Get / Save / Delete / EnsureExists`。
- 库目录设置：键 `library.root`（设置页 `LibraryRootBox`）。
- 扫描能力：[RomImporter.Scan](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Services/RomImporter.cs#L24-L91) 只按全路径去重；返回 `Candidates + DuplicateCount + ExcludedCount`。
- 候选模型：[ImportCandidate](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Services/RomImporter.cs#L7-L18)（ObservableObject，字段 FilePath/FileSize/NeedsSpecify/Title/Platform）。
- 入库写入：[MainViewModel.ApplyImportAsync](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/ViewModels/MainViewModel.cs#L237-L305) 逐条 `Insert`。
- 预览窗口：[ImportPreviewWindow.xaml.cs](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Views/ImportPreviewWindow.xaml.cs) 仍在但已停用，可直接复用。
- 数据维护页：[SettingsWindow.xaml](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Views/SettingsWindow.xaml#L179-L193) 已有备份/恢复/CSV/清理失效路径按钮与 `MaintenanceStatus` 文本。
- 设置关闭后：[MainViewModel.OpenSettings](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/ViewModels/MainViewModel.cs#L599-L607) 只 `LoadEmulators + ApplyFilter`，不重载游戏列表。
- 数据库现状：`games` 表 23 列（末列 `disc_paths`），`user_version = 5`。

## 方案（Proposed Changes）

### 1. 新增 `Services/RomHasher.cs`

```csharp
public static class RomHasher
{
    /// <summary>返回内容指纹；读取失败返回空串。</summary>
    public static string Compute(string path);

    /// <summary>小文件全量 SHA256；大文件采样 SHA256（首/中/尾各 1MB + 文件长度），前缀 fast: 区分。</summary>
}
```
- 阈值：`<= 128 MB` 走全量 SHA256；更大的文件走采样（避免几 GB 的 ISO 每次全读）。
- 输出统一为 64 位十六进制（采样版加 `fast:` 前缀），空文件/读取失败返回 `""`。
- 与 `LocalPath` 一样做异常包裹，不让单个坏文件中断扫描。

### 2. `Models/Game.cs` 与数据库 v6

- `Game` 增加 `[ObservableProperty] private string romHash = "";`（并在 `ApplyFrom` 中拷贝，避免上次碟片字段的教训）。
- [Database.cs](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Data/Database.cs) 追加：

```sql
ALTER TABLE games ADD COLUMN rom_hash TEXT DEFAULT '';
PRAGMA user_version = 6;
```

### 3. `Data/GameRepository.cs`

- `Columns` 末尾追加 `rom_hash`；`Insert` / `Update` / `AddParameters` / `Map`（索引 23）同步。
- 新增：
  - `HashSet<string> ListHashes()` —— 库内非空指纹集合（用于扫描去重）；
  - `void SetHash(long id, string hash)` —— 补算存量指纹时使用。

### 4. `ImportCandidate` 增加指纹与重复标记

```csharp
[ObservableProperty] private string romHash = "";
public bool DuplicateByHash { get; init; }
```

### 5. `RomImporter.Scan` 增加指纹去重

签名扩展为：

```csharp
public static ScanResult Scan(
    IEnumerable<string> inputs, bool recursive,
    IEnumerable<string> existingPaths,
    IReadOnlyCollection<string>? existingHashes = null)
```
- 在 `AddFile` 内：先按路径去重（现有逻辑），再 `var hash = RomHasher.Compute(fullPath)`；
  - `hash` 非空且命中 `existingHashes` → 计入 `Duplicates`，跳过（不加入候选）；
  - 否则写入 `candidate.RomHash = hash`。
- 返回值新增 `HashDuplicateCount`（或复用 `DuplicateCount` 并在调用方文案中统一表述为「已存在」）。采用后者：仍用 `DuplicateCount`，文案改为「已存在（路径或内容重复）」。

### 6. 导入向导接入指纹

[ImportWizardWindow.xaml.cs](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Views/ImportWizardWindow.xaml.cs)：
- 构造时保存 `_existingHashes = AppServices.GameRepo.ListHashes();`
- `ScheduleScan` 调 `Scan(..., _existingPaths, _existingHashes)`；扫描期间 `BusyText` 显示「正在计算指纹…」（扫描结束后清空）。
- 摘要文案改为「共 N 个文件，其中 M 个需要指定平台；已存在 / 忽略 K 个」。

### 7. 导入落库写指纹

[MainViewModel.ApplyImportAsync](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/ViewModels/MainViewModel.cs#L237-L305)：

```csharp
RomHash = string.IsNullOrEmpty(candidate.RomHash) ? RomHasher.Compute(romPath) : candidate.RomHash,
```
（复制/移动后路径变了但内容不变，直接用扫描时算好的值。）

### 8. 平台页扫描按钮

`SettingsWindow.xaml` 平台页：把「保存平台设置」与「扫描该平台库目录…」放进同一横向 `StackPanel`，下方加提示文本（`PlatformScanHint`）。

`SettingsWindow.xaml.cs` 新增 `ScanPlatformLibrary_Click`：

1. 未选中平台 → 提示；库目录为空 → 提示去常规设置配置。
2. `dir = Path.Combine(libraryRoot, RomImporter.SanitizeFolderName(folder 为空 ? 平台名 : folder))`；目录不存在 → 提示实际路径。
3. 后台扫描：`Scan(new[]{dir}, recursive:true, 现有 RomPath 列表, AppServices.GameRepo.ListHashes())`；期间按钮禁用、`MaintenanceStatus` 显示「正在扫描并计算指纹…」。
4. 0 候选 → 提示「没有需要补充的 ROM（已存在 X 个，忽略 Y 个）」。
5. 每个候选 `Platform = 当前平台`，弹 `ImportPreviewWindow`；确认后逐条 `Insert`（`RomPath = 磁盘路径`、`RomHash = candidate.RomHash`、`FileSize`、`Status = NotPlayed`），链接语义不动文件。
6. 汇总 MessageBox；成功后 `((MainWindow)Owner)?.RefreshGames()`。

### 9. 数据维护：补算缺失指纹

`SettingsWindow.xaml` 数据维护页的按钮区加入「补算缺失指纹」。

`SettingsWindow.xaml.cs`：
- 遍历 `AppServices.GameRepo.ListAll()` 中 `RomHash` 为空且 `File.Exists(RomPath)` 的记录，后台逐个 `RomHasher.Compute` → `SetHash(id, hash)`；
- 完成后 `MaintenanceStatus` / MessageBox 汇总「已补算 N 条，跳过 M 条（文件缺失或为空）」；
- 过程中禁用按钮并显示进度文案。

### 10. 主界面刷新

[MainViewModel.OpenSettings](file:///d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/ViewModels/MainViewModel.cs#L599-L607)：关闭设置后改为 `LoadEmulators(); LoadGames();`，让扫描补入的条目立刻出现（`LoadGames` 内部会 `RefreshLookups + ApplyFilter`）。

### 11. 无需改动的部分

- 不复用导入向导完成平台扫描（来源固定、方式固定为链接，预览窗口更贴合）。
- 不加 CSV 指纹列、不加按标题去重。

## 决策与假设（Assumptions & Decisions）

- **指纹算法**：≤128MB 全量 SHA256；更大文件用「大小 + 首/中/尾各 1MB」采样 SHA256（前缀 `fast:`）。理由：PS2/GC 镜像动辄数 GB，每次全量哈希会让扫描到分钟级；采样指纹足以识别「同一份 ROM 拷贝」。
- **去重口径**：路径命中 或 指纹命中，均记为「已存在」并跳过；预览窗口不再重复展示。
- **扫描固定链接入库**：文件已在库目录，不再复制/移动。
- **平台固定为当前选中平台**，预览里可手工修改后按修改值入库。
- **存量条目不自动补指纹**（避免启动变慢），改为数据维护页手动触发。
- **失败不中断**：单条异常计入失败/跳过，最后汇总。
- **扫描使用平台行当前内存值**，不必先点保存。

## 验证（Verification）

1. `dotnet build MicaRomManager.sln -c Release`：0 警告 0 错误；`dotnet test`：48 用例全通过。
2. 数据库：启动后 `user_version = 6`，`games` 表存在 `rom_hash` 列。
3. 指纹：
   - 向 `库目录\PS2\` 拷入同一 ISO 的两份不同文件名副本 → 扫描只入库 1 条（另一条因指纹重复被跳过）；
   - 再扫一次 → 全部提示已存在；
   - 导入向导选这两个文件 → 同样只导入 1 条；
   - 数据维护「补算缺失指纹」→ 存量条目 `rom_hash` 被填上，重复执行提示无缺失。
4. 平台扫描：
   - 从库中移除某 PS2 条目（不删文件）→ 扫描能重新补回；
   - 扫描 NES 时不会带入 PS2 目录文件；
   - 未配置库目录 / 目录不存在 → 给出明确提示不报错。
5. 回归：正常导入（链接/复制/移动）、启动游戏、平台默认模拟器仍工作。

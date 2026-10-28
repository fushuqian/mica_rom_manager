# lite 分支与主版本（main / WPF）差异分析

> 对比对象：`origin/main`（WPF v0.13.1，HEAD `9e49a5f`）与当前 `lite` 分支（HEAD `1af90b1`，v0.14.0-lite）。
> 方式：只读代码/文档对比，未运行程序、未改任何文件。
> 结论范围：工程形态、功能对齐度、已确认差异、数据兼容性与后续建议。

## 1. 结论摘要

1. `lite` 不是"删功能换轻量"的精简版，而是**同功能的技术栈重构**：main 用 WPF + .NET 9，lite 用 Rust + Tauri 2 + Svelte 5，功能目标为 1:1 对齐，并共用现有 `data\mica.db`（schema v10，不改表结构）。
2. `lite` 分支**原样保留了 WPF 源码**：`src/`、`tests/`、`MicaRomManager.sln` 与 `origin/main` 完全一致（diff 为 0），仅作行为参考，不参与 lite 构建；新增的 `app/` 才是 lite 产物。
3. 功能总体对齐，但已确认 **4 项功能缺失/退化 + 1 项工程质量差距**：
   - 模拟器安装后**不能选择 exe**（自动取第一个候选）；
   - **rar 解压依赖外部 7-Zip/WinRAR**，WPF 是内置 SharpCompress；
   - 设置里**缺少「测试 GitHub 连接」**；
   - **长任务无进度条/无取消**（导入、指纹补算、下载只显示忙碌动画）；
   - 单元测试 **9 个（Rust） vs 78 个测试方法（WPF）**。
4. 交付形态差异明显：lite 便携 exe 实测 **6.54 MB**（`publish-lite\MicaRomManager.exe`），但依赖系统 **WebView2**；WPF 为自包含单文件，无额外运行时依赖，体积大得多。

## 2. 工程与仓库层面

| 维度 | main（主版本） | lite 分支 | 说明 |
| --- | --- | --- | --- |
| 技术栈 | WPF / .NET 9 / CommunityToolkit.Mvvm / Microsoft.Data.Sqlite / SharpCompress | Tauri 2 / Rust（rusqlite bundled）/ Svelte 5 + TS + Vite | 后端全部重写，前端自制表格与弹窗 |
| 目录 | `src/MicaRomManager`、`tests` | 额外新增 `app/`（`web` + `src-tauri`），`src/`、`tests/` 原样保留 | lite 分支同时存在两套代码 |
| 代码量 | C#+XAML 约 9,742 行；测试约 1,172 行 | Rust 约 5,184 行；Svelte/TS 约 4,610 行 | 总量接近，但 lite 把逻辑压到更少代码 |
| UI 实现 | XAML + DataGrid + CollectionView 筛选 | 自绘虚拟滚动表格；筛选/排序在前端内存完成 | 列宽、列顺序、行高 28px 照抄 WPF |
| 构建 | `dotnet publish ... -o publish` | `npm run tauri build`，交付 `publish-lite\` | 两套工具链 |
| 产物 | 单文件自包含 exe + `Resources\` + `data\` | exe 6.54 MB + `Resources\` + `data\` | lite 需 WebView2 |
| 文档 | 无 ROADMAP | 新增 `ROADMAP.md`，更新 `RULES.md`/`IMPORTANT.md` | 见 `git diff origin/main HEAD` |
| 版本号 | v0.13.1 | v0.14.0-lite | 版本序列不连续，lite 从 0.14 起 |

构建产物明细：`publish-lite` 下仅 `MicaRomManager.exe`（6,857,728 字节）、`Resources\`、`data\`。

## 3. 功能对齐情况

以 lite 的重构计划功能清单（`.trae/documents/lite分支Rust-Tauri重构计划.md` 第 6 节）为基线，逐项核对：

| 功能块 | 对齐 | 核对依据 / 差异 |
| --- | --- | --- |
| 工具栏 8 项 | 对齐 | `app/web/components/Toolbar.svelte` 与 WPF 工具栏一一对应 |
| 平台侧栏 + 数量 | 对齐 | `PlatformSidebar.svelte` |
| 细筛（搜索/状态/区域/语言/开发商/标签/仅收藏/仅缺失） | 对齐 | `FilterBar.svelte`、`lib/state.svelte.ts` |
| 17 列 + 列宽/排序/筛选持久化 | 基本对齐 | `lib/grid.ts` 17 列，宽/WPF 一致；**格式列排序键为 `romPath`**（WPF 为 `Format`） |
| 编辑模式 8 类单元格、评分星标 | 对齐 | `GameGrid.svelte` 编辑与点星 |
| 右键菜单 15 项 + 指定模拟器子菜单 | 对齐 | `App.svelte` 595–662 行 |
| 多碟合并/拆分、碟片选择 | 对齐 | `gamesMergeDiscs`/`gamesSplitDiscs`、`DiscSelectDialog.svelte` |
| 导入向导（3 种入库、递归、拖放、去重） | 对齐 | `ImportWizard.svelte` + `services/importer.rs` |
| 平台管理（默认模拟器/子文件夹/格式白名单/核心/参数/扫描/整理/清理） | 对齐 | `SettingsDialog.svelte` 平台页 + `platforms.rs` |
| 字典库 7 类词条与级联 | 对齐 | `lookups_kinds` 返回 7 类 |
| 模拟器管理（模板/压缩包安装/调试/更新/删除） | 基本对齐 | 安装后 exe 选择退化，见 4.1 |
| 分享打包（含模拟器/存档/zip） | 对齐 | `SharePackDialog.svelte`、`services/sharer.rs` |
| 便携模式与路径重定位 | 对齐 | `portable.rs`、`paths.rs`；`%DATA%` 语义一致 |
| 数据维护（备份/恢复/CSV/补指纹/清理） | 对齐 | `SettingsDialog.svelte` 数据维护页 |
| 代理三态 + GitHub 更新/下载 | 部分对齐 | 代理三态有；**缺连接测试按钮**，见 4.3 |
| 游玩统计、自动改状态 | 对齐 | `games_add_play_stats` |
| 设置 6 页签 | 对齐 | 常规/模拟器管理/字典库/平台/数据维护/关于 |
| 数据库 schema v10 | 对齐 | 两边迁移到 v10；列（rating/args_template/core_path/website_url/rom_hash/disc_paths）一致 |

## 4. 已确认的差异（按影响排序）

### 4.1 模拟器安装后不能挑选 exe —— 功能退化

- WPF：安装完成后用 `ExePickWindow` 列出候选，让用户选择可执行文件（`src/MicaRomManager/ViewModels/EmulatorManagerViewModel.cs:128`）。
- lite：`SettingsDialog.svelte:202` 直接 `const exe = outcome.exeCandidates[0] ?? ""`，自动用第一个候选。
- 影响：多 exe 的模拟器包（含 GUI/控制台两个入口）可能绑定错；用户只能事后去编辑配置改。
- 建议：安装完成后弹候选选择（复用 `EmulatorEditDialog` 或新增小弹窗），默认选第一个。

### 4.2 rar 解压依赖外部程序

- WPF：SharpCompress 内置解压 `.7z/.rar/.tar.gz`（`EmulatorInstaller.cs:45-73`）。
- lite：zip/7z/tar.gz 纯 Rust 实现；**rar 必须找本机 7-Zip 或 WinRAR**，找不到直接报错（`app/src-tauri/src/services/installer.rs:224-260`，`IMPORTANT.md` 第 21 行）。
- 影响：干净机器（无 7-Zip/WinRAR）无法安装 rar 包；与 WPF 行为不一致。
- 建议：保留外部调用作为兜底，错误文案明确提示「请先安装 7-Zip，或手动解压后用文件夹安装」。

### 4.3 设置缺少「测试 GitHub 连接」

- WPF：常规页有「测试 GitHub 连接」按钮（`SettingsWindow.xaml:76`）。
- lite：常规页只有代理模式/地址/保存，没有测试入口（`SettingsDialog.svelte:632-643`）。
- 影响：老板排查代理是否生效只能靠「检查更新」间接验证。
- 建议：新增后端命令（如 `network_test_github`）并在常规页加按钮，复用现有代理构建逻辑。

### 4.4 长任务无进度、无取消

- WPF：下载、补算指纹等有 ProgressBar / 状态文本进度（`MainWindow.xaml:320`、`EmulatorManagerView.xaml:115`、`SettingsWindow.xaml.cs:480`）。
- lite：所有命令一次性返回，界面仅显示忙碌动画（`App.svelte:588`），没有 `scan://progress` 等事件（全仓库只有 `commands/files.rs:28` 一处 `emit`）。
- 影响：上千条补指纹、大包下载时用户不知道进度，也无法中断。
- 建议：按重构计划 3.3 补事件（`import://progress`、`hash://progress`、`download://progress`），前端 `listen` 更新状态栏。

### 4.5 测试基线明显变薄

- lite：`app/src-tauri/src/tests.rs` 仅 9 个 `#[test]`，覆盖迁移/路径/参数/格式/哈希/整理等少量规则。
- main：`tests/` 下 78 个 `[Fact]/[Theory]` 标注（ROADMAP 记录约 103 个用例），覆盖导入过滤/去重、分享包、模拟器核心、代理地址、字典/游戏仓储等。
- 影响：lite 的导入、分享、安装、数据库仓储等高风险模块无自动化回归，后续两个分支并行开发容易漂移。
- 建议：优先补 `importer`、`sharer`、`installer`、`db/games` 四块单测，再谈发版。

### 4.6 其他小差异

| 项 | main | lite | 影响 |
| --- | --- | --- | --- |
| 格式列排序 | `SortMemberPath=Format` | `sortKey: "romPath"`（`lib/grid.ts:33`） | 排序结果按完整路径而非扩展名，语义偏差 |
| 「同步标签字典」 | 无独立入口 | 后端已实现 `maintenance_sync_tags`，前端未接（`api.ts:276`） | 死命令，建议接上或删除 |
| 运行时依赖 | 自包含 .NET，无系统依赖 | 依赖 WebView2（旧 Win10 需安装） | 便携场景多一个前置条件 |
| 体积/启动 | 单文件较大 | 6.54 MB，冷启动/内存目标 ≤1s / ≤120MB（待实测） | lite 优势 |
| 进程共存 | 可与 lite 交替打开同一库 | 与 WPF 不能同时运行（SQLite 单写 + `busy_timeout`） | 交付说明需注明 |

## 5. 数据与兼容性

- **schema**：两版都为 v10，lite 的 `db/migrations.rs` 复刻 SchemaV1–V10，`user_version` 保持 10；WPF 打开 lite 写过的库没有结构风险。
- **设置键**：`library.root`、`library.portable`、`library.import_mode`、`network.proxy.mode/address`、`list_view_state` 保持同名同结构，视图状态可互读。
- **路径语义**：`%DATA%`、便携相对路径、模拟器 `exe_path/core_path` 只展开 `%DATA%` 的规则一致（`MEMORIES.md` 已知坑已继承）。
- **可回退性**：WPF 源码仍在 `origin/main`，lite 分支已将其移入 `toberemoved/wpf/`；需要对照或回退时切回 main 即可。
- **未验证项**：未实机跑 lite 的导入→启动→分享闭环，未做 1000 条性能与 200% DPI 复核（重构计划 M8 验收项）。

## 6. 后续建议（需老板确认后再排期，本次不执行）

优先级从高到低：

1. **补齐 4 项功能缺口**（建议一个版本内完成）：
   - `app/web/components/SettingsDialog.svelte`：安装后 exe 候选选择；常规页加「测试 GitHub 连接」。
   - `app/src-tauri/src/services/installer.rs`：rar 失败文案与文件夹安装引导。
   - `app/src-tauri/src/commands/import.rs` + `services/maintenance.rs` + `installer.rs`：补进度事件；前端 `App.svelte`/`SettingsDialog.svelte` 监听显示。
2. **补测试**：`importer`/`sharer`/`installer`/`db::games` 四块，目标覆盖 WPF 现有核心规则。
3. **修小偏差**：`lib/grid.ts` 格式列改按扩展名排序；决定 `maintenance_sync_tags` 接前端还是删除。
4. **收尾分支**：功能对齐验收通过后，按规则 11 把 `src/`、`tests/`、`MicaRomManager.sln` 移入 `toberemoved/wpf/`，明确 lite 为主版本；在此之前保持双份代码。

## 7. 验证与复现方式

```powershell
# 1. WPF 源码已移入 toberemoved/wpf（lite 分支）
git log --oneline --stat -1             # 应看到 src/tests/sln 的重命名

# 2. 查看 lite 版本差异
git diff --stat origin/main HEAD

# 3. 测试数量
(Select-String -Path app/src-tauri/src/tests.rs -Pattern '#\[test\]').Count           # 12

# 4. 产物体积
Get-ChildItem publish-lite\MicaRomManager.exe | Select-Object Length                   # 6,857,728

# 5. lite 质量门（在 app/src-tauri 下）
cargo check --all-targets
cargo test --lib --target x86_64-pc-windows-msvc   # GNU 工具链下测试 exe 起不来，见 MEMORIES
```

## 8. 假设与边界

- 「主版本」按 `origin/main`（WPF v0.13.1）理解；若老板指的是其他分支/发布包，需要再校准。
- 本分析只做静态对比，不代表 lite 已通过实机验收；性能、DPI、WebView2 兼容性均未实测。
- 差异清单只记录**已从代码中确认**的项，未逐行审计全部业务逻辑（如启动参数边界、代理细节）。

## 9. 追平结果（2026-10-28）

| 原差异 | 状态 | 证据 |
| --- | --- | --- |
| 4.1 安装后不能选 exe | 已修 | 新增 `ExePickDialog.svelte`，`SettingsDialog.finishInstall` 按 0/1/多候选分流 |
| 4.2 rar 依赖外部程序 | 已修 | `installer::extract_rar_builtin`（unrar-rs）优先，`extract_rar_external` 兜底 |
| 4.3 缺「测试 GitHub 连接」 | 已修 | 命令 `network_test_github` + 常规页按钮 |
| 4.4 长任务无进度、无取消 | 已修 | `progress.rs` 广播 `task://progress`/`task://done`，`cancel_task`；导入、补指纹、下载均支持中止 |
| 4.5 测试基线变薄 | 已补 | 9 → 12 项，新增 games CRUD/指纹、sharer 预览与打包、installer 文件夹安装 |
| 格式列排序 | 已修 | `grid.ts` 改为 `sortValue` 按扩展名，`canSort()` 统一判断 |
| `maintenance_sync_tags` 死命令 | 已接 | 数据维护页「同步标签字典」 |
| 双份代码 | 已清 | `src/`、`tests/`、`MicaRomManager.sln` → `toberemoved/wpf/` |
| WebView2 依赖 | 无法消除 | Tauri 架构决定，仅能交付说明注明 |
| 与 WPF 进程不能共存 | 无法消除 | SQLite 单写，属设计约束 |

仍未完成：实机验收（导入→启动→分享闭环、1000 条性能、200% DPI）。

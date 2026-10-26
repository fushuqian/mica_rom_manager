# MICA ROM MANAGER 实施计划

## 1. 概述

做一个 Windows 桌面端 ROM 集中管理工具（产品名：Mica ROM Manager，界面中文）：

* 主界面上部为工具栏 + 筛选区，下部为 Excel 风格游戏列表（DataGrid）。

* 双击行打开「游戏信息」窗口编辑；右键提供完整操作菜单。

* 右键「分享打包」可把 ROM + 模拟器 + 配置 + 启动器打包成独立文件夹，拷到其他电脑双击即玩。

* 设置中提供「模拟器管理」：安装（zip/文件夹导入到本地模拟器目录）、更新（手动替换 / GitHub Release 检查）、配置（exe 路径、启动参数、核心、扩展名、默认绑定）。

已确认决策：WPF + .NET 9；模拟器通用可配置（内置常见模板）；分享包为「导出文件夹 + play.bat 启动器」，并可选压缩为 zip。

## 2. 现状分析

* `d:\Trae\MICA_ROM_MANAGER` 为空目录，无任何代码、无 git 仓库。

* 本机工具链：.NET SDK 8.0.424 / 9.0.317、Node 24、Python 3.12、Git 2.55。

* 规则要求的存在性文件 `RULES.md`、`QUEST.md`、`CHANGELOG.md`、`MEMORIES.md`、`IMPORTANT.md` 均缺失，需在实施第一步创建。

* 无历史 memory，无既有约定可复用。

## 3. 技术选型

| 项    | 选择                                  | 理由                                 |
| ---- | ----------------------------------- | ---------------------------------- |
| 运行时  | .NET 9 (`net9.0-windows`)           | 本机已装 SDK                           |
| 框架   | WPF                                 | DataGrid 原生支持 Excel 式列表、排序、列宽、右键菜单 |
| MVVM | CommunityToolkit.Mvvm 8.x           | 轻量、SourceGenerator                 |
| 存储   | SQLite（`Microsoft.Data.Sqlite`）     | 单文件、免服务、便携                         |
| 压缩   | `System.IO.Compression`             | 内置，无额外依赖                           |
| 回收站  | Shell32 `SHFileOperation`（P/Invoke） | 删除 ROM 走回收站，符合规则 11                |
| 测试   | xUnit                               | 覆盖参数构建、扩展名映射、分享包生成等纯逻辑             |

**字体**：统一使用本机已安装的 HarmonyOS Sans SC（`C:\Windows\Fonts\HarmonyOS_Sans_SC_Regular.ttf` / `_Bold.ttf`，族名已核实为 `HarmonyOS Sans SC`）。**不嵌入字体、不复制字体文件**，直接按系统字体名引用，带回退：

```xml
<FontFamily x:Key="AppFontFamily">HarmonyOS Sans SC, Microsoft YaHei UI</FontFamily>
```

在 `Window` 级设置 `FontFamily="{StaticResource AppFontFamily}"`，正文 13px，表格行高 30，标题 14–16px；数字/等宽列用 `Consolas` 保持对齐。特殊字符（如 ★、▸）若字体缺字形，WPF 会自动回退，不影响显示。

不引入 EF Core（表少，手写 SQL 更轻）；不引入网络刮削（封面手动指定）；首版不做暗色主题。

## 4. 目录结构

```
d:\Trae\MICA_ROM_MANAGER\
├─ RULES.md / QUEST.md / CHANGELOG.md / MEMORIES.md / IMPORTANT.md
├─ .gitignore
├─ MicaRomManager.sln
├─ src\MicaRomManager\
│  ├─ MicaRomManager.csproj            # WPF, net9.0-windows, WinExe
│  ├─ App.xaml / App.xaml.cs           # 启动、全局异常、DB 初始化
│  ├─ MainWindow.xaml / .cs
│  ├─ Models\                          # Game, Emulator, GameStatus, EmulatorTemplate
│  ├─ Data\                            # Database, Migrations, GameRepository, EmulatorRepository, SettingsRepository
│  ├─ Services\                        # Importer, Launcher, SharePackager, EmulatorInstaller, EmulatorUpdater, FileRecycleBin, PathService, CoverStore, ExtensionMap
│  ├─ ViewModels\                      # Main, GameEdit, EmulatorManager, SharePack, Settings
│  ├─ Views\                           # GameEditWindow, EmulatorManagerWindow, SharePackWindow, SettingsWindow
│  ├─ Resources\EmulatorTemplates.json # 内置模拟器模板
│  ├─ Themes\Theme.xaml                # 颜色、字体、按钮、DataGrid 样式
│  └─ Helpers\                         # RelayCommand 辅助、格式转换器
└─ tests\MicaRomManager.Tests\
   ├─ MicaRomManager.Tests.csproj
   ├─ LauncherArgsTests.cs
   ├─ ExtensionMapTests.cs
   └─ SharePackagerTests.cs
```

运行期数据目录（`PathService` 决定）：

* 优先便携模式：`AppContext.BaseDirectory\data\`（可写时）。

* 不可写则回退 `%LOCALAPPDATA%\MicaRomManager\`。

* 内含 `mica.db`、`covers\`（封面副本）、`emulators\<名称>\`（安装的模拟器）、`exports\`（默认导出位置）。

## 5. 数据模型

### 5.1 SQLite 表（`Database.EnsureCreated` 用脚本 + `PRAGMA user_version` 做迁移）

```sql
CREATE TABLE games (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  title TEXT NOT NULL,
  platform TEXT NOT NULL DEFAULT '',
  genre TEXT DEFAULT '',
  region TEXT DEFAULT '',
  release_year INTEGER,
  publisher TEXT DEFAULT '',
  developer TEXT DEFAULT '',
  favorite INTEGER NOT NULL DEFAULT 0,
  status TEXT NOT NULL DEFAULT '未玩',      -- 未玩/在玩/已通关/已放弃
  rom_path TEXT NOT NULL UNIQUE,
  file_size INTEGER NOT NULL DEFAULT 0,
  cover_path TEXT DEFAULT '',
  notes TEXT DEFAULT '',
  tags TEXT DEFAULT '',                     -- 逗号分隔
  emulator_id INTEGER,
  play_count INTEGER NOT NULL DEFAULT 0,
  play_seconds INTEGER NOT NULL DEFAULT 0,
  last_played_at TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE emulators (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL UNIQUE,
  exe_path TEXT NOT NULL DEFAULT '',        -- 可为相对 data\emulators 或绝对路径
  args_template TEXT NOT NULL DEFAULT '"{rom}"',
  core_path TEXT DEFAULT '',                -- RetroArch -L 用，相对模拟器目录
  supported_ext TEXT DEFAULT '',            -- ".sfc,.smc"
  version TEXT DEFAULT '',
  install_dir TEXT DEFAULT '',
  config_path TEXT DEFAULT '',
  github_repo TEXT DEFAULT '',               -- 可选，用于检查更新 "owner/repo"
  notes TEXT DEFAULT '',
  enabled INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE emulator_platforms (
  emulator_id INTEGER NOT NULL,
  platform TEXT NOT NULL,
  is_default INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (emulator_id, platform)
);

CREATE TABLE settings (
  key TEXT PRIMARY KEY,
  value TEXT
);
```

`settings` 键：`library.copy_on_import`(bool)、`library.root`、`share.last_export_dir`、`ui.column_widths`、`ui.filter_state`。

### 5.2 模拟器模板（`Resources/EmulatorTemplates.json`，复制到 `data\EmulatorTemplates.json` 供用户改）

字段：`name`、`platforms`、`extensions`、`argsTemplate`、`corePath`、`githubRepo`、`notes`。
内置条目：

| 名称          | 平台           | 参数模板                   |
| ----------- | ------------ | ---------------------- |
| RetroArch   | 多平台          | `-L "{core}" "{rom}"`  |
| Dolphin     | GameCube/Wii | `-b -e "{rom}"`        |
| PCSX2       | PS2          | `"{rom}" --fullscreen` |
| PPSSPP      | PSP          | `"{rom}"`              |
| DuckStation | PS1          | `"{rom}"`              |
| RPCS3       | PS3          | `"{rom}"`              |
| mGBA        | GB/GBC/GBA   | `"{rom}"`              |
| Mesen       | NES/SNES     | `"{rom}"`              |
| Project64   | N64          | `"{rom}"`              |

### 5.3 扩展名 → 平台映射（`ExtensionMap`）

内置：`.nes→NES`、`.smc/.sfc→SNES`、`.gb/.gbc→Game Boy`、`.gba→GBA`、`.nds→NDS`、`.n64/.z64/.v64→N64`、`.md/.gen/.bin→Mega Drive`、`.gg→Game Gear`、`.sms→Master System`、`.pce→PC Engine`、`.vb→Virtual Boy`、`.wbfs/.rvz→Wii/GameCube`、`.iso/.chd/.cue→光盘镜像(需指定)`、`.3ds/.cia→3DS`、`.pbp/.cso→PSP`、`.xci/.nsp→Switch`。
`.bin` 同时属于 Mega Drive 与光盘镜像 → 映射为「需指定平台」，批量导入时弹窗一次性指定（记住选择）。

## 6. 功能实现细案

### 6.1 主界面布局（`MainWindow.xaml`）

* 顶栏：标题 + 工具栏按钮（导入文件 / 导入文件夹 / 新建条目 / 启动游戏 / 分享打包 / 打开所在目录 / 刷新 / 设置）。

* 筛选条：搜索框（标题/文件名/路径/标签，即时过滤）、平台下拉（全部 + 库内实际平台）、状态下拉、标签下拉、复选框「仅收藏」「仅缺失文件」「重复项」、右侧显示统计「共 N 款 / 已筛选 M 款」。

* 表格：`DataGrid`（ReadOnly，`SelectionMode=Extended`、`CanUserSortColumns`、`CanUserResizeColumns`、`CanUserReorderColumns`、`GridLinesVisibility=Horizontal`、交替行色）。

* 底部状态栏：数据文件路径、上次刷新时间。

* 交互：双击行 → 编辑窗口；Enter → 启动；F2 → 编辑；Delete → 从库移除（不删文件，二次确认）；Ctrl+F → 聚焦搜索；拖拽文件/文件夹到表格 → 导入。

* `CollectionViewSource` + `ICollectionView.Filter` 做内存过滤与排序（ROM 库量级数千，够用）。

列定义（顺序即默认列顺序，隐藏列可后续在设置中配置，可省）：

| 列    | 字段                       | 宽度  |
| ---- | ------------------------ | --- |
| ★    | favorite                 | 32  |
| 封面   | cover\_path 缩略图          | 64  |
| 标题   | title                    | 220 |
| 平台   | platform                 | 110 |
| 类型   | genre                    | 100 |
| 年份   | release\_year            | 70  |
| 状态   | status                   | 80  |
| 次数   | play\_count              | 60  |
| 游玩时长 | play\_seconds（格式化为 h:mm） | 90  |
| 最近游玩 | last\_played\_at         | 130 |
| 大小   | file\_size               | 90  |
| 标签   | tags                     | 120 |
| 文件路径 | rom\_path                | 320 |

缺失文件（`File.Exists=false`）整行文字变红并加提示；封面不存在时显示占位。

### 6.2 导入（`RomImporter`）

* 入口：工具栏「导入文件/文件夹」、拖拽。

* 文件对话框按已知扩展名过滤；文件夹递归扫描。

* 流程（异步 + 进度条）：扫描 → 扩展名判平台 → 去重（已存在 `rom_path` 跳过，同大小同文件名冲突提示）→ 生成预览列表（可改标题/平台）→ 入库。

* 标题默认取文件名（去扩展名），可批量编辑。

* 库策略：默认「仅索引」；若 `library.copy_on_import=true`，复制到 `library.root\<平台>\<文件名>` 后入库。

* 未知扩展名/需指定平台的项，在预览窗口中统一指定平台。

* 完成后提示「新增 X 条，跳过 Y 条」。

### 6.3 游戏信息窗口（`GameEditWindow`）

* 模式：编辑已有 / 新建空白。

* 字段：标题、平台（可编辑下拉）、类型、地区、年份、发行商、开发商、状态、收藏、标签、备注（多行）、ROM 路径（只读 + 浏览）、文件大小（只读）、封面（选择图片 → 复制到 `data\covers\<guid>.<ext>`，可清除）、模拟器（下拉，默认按平台默认模拟器）。

* 校验：标题非空、ROM 路径非空。

* 保存后刷新列表并保持选中行。

### 6.4 右键菜单（DataGrid ContextMenu）

启动游戏 / 用指定模拟器启动 ▸ / 编辑信息 / 分享打包… / 打开所在文件夹 / 复制文件路径 / 设置封面… / 收藏切换 / 刷新文件状态 / 从库中移除（不删文件）/ 移到回收站并移除（二次确认）。

多选时：启动、分享、收藏、移除 均支持批量；「打开所在文件夹」取第一项。

### 6.5 启动与统计（`RomLauncher` + `ProcessTracker`）

* 启动前校验 ROM 与模拟器 exe 存在，缺失弹窗提示去模拟器管理配置。

* 参数构建：`ArgsBuilder.Build(template, context)`，占位符 `{rom}`、`{romdir}`、`{romname}`（含扩展名）、`{core}`、`{config}`，全部替换为**绝对路径并加引号**；模板中已带引号时避免重复加引号。

* 选择模拟器优先级：游戏绑定 → 平台默认 → 平台任意启用 → 提示。

* `Process.Start` + `EnableRaisingEvents`，异步等待退出后累加 `play_seconds`、`play_count`、`last_played_at`，回写 DB 并刷新行。

* 核心 `{core}` 解析：优先 `core_path`（相对 `install_dir`），RetroArch 缺核时提示。

### 6.6 分享打包（`SharePackager` + `SharePackWindow`）

导入结构（选中的每个游戏生成一个 `Play-*.bat`，模拟器只打包一次）：

```
<ExportDir>\<包名>\
├─ emulators\<模拟器名>\...        # 从 install_dir 完整复制（可勾选“不打包模拟器”）
├─ roms\<游戏标题>\<rom 文件>
├─ config\<模拟器名>\...           # 模拟器 config_path（存在时复制）
├─ saves\                          # 可选：复制模拟器存档目录（用户勾选并指定）
├─ Play-<游戏标题>.bat
├─ manifest.json                   # 版本、游戏、平台、模拟器、参数模板
└─ README.txt                      # 双击 Play-*.bat 即可游玩；说明目录结构
```

* `play.bat` 生成规则：UTF-8 无 BOM，首行 `chcp 65001 >nul`，`cd /d "%~dp0"`，`start "" "emulators\...\xxx.exe" <args>`；路径使用相对包目录 + 引号。示例（RetroArch）：

```bat
@echo off
chcp 65001 >nul
cd /d "%~dp0"
start "" "emulators\RetroArch\retroarch.exe" -L "emulators\RetroArch\cores\snes9x_libretro.dll" "roms\Super Metroid\Super Metroid.sfc"
```

* 文件名清洗（去除 `\ / : * ? " < > |`），重名追加序号。

* 导出前显示体积估算（ROM + 模拟器目录大小）与目标路径；支持「导出」与「导出并压缩 zip」；完成后可「打开文件夹」。

* 模拟器可执行文件找不到 / 版本为空时给出警告但允许继续（目标机自备）。

* 参数模板中 `{rom}` 等占位符在 bat 中替换为相对路径。

### 6.7 模拟器管理（`EmulatorManagerWindow`，从设置进入）

* 列表：名称、平台、版本、exe 路径、扩展名、启用状态、默认平台。

* 按钮：

  * **新增（从模板）**：选择 `EmulatorTemplates.json` 中的模板 → 填充名称/平台/扩展名/参数。

  * **安装**：选择 zip 或文件夹 → 解压/复制到 `data\emulators\<名称>\` → 自动在目录内搜索 `*.exe`（优先与名称匹配的 exe）→ 写入 `install_dir` / `exe_path`。

  * **编辑配置**：exe 路径（可浏览）、参数模板（含占位符说明）、核心路径、扩展名、版本、GitHub 仓库、config 目录、备注、平台与默认绑定（可多选）。

  * **设默认**：把该模拟器设为所选平台默认（`emulator_platforms.is_default`）。

  * **更新**：两种方式 ——

    1. 手动更新：选择新版本 zip/文件夹，覆盖 `install_dir`（旧目录先改名备份到 `data\emulators\.backup\<名称>-<时间戳>`，不直接删）。
    2. 检查更新：配置了 `github_repo` 时调用 GitHub 公共 Release API（免费、无需 token，注意 60 次/小时限流；仅在用户点击时调用），比较 `tag_name` 与本地 `version`，列出下载资产供用户下载后走手动更新流程。

  * **删除**：仅从库移除配置；若 `install_dir` 在 `data\emulators` 内，二次确认后移到回收站。

  * **测试启动**：用一个示例 ROM 或空参数试运行，验证 exe 可用。

### 6.8 设置窗口（`SettingsWindow`，TabControl）

* 常规：数据目录（只读提示 + 打开）、导入策略（仅索引 / 复制到库目录 + 库根目录选择）、列表显示项（封面列开关）。

* 模拟器管理：内嵌 `EmulatorManagerWindow` 的同一套 View（用户控件化，便于设置内展示）。

* 数据维护：数据库备份（复制 db 到指定位置）、从备份恢复（提示重启）、导出 CSV、清理失效路径记录。

* 关于：版本、GitHub 仓库地址、开源说明。

### 6.9 交互与样式

* `Themes\Theme.xaml`：浅色主题，主色深蓝灰，圆角按钮，Segoe Fluent Icons 字体图标（无外部图片资源）；全局 `FontFamily` 使用系统已装的 HarmonyOS Sans SC（见第 3 节说明，不嵌入字体），正文 13px、表格行高 30。

* 全局异常捕获：`App.DispatcherUnhandledException` → 写 `data\logs\error-*.log` 并弹窗。

* 单实例运行（Mutex），防止数据库并发写冲突。

## 7. 实施步骤

按顺序执行，每步完成即可编译：

1. **项目初始化**

   * 创建 `RULES.md`、`QUEST.md`、`CHANGELOG.md`、`MEMORIES.md`、`IMPORTANT.md`（按用户规则，写入需求、状态、优先级、时间）。

   * `git init` + `.gitignore`（bin/obj/data/toberemoved）。

   * 创建 `MicaRomManager.sln`、`src\MicaRomManager\MicaRomManager.csproj`（WPF、net9.0-windows、`ApplicationIcon` 可省）、`tests\MicaRomManager.Tests`。

   * NuGet：`CommunityToolkit.Mvvm`、`Microsoft.Data.Sqlite`；测试项目 `xunit`、`Microsoft.NET.Test.Sdk`。

   * `Theme.xaml` 定义 `AppFontFamily` = `HarmonyOS Sans SC, Microsoft YaHei UI`（系统已装，不嵌入字体），并在所有 Window 应用。

   * `App.xaml` 全局资源合并、启动初始化 DB。

2. **核心层**

   * `Models\Game.cs`、`Emulator.cs`、`EmulatorTemplate.cs`、`GameStatus.cs`。

   * `Data\Database.cs`（建库、迁移、连接工厂）、`GameRepository.cs`（CRUD、按平台/状态查询、统计）、`EmulatorRepository.cs`、`SettingsRepository.cs`。

   * `Services\PathService.cs`（数据目录判定、封面/模拟器/导出路径）。

   * 单元测试：DB 建表 + CRUD 基本通。

3. **业务服务**

   * `ExtensionMap.cs`、`RomImporter.cs`。

   * `ArgsBuilder.cs`（占位符、引号、相对路径模式）+ `RomLauncher.cs` + `ProcessTracker.cs`。

   * `SharePackager.cs`（包结构、bat 生成、zip、manifest、体积估算）。

   * `EmulatorInstaller.cs`、`EmulatorUpdater.cs`、`FileRecycleBin.cs`、`CoverStore.cs`。

   * 单元测试：`LauncherArgsTests`（占位符/引号/中文路径/相对路径）、`ExtensionMapTests`、`SharePackagerTests`（临时目录生成 bat 并断言内容与目录结构）。

4. **主界面**

   * `MainViewModel.cs`（集合、筛选、命令、统计、异步导入进度）。

   * `MainWindow.xaml`：工具栏 + 筛选条 + DataGrid + 右键菜单 + 状态栏 + 拖拽。

   * 双击/Enter/F2/Delete 快捷键。

   * 编译并运行验证列表展示、排序、筛选、右键菜单可点击。

5. **编辑与分享窗口**

   * `GameEditViewModel` + `GameEditWindow`（封面选择、模拟器下拉、校验）。

   * `SharePackViewModel` + `SharePackWindow`（目标目录选择、打包模拟器/存档勾选、体积估算、导出、压缩、打开目录）。

6. **模拟器管理与设置**

   * `EmulatorManagerView`（用户控件）+ `EmulatorManagerViewModel`（模板新增、安装、编辑、默认绑定、更新、删除、测试启动）。

   * `SettingsWindow`（4 个 Tab）+ `SettingsViewModel`。

   * 接入 GitHub Release 检查（手动触发、异常静默提示）。

7. **打磨与验证**

   * 全局异常日志、单实例、缺失文件标记、空状态提示。

   * `dotnet build` 零警告（尽量）、`dotnet test` 全绿。

   * 按第 9 节清单手动跑一遍。

   * 更新 `CHANGELOG.md`、`MEMORIES.md`、`QUEST.md` 状态。

   * 创建 README（仅在用户确认需要时；默认不建）。

8. **GitHub 同步**

   * 提交代码；若用户提供远程仓库地址则 `git remote add` + push，否则提交后提示老板提供地址（规则 5）。

   * 发布首版时打 tag `v0.1.0` 并附 `CHANGELOG` 摘要。

## 8. 假设与决策

1. 首版只做 Windows 桌面、单语言（中文）、浅色主题。
2. 导入默认「仅索引」原文件，不移动/复制；设置中可切换为复制入库。
3. 不做在线刮削与自动元数据，封面手动指定。
4. 分享包只做「文件夹 + play.bat」，zip 为可选项；不做自解压 exe（避免杀软误报）。
5. 模拟器更新以手动替换为主，GitHub Release 检查为可选增强、仅点击时请求（免费 API，无需 token）。
6. 删除 ROM 文件一律走回收站并二次确认；「从库移除」不碰文件。
7. 数据库为单文件 SQLite，无并发多进程需求，加 Mutex 单实例即可。
8. 模拟器安装目录体积可能很大（RetroArch 数百 MB），打包前必须显示估算体积。

## 9. 验证清单

编译与测试：

* `dotnet build MicaRomManager.sln -c Debug` 成功。

* `dotnet test` 全绿（ArgsBuilder / ExtensionMap / SharePackager / DB）。

手动验收：

1. 空库启动不报错，界面布局完整，全界面（含表格、菜单、弹窗）使用 HarmonyOS Sans SC。
2. 导入文件夹（含 nes/sfc/gba/未知扩展名）→ 预览可改平台 → 列表新增数量正确，重复导入提示跳过。
3. 表格列可排序、可拖宽、筛选/搜索/收藏/缺失过滤生效。
4. 双击行 → 改标题/状态/标签/封面 → 保存后列表即时更新，重启后仍在。
5. 配置一个模拟器（安装到 data\emulators）→ 启动游戏成功 → 退出后次数 +1、时长有值、最近游玩时间更新。
6. 右键多选 → 分享打包 → 目标文件夹内 `Play-*.bat` 双击能在无本工具环境下启动游戏（换目录模拟）。
7. 导出并压缩 zip 成功，解压后仍可运行。
8. 模拟器管理：安装 zip、编辑参数、设默认、手动更新（旧目录进备份）、GitHub 检查更新（配了仓库时）。
9. 设置：切换导入策略为复制入库后导入的文件进入库目录；数据库备份/恢复可用。
10. 移到回收站后文件可在回收站找回。

## 10. 风险与对策

| 风险                 | 对策                                                   |
| ------------------ | ---------------------------------------------------- |
| 不同模拟器参数差异大         | 模板 + 自由编辑 + 占位符说明；参数错误仅启动失败，不影响数据                    |
| 打包体积巨大             | 导出前显示估算，可取消勾选打包模拟器/存档                                |
| 中文/空格路径导致 bat 启动失败 | 统一加引号、bat 首行 `chcp 65001`、`cd /d "%~dp0"`；测试用例覆盖中文路径 |
| 模拟器目录内多 exe 选错     | 安装时列出候选 exe 让用户确认，默认按名称相似度预选                         |
| GitHub API 限流      | 仅用户点击时调用，失败给可读提示                                     |
| WPF DataGrid 大库卡顿  | 内存过滤 + 虚拟化（默认开启），列宽固定；必要时分页（首版不做）                    |


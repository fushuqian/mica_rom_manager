# lite 分支：Rust + Tauri 2 等价重构计划

## 1. 目标

- 在 `lite` 分支用 Rust + Tauri 2 重写整个程序，**保留 v0.13.1 全部功能**，界面 1:1 复刻（注意：现有主题是浅色，不是暗色）。
- 直接读写现有 `data\mica.db`（schema v10）与 `data\` 目录，老板现有库、设置、封面、模拟器无需迁移。
- 轻量化目标：单个 `MicaRomManager.exe` ≤ 20MB；冷启动 ≤ 1s；1000 条列表筛选/排序 ≤ 200ms；常驻内存 ≤ 120MB。

## 2. 现状分析（已核实）

| 项 | 事实 |
| --- | --- |
| 代码量 | C# 6376 行 + XAML 1490 行；单文件自包含发布 |
| 技术栈 | WPF / .NET 9 / Microsoft.Data.Sqlite / CommunityToolkit.Mvvm / SharpCompress 0.50.7 |
| 数据库 | SQLite，`PRAGMA user_version = 10`；表：`games`、`emulators`、`emulator_platforms`、`platforms`、`settings`、`lookup_items` |
| 数据目录 | 优先 `<exe目录>\data`，不可写回退 `%LOCALAPPDATA%\MicaRomManager`；内含 `mica.db`、`covers`、`emulators`、`exports`、`logs`、`downloads`、`emulators\.backup` |
| 设置键 | `library.root`、`library.portable`、`library.import_mode`、`library.copy_on_import`、`network.proxy.mode`、`network.proxy.address`、`list_view_state` |
| 主界面 | 工具栏 8 项；左侧平台侧栏；8 个筛选控件；17 列（★/平台/标题/区域/语言/类型/年份/光碟数/格式/状态/评分/次数/时长/最近游玩/大小/标签/指纹）；右键菜单 18 项；编辑模式、拖放、状态栏 |
| 窗口 | GameEdit、BatchEdit、Settings（6 个页签：常规/模拟器管理/字典库/平台/数据维护/关于）、ImportWizard、SharePack、EmulatorManager、EmulatorEdit、DiscSelect、ExePick |
| 服务 | ArgsBuilder、PortablePath/LocalPath/PathService、RomHasher、RomImporter、ExtensionMap、PlatformFormats、LibraryOrganizer、CoverStore、FileRecycleBin、RomLauncher、EmulatorInstaller、EmulatorUpdater、EmulatorCores、EmulatorResolver、EmulatorTemplateStore、NetworkClientProvider、SharePackager |
| 测试基线 | `dotnet test` 103 项全通过（格式/路径/导入过滤/参数/整理/分享包/字典库/批量编辑等） |
| 环境 | rustc 1.99 msvc、MSVC 14.44 + Win SDK 10.0.26100、Node 24.20、WebView2 155.0.4283.62 已安装 |

## 3. 技术方案

### 3.1 选型（已确认）

- 后端：Rust（edition 2021，msvc）+ Tauri 2。
- 前端：Svelte 5 + TypeScript + Vite，不引 UI 组件库；表格、弹窗、筛选条全部自写，样式对齐 `Themes\Theme.xaml`。
- 数据库：`rusqlite`（bundled，内置 SQLite，不依赖外部 DLL）。
- 归档：`zip`、`sevenz-rust2`、`flate2` + `tar`、`unrar`（C++ 源码随 crate 编译，本机 VC++ 工具链已具备）。
- 网络：`reqwest`（`rustls-tls`、`json`、`stream`、`system-proxy`），代理三态与现版一致。
- 系统集成：`tauri-plugin-dialog`（文件/目录选择）、`tauri-plugin-opener`（打开目录/网址）、`tauri-plugin-clipboard-manager`（复制路径）、`trash`（回收站）、`walkdir`、`sha2`、`uuid`、`chrono`、`thiserror`、`serde`/`serde_json`。

### 3.2 目录结构（新增，WPF 版暂不动）

```
app/
  package.json  vite.config.ts  index.html
  web/                      # Svelte 源码（src）
    main.ts  App.svelte
    lib/{api.ts,theme.css,grid.ts,format.ts,events.ts}
    components/{Toolbar,PlatformSidebar,FilterBar,GameGrid,StatusBar,Modal,Toast,StarRating,...}.svelte
    views/{GameEditDialog,BatchEditDialog,SettingsDialog,ImportWizard,SharePackDialog,EmulatorManager,EmulatorEditDialog,DiscSelectDialog,ExePickDialog}.svelte
  src-tauri/
    Cargo.toml  tauri.conf.json  build.rs  icons/
    capabilities/default.json
    Resources/EmulatorTemplates.json      # 从 src\MicaRomManager\Resources 原样复制
    src/main.rs  lib.rs  state.rs  error.rs  models.rs
    src/paths.rs  portable.rs  settings.rs
    src/db/{mod.rs,games.rs,emulators.rs,platforms.rs,lookups.rs,migrations.rs}
    src/services/{hasher.rs,formats.rs,extension_map.rs,args.rs,importer.rs,organizer.rs,launcher.rs,share.rs,cover.rs,recycle.rs,installer.rs,updater.rs,network.rs,cores.rs,templates.rs}
    src/commands/{games.rs,emulators.rs,platforms.rs,lookups.rs,settings.rs,import.rs,share.rs,maintenance.rs,system.rs,network.rs,discs.rs}
```

- WPF 版 `src/`、`tests/`、`MicaRomManager.sln` 在 M0–M8 期间保留为行为参考；M8 验收通过后整体移入 `toberemoved/wpf/`（遵循规则 11）。
- 发布目录：开发期 `app/target/release/`；交付用 `publish-lite/`（exe + `Resources\` + `data\`）。测试时用环境变量 `MICA_DATA_DIR` 指向现有 WPF 数据目录，避免数据分叉。

### 3.3 运行时架构

- `AppState`：`data_dir`、`Mutex<Connection>`、`SettingRepo` 缓存。所有写操作在 `tauri::async_runtime::spawn_blocking` 中执行；命令返回 `Result<T, String>`（中文可读错误）。
- 前端启动时一次性 `games_list` 载入全部游戏（数千条量级），筛选/排序在前端内存完成（与 WPF CollectionView 语义一致）；写操作逐条/批量回传 Rust。
- 耗时任务用事件回报进度：`scan://progress`、`import://progress`、`hash://progress`、`install://progress`、`download://progress`（payload `{done,total,message}`）。
- 封面显示：启动时 `fs_scope.allow_directory(data_dir)`，前端 `convertFileSrc(coverPath)` 加载；不暴露其它目录。
- 单实例：不实现（与现版一致）。

## 4. 兼容性硬约定（重写时必须逐条对齐）

1. **Schema 不变**：`db/migrations.rs` 原样复刻 SchemaV1–V10 的 SQL 与 `PRAGMA user_version`；lite 不改 schema。打开旧库只做幂等迁移；打开后 `user_version` 仍为 10。
2. **时间格式**：`created_at`/`updated_at`/`last_played_at` 用 .NET `"O"` 等价格式 `%Y-%m-%dT%H:%M:%S%.7f%:z`（本地时区）；`SetHash` 同理。
3. **路径存储**：写库前经 `PortablePath::to_stored`（`%DATA%` 前缀优先；便携模式开启时程序目录内路径存相对）；读库后 `expand`。`disc_paths` 多行同样处理。模拟器 `exe_path`/`core_path` 为 `install_dir` 下简单文件名时不得当相对程序目录处理（MEMORIES 已有教训）。
4. **ArgsBuilder**：占位符 `{rom}/{romdir}/{romname}/{core}/{config}` 替换为**带双引号**的值，先替换 `"{token}"` 再替换裸 token，大小写不敏感，结果 trim。平台模板为空时：沿用模拟器模板；模板含 `{core}` 直接沿用；模拟器名含 `RetroArch` 用 `-L "{core}" "{rom}"`；否则模拟器模板为空用 `"{rom}"`。
5. **ROM 指纹**：≤128MB 全量 SHA256；更大文件 `fast:` + SHA256(文件长度 8 字节小端 + 偏移 0 / `len/2-512KB` / `len-1MB` 各读 1MB)，小写十六进制。
6. **导入去重**：库内路径已存在、本批路径重复、库内 hash 已存在、本批 hash 重复 → 计重复；显式选中的文件格式不符计 excluded，目录扫描时格式不符静默跳过。
7. **入库目标**：`libraryRoot\{folder 或 platform 或 未分类}\文件名`；同名追加 ` (1)`、` (2)`；移动跨盘失败退化为复制后删源。
8. **子库整理**：文件名 = 标题（非法字符替换 `_`、空白压缩、≤150 字） + 多碟 ` Disc-{n}`（n 从 1） + ` (区域)` + ` (语言)` + 原扩展名；区域/语言多值以 `,` 连接。只处理 platformDir 内文件，重名追加 ` (2..999)`。
9. **分享包**：目录含 `roms\`、可选 `emulators\`、`Play-{标题}.bat`、说明 txt；bat 内容：`@echo off` / `chcp 65001 >nul` / `cd /d "%~dp0"` / `start "" "{exe}" {args}`，UTF-8 无 BOM；可选 zip。
10. **模拟器安装**：支持 zip / 7z / rar / tar.gz，解压防目录穿越；更新资产挑选顺序 win64 → windows → `-win` → 任意 zip → 任意 7z → 首个资产；GitHub API `releases/latest`，UA `MicaRomManager/0.2`。
11. **代理**：`none` 直连；`system` 跟随系统（Windows 注册表）；`manual` 用 `host:port`（可带 http://，端口 1–65535），非法回退系统。API 30s、下载 30min 超时。
12. **设置键与 `list_view_state` JSON 结构完全不变**（camelCase，含 Search/Platform/Status/Tag/Region/Language/Developer/OnlyFavorites/OnlyMissing/SortColumn/SortDirection/ColumnWidths）。
13. **业务规则**：游玩结束后 `play_count+1`、`play_seconds+秒数`、`last_played_at=now`、`status` 由「未玩」自动改「在玩」；回收站删除走系统回收站；「从库中移除」不动文件；封面复制到 `data\covers\{guid}{ext}`。
14. **UI 文案、列宽、列顺序、快捷键（Enter 启动、F2 编辑、Ctrl+F 搜索）、右键菜单项**照抄现版。

## 5. 分阶段实施

> 版本节奏：M0–M2 产出 `0.14.0-lite-alpha`，M3–M5 `0.14.0-lite-beta`，M6–M8 `0.14.0-lite`。每阶段独立可运行、可回归。

### M0 工程骨架与构建链

- 新建 `app/`（package.json、vite、Svelte 骨架）+ `app/src-tauri`（Tauri 2 空壳，窗口 1280×780、最小 1000×600、居中、标题 `Mica ROM Manager`）。
- 引入插件与 `capabilities/default.json`（dialog/opener/clipboard 权限）。
- `tauri.conf.json`：`productName=MicaRomManager`、`identifier=com.fushuqian.micarommanager`、无边框装饰保持系统默认、图标用现有 `app.ico` 转 png/ico。
- 冒烟：`npm run tauri dev` 能起窗口；`cargo tauri build` 能出 exe。
- 验证：`npm run build`、`cargo clippy -- -D warnings`、`cargo test`、手动启动截图。

### M1 数据层、路径与设置

- `paths.rs`（exe 目录 data 可写探测 + LocalAppData 回退 + `MICA_DATA_DIR` 覆盖）、`portable.rs`（`%DATA%` 与相对路径）、`migrations.rs` + 各 repository（games/emulators/platforms/lookups/settings）。
- 命令：`settings_get_all`、`settings_set`、`settings_save_general`、`games_list`、`games_save`、`games_create`、`games_delete`、`games_delete_all`、`lookups_*`、`platforms_*`（基础 CRUD）、`emulators_*`（基础 CRUD）。
- 单测：迁移到 v10、旧库只读兼容（拷贝一份真实 `mica.db` 到临时目录断言 user_version/表结构/记录数）、便携路径、时间格式、空库初始化种子。
- 验证：用现有数据目录跑 `games_list` 数量与 WPF 版一致。

### M2 主窗口（只读）+ 视图状态

- 前端：工具栏、平台侧栏（数量统计）、8 个筛选控件、17 列表格（排序/列宽拖动/横向滚动）、状态栏（数据目录/状态消息/刷新时间）、星标与评分只读展示。
- 筛选语义照抄 `MainViewModel.FilterGame`：搜索匹配标题/文件名/路径/标签/开发商；平台、状态、区域、语言、开发商、标签；仅收藏、仅缺失；「全部/未指定」聚合项。
- 视图状态：启动恢复、变更 500ms 防抖保存到 `list_view_state`。
- 验证：1000 条数据筛选/排序耗时 ≤200ms；列宽排序重启后保持。

### M3 列表交互与启动

- 单元格编辑模式（平台/区域/语言/类型/状态下拉、标题/年份/发行商/开发商/标签文本、评分星标点击改分、再次点同一颗清空），保存走 `games_save`（全字段更新 + `updated_at`）。
- 右键菜单 18 项与工具栏命令全部接通；多选、批量选择计数；快捷键。
- 启动：`launch_game`（平台解析模拟器→平台核心→参数替换→`Command` 启动→等待退出→`AddPlayStats`）；指定模拟器启动子菜单；多碟弹出 `DiscSelectDialog`。
- 文件操作：打开所在目录、复制路径、刷新文件状态（IsMissing 重算）、设置封面（复制到 covers）、收藏切换、从库移除、移入回收站并移除。
- 验证：启动真实模拟器进程、退出后次数/时长/状态正确；回收站能在资源管理器还原。

### M4 导入、扫描与整理

- 导入向导：添加文件/文件夹、预览（标题/平台/大小/指纹）、批量指定平台、入库方式（链接/复制/移动）、递归开关、拖放（主窗与向导）、跳过重复统计。
- `platforms_scan`：按平台格式白名单扫描子库，补充新文件，清理文件已不存在的条目（链接入库按原路径确认）。
- 数据维护：`hash_recalc`（补算存量指纹，带进度）。
- 平台整理：`platforms_organize` 按命名模板重命名子库文件。
- 验证：100 个文件 / 3 种模式的导入结果与 WPF 版逐项一致；重复项计数一致。

### M5 编辑窗口、批量编辑、字典库与平台管理

- GameEditDialog（全部字段 + 封面预览/更换 + 多碟列表 + 备注）；保存/取消不改原对象。
- BatchEditDialog：11 类字段勾选写入，标签追加/移除、备注追加/覆盖，未勾选保持原值。
- 字典库页：区域/语言/平台/类型/发行商/开发商/标签的增删改，改名级联、删除受限。
- 平台页：默认模拟器、库内子文件夹、格式白名单、RetroArch 核心下拉（`cores\*.dll`）、启动参数默认填充、扫描/整理/清除失效。
- 合并多碟 / 拆分多碟（右键）。
- 验证：批量编辑 20 条后筛选仍正确（MEMORIES 已知坑）；字典改名后游戏字段同步。

### M6 模拟器管理与网络

- 模拟器管理左右布局：导入（zip/7z/rar/tar.gz）、运行调试、打开目录、编辑配置、版本更新（应用内下载安装 + 官网兜底）、删除（含 `.backup` 备份策略照旧）。
- 模板导入（`Resources\EmulatorTemplates.json` 原样复用）。
- `emulator_check_update` / `emulator_download_install`：资产挑选、下载进度事件、解压、`version` 回写。
- 设置-常规：数据目录、导入策略、库目录、便携模式、代理三态、测试 GitHub 连接。
- 验证：接入代理 127.0.0.1:7890 检查更新成功；下载 7z 安装包并回写版本；断网/代理错误有中文提示。

### M7 分享打包、便携与数据维护

- SharePackDialog：选游戏、是否含模拟器/存档、目标目录、可选 zip；生成 bat 启动器与说明文件；结果汇总（大小、警告）。
- 便携模式启动流程：库目录找不到 → 重新指定（自动重定位旧记录）或放弃旧库新建（清空游戏，保留模拟器/设置）；路径规范化。
- 数据维护页：补算指纹、清理失效、数据库大小与统计。
- 关于页：版本、GitHub 链接、数据目录说明。
- 验证：分享包拷到另一台/另一盘符双击 bat 可玩；便携重定位后路径全部更新。

### M8 打磨、发布与文档

- 功能对等清单逐条勾验（第 6 节）；性能与内存达第 1 节指标；200% DPI 与中文字体检查。
- 打包：`cargo tauri build --release`，取 `app/target/release/MicaRomManager.exe` + `Resources\` 组成 `publish-lite/`，从该目录启动冒烟。
- 文档：更新 `RULES.md`（本项目技术栈改为 Rust + Tauri 2）、`QUEST.md`（Q-018 记录及验收状态）、`CHANGELOG.md`、`IMPORTANT.md`（新构建/发布路径）、`MEMORIES.md`（新踩坑）；`ROADMAP.md` 标注 lite 分支定位。
- Git：每阶段一次提交（`feat(lite): ...`），推送阶段成果到 `origin/lite`：
  `git -c http.proxy=http://127.0.0.1:7890 -c credential.https://github.com.helper=manager push origin lite`
- 验收通过后按规则 11 将 WPF 版移入 `toberemoved/wpf/`。

## 6. 功能对等清单（验收用）

1. 工具栏：导入… / 新建条目 / 批量编辑 / 启动游戏 / 分享打包 / 打开目录 / 刷新 / 设置。
2. 平台侧栏含「所有游戏」与各平台数量，点击即筛。
3. 细筛：搜索（标题/文件名/路径/标签/开发商）、状态、区域、语言、开发商、标签、仅收藏、仅缺失、清除筛选、条数显示。
4. 17 列全在，列宽/排序/筛选重启保持；指纹列可排序并提示文件路径。
5. 编辑模式可改 8 类单元格，评分星标点击即存，双击非评分列启动。
6. 右键 18 项：启动、指定模拟器启动、编辑、批量编辑、合并多碟、拆分多碟、分享、打开文件夹、复制路径、设置封面、收藏、刷新、移除、回收站。
7. 导入向导三种入库方式、递归、拖放、重复/格式提示。
8. 平台管理：默认模拟器、子文件夹、格式白名单、核心、启动参数、扫描、整理、清理失效。
9. 字典库 7 类词条管理与级联。
10. 模拟器管理：模板导入、压缩包安装、调试运行、打开目录、编辑配置、更新下载安装、删除。
11. 设置 6 页签（常规/模拟器管理/字典库/平台/数据维护/关于）功能齐备。
12. 分享包（含 zip）与便携模式、路径重定位。
13. 代理三态与 GitHub 检查更新/下载。
14. 游玩统计（次数/时长/最近游玩/自动改状态）。
15. 数据库 schema v10 兼容，WPF 版可继续打开 lite 写过的库。

## 7. 测试与质量门

- Rust 单测：移植现有 103 项覆盖的规则（PlatformFormats、ExtensionMap、PortablePath/LocalPath、ArgsBuilder、RomHasher、RomImporter 过滤/去重、LibraryOrganizer、BatchEditChanges、SharePackager、EmulatorCores、ProxyAddress、LookupRepository、GameRepository）。
- 数据兼容测试：真实 `mica.db` 副本打开迁移 + CRUD 回环 + `list_view_state` 反序列化兼容。
- 前端单测：`vitest` 覆盖筛选、排序、视图状态序列化。
- 每阶段质量门：`cargo fmt --check` + `cargo clippy -- -D warnings` + `cargo test` 全绿 + `npm run build` 通过 + 手动冒烟清单。
- 发布门：`publish-lite/` 启动冒烟、导入→启动→分享闭环、与 WPF 版同库交替读写无异常。

## 8. 风险与对策

| 风险 | 对策 |
| --- | --- |
| `unrar` 需 C++ 编译 | 本机 MSVC 14.44 + SDK 已具备，M0 先做该 crate 编译验证；失败则暂用 7z 解压前置（提示用户先转 zip/7z），并记录到 QUEST 待办 |
| WebView2 缺失（旧 Win10） | 安装包用 NSIS 并内置 Evergreen Bootstrapper；便携包在关于页给出下载指引 |
| 前端自制表格行为偏差 | 每阶段与 WPF 版同库对照验收；行高 28px、单元格垂直居中、列宽默认值照抄 XAML |
| 数据库被两个程序同时打开 | 交付说明与 lite 启动提示：不与 WPF 版同时运行；SQLite 设 `busy_timeout=3000` |
| 大文件哈希阻塞 UI | 扫描/补算走 `spawn_blocking` + 进度事件 |
| 便携相对路径语义破坏模拟器 exe | 严格按 MEMORIES：只对安装目录/库目录/ROM/封面做程序目录相对化 |

## 9. 待老板确认的前置假设

- 前端采用 Svelte 5（非 React），产物更小、无运行时框架开销。
- lite 版版本号从 `0.14.0-lite` 起（不沿用 0.13.x）。
- 验收通过前 WPF 版保留在原位，不做删除或移动。

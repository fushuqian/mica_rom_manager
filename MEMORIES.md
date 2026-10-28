# 经验记录（MEMORIES）

## 2026-10-28（项目终止原因）

- 老板决定暂停 lite（Rust + Tauri 2）开发：该模式开发难度高于 WPF 主版本，功能实现仍有不完全之处；主版本 publish 产物也只有 100 多 MB，为轻量化重写一套的性价比过低。
- 后续策略：先把主版本功能做完，再来评估是否重构；期间 lite 分支冻结在 v0.14.0，不再投入功能开发。
- 教训：重构立项前先量化「收益（体积/启动/内存）」与「成本（双份代码维护、平台差异、测试覆盖）」；仅凭体积一项不足以支撑全量重写。

## 2026-10-28（lite 长任务进度 / 测试环境）

- 本机 GNU 工具链跑 `cargo test` 会在测试 exe 启动时报 `0xC0000139 STATUS_ENTRYPOINT_NOT_FOUND`（WebView2Loader / 运行库冲突），改用 `cargo test --lib --target x86_64-pc-windows-msvc` 即可正常跑通；构建仍保持 GNU 不动。
- 长任务进度统一走 `task://progress`，节流 120ms；服务层用 `TaskReporter` trait 解耦，单测传 `NoopReporter`（`#[cfg(test)]`）。
- 模态框会盖住底部状态栏，取消按钮必须放在模态框内部（导入向导、模拟器下载），状态栏只做进度文字展示。
- Windows GNU 链接 `cdylib` 时，引入符号量大的库（如 unrar-rs）会报 `export ordinal too large: N`；桌面端把 `[lib] crate-type` 收窄为 `["rlib"]` 即可，`cargo check` 不会发现这类链接错误，必须跑一次 `cargo build`。

## 2026-10-28（lite 追平主版本）

- rar 解压可用 `unrar-rs`（RARLAB UnRAR 的纯 Rust 移植，0.14.x，仅解压）。默认特性带 `crypto-aws-lc` 需要 C 工具链，Windows GNU 工具链下要写 `default-features = false, features = ["crypto-rust"]`。API：`RarArchive::open(File)` → `by_index(i)` → `entry.unpack_in(dir)`。
- 前端 `file_exists` 带 `%DATA%` 的便携路径必须先走 `AppState::expand`，否则碟片「文件缺失」标记在便携模式永远为假。
- 表格列若没有真实字段（光碟数/格式），排序要给列补 `sortValue` 函数，不能只靠 `sortKey`；可排序判断统一用 `canSort()`，否则表头点了没反应。

## 2026-10-27（lite / Rust + Tauri 2）

- Tauri 2 的 `reqwest` 要用 `default-features=false` + `rustls-tls` + `system-proxy`，否则 Windows 系统代理不生效；手动代理用 `Proxy::all`，直连用 `.no_proxy()`。
- 7z 用 `sevenz-rust`；rar 早期只能调外部 `7z.exe`/`UnRAR.exe`（现已改为 `unrar-rs` 内置优先、外部兜底，见 2026-10-28）。
- 前端 `@tauri-apps/plugin-opener` 的 `opener:default` 只含 open-url/reveal-item-in-dir，`openPath` 必须额外加 `opener:allow-open-path` 权限，否则调用被拒。
- 模拟器 `exe_path/core_path` 的相对语义仍与 WPF 一致：只展开 `%DATA%`，不拼程序目录；完整 exe 由 `resolve_exe_path` 结合安装目录计算。
- SQLite 备份 `VACUUM INTO` 或 `wal_checkpoint` 后再复制，能避免 WAL 未落盘导致备份缺数据；恢复用 `ATTACH` + 版本校验 + 逐表 DELETE/INSERT。
- Tauri dev 进程会锁住构建目录：`cargo clean`/`clippy` 会提示「Blocking waiting for file lock」，等 dev 退出或直接容忍等待即可。
- `cargo fmt` 首次对全库生效会改很多换行；先提交前统一跑一次，之后保持 `--check` 干净。
- 前端构建的 Svelte a11y 警告（`<div onclick>`）不影响产物；自绘右键菜单要接受该警告或用 `<button>`。

## 2026-10-27

- 平台级启动参数为空时不能固定回退 `"{rom}"`：应沿用模拟器自带 `ArgsTemplate`；RetroArch 必须带 `-L "{core}" "{rom}"`，否则双击没有核心直接启动失败。平台设置里选 RetroArch 时自动补默认参数，后台解析也要有同样的兜底。
- 主界面筛选侧栏用 ListBox 绑定 `SelectedItem` 时，重建列表的 `Clear()` 会把选中值写成 null；在字段的 `OnXxxChanged` 里对 null 直接 return，才能保证筛选不被意外清空，重建后再按名字回填选中项。
- 状态字段不能只靠用户手填：在 `AddPlayStats` 落库时用 `CASE WHEN status='未玩' THEN '在玩'` 同步，迁移里也要把 `play_count > 0` 的历史「未玩」修正，否则用户会觉得状态「没用」。
- 评分星标放在 DataGrid 只读 CellTemplate 里点击时，要同时处理行双击启动：`OnRowDoubleClick` 中判断原始命中的 `DataGridCell.Column` 是否为评分列，避免连点星标误启动游戏。
- 老板说的「文件路径改成 hash 值」只指列表展示与排序筛查，不改文件命名、不改库结构；提需求时容易字面误解，先问清楚。
- `games.rom_path` 有 UNIQUE 约束；写测试造数据时多条记录不能共用同一路径，否则 Insert 抛 `SQLite Error 19: UNIQUE constraint failed`。
- 指纹列可直接用于人工筛查：同内容不同文件名的重复条目排序后相邻，肉眼即可发现。
- 模拟器 `exe_path/core_path` 可能是相对 `install_dir` 的简单文件名（如 `retroarch.exe`），不能让便携相对路径逻辑对任意非绝对路径拼程序目录，否则会破坏该语义。只有安装目录本身、库目录、ROM/封面走程序目录相对化。
- `AppServices` 未初始化时 `LocalPath.Expand` 不会展开 `%DATA%`；`PortablePath.Expand` 必须识别 `%DATA%` 前缀并原样返回，否则单元测试会把它误当相对路径。

## 2026-10-26

- 本机原无 .NET SDK（仅运行时），`dotnet build` 报 "No .NET SDKs were found"；`winget install Microsoft.DotNet.SDK.9` 装 9.0.320 解决。构建用全路径 `C:\Program Files\dotnet\dotnet.exe` 更稳。
- 唯一运行/数据目录：`d:\Trae\MICA_ROM_MANAGER\publish`（exe 与 data 同级，便携模式）。每次交付都执行 `dotnet publish src\MicaRomManager\MicaRomManager.csproj -c Release -r win-x64 --self-contained true -o publish`，并从 publish 启动验证；禁止再从 bin\Release 或其它位置启动，否则数据会分叉。
- `.gitignore` 写 `data/` 会匹配任意层级的 data 目录，本项目因此漏提交了 `src/MicaRomManager/Data/` 整个数据层。忽略目录一律加前导 `/` 锚定仓库根。
- 便携发布目录 `publish/` 是 self-contained 产物；只拷贝 bin 的 exe 进 publish 会让程序因缺运行时崩溃（apphost 变框架依赖）。要更新就用 `dotnet publish -o publish`。
- 数据库迁移验证：SQLite 主文件头 offset 60 的 4 字节大端即 user_version；表结构 SQL 明文存在文件里，可直接搜字段名确认。
- GitHub 推送走 `http://127.0.0.1:7890` 代理；本机 `.gitconfig` 里 github.com 指定了 `gh auth git-credential` 助手但 gh 未登录，会卡死在 askpass。命令行用 `-c credential.https://github.com.helper=manager` 覆盖为 GCM 即可；也可用 `-c credential.helper=` 全空然后让系统级 `manager` 生效。
- GCM 在非交互模式（`GIT_TERMINAL_PROMPT=0`）下可直接返回 token，不弹 UI；API 调用可用 `"protocol=... nhost=github.com n n" | git credential fill` 获取。
- csproj `ApplicationIcon` 设好后，WPF 窗口不设 `Icon` 也会自动用 exe 图标。

## 历史经验（v0.1~v0.2）

- SharpCompress 0.50 的入口改为 `ArchiveFactory.OpenArchive(path)`，旧的 `ArchiveFactory.Open` 已不存在；`entry.WriteToDirectory(dest, options)` 仍是扩展方法，需 `using SharpCompress.Archives;` 与 `using SharpCompress.Common;`。
- 代理三态：`HttpClientHandler.UseProxy=false` 表示直连；`UseProxy=true` + `Proxy=null` 走 Windows 系统代理；手动模式用 `new WebProxy("http://host:port")`。
- `DataGridTextColumn` 生成的 TextBlock 默认垂直拉伸，行高较大时文字会贴顶；统一加 `ElementStyle="{StaticResource DataGridTextStyle}"`（内含 VerticalAlignment=Center）。
- GitHub Release 资产按名字挑 Windows 包：优先含 win64 / windows / -win 的 zip 或 7z，再退回任意 zip、任意 7z，否则会出现误装 3DS 构建之类的问题。
- WPF 弹窗（MessageBox）在 200% DPI 下用 UIA 取不到子按钮，`WM_COMMAND IDYES` 也不可靠；端到端下载验证不要依赖自动化脚本，手动跑更省时间。
- 本机 GitHub 直连偶发不可用，系统代理 127.0.0.1:7890 可用；PowerShell 脚本文件无 BOM 时中文按 ANSI 读取会乱码，脚本里避免中文字面量。
- HarmonyOS Sans SC 的 TTF 内部族名核实为 `HarmonyOS Sans SC`（Bold 文件 full name 为 `HarmonyOS Sans SC Bold`），WPF 直接按系统字体名引用即可，无需嵌入。
- WPF `DataGrid` + `CollectionView Filter` 足以支撑数千条 ROM 的排序筛选；避免在单元格里做重活。
- 分享包启动器 bat 必须 `chcp 65001` + `cd /d "%~dp0"`，并对所有含空格/中文的路径加引号。
- WPF 项目里 `ImplicitUsings` 对 XAML 临时工程（`*_wpftmp.csproj`）不生效，`Path`/`File`/`Directory`/`HttpClient` 会报找不到类型；缺的 `using System.IO;`、`using System.Net.Http;` 要显式补。
- `JsonElement` 是结构体，`FirstOrDefault` 返回 `ValueKind=Undefined` 而非 null，不能用 `??`；用 `ValueKind == JsonValueKind.Undefined` 判断。
- 截图验证窗口时直接用屏幕拷贝会截到其它窗口；用 `PrintWindow(hWnd, hdc, 2)` 更稳。本机 200% 缩放，窗口逻辑尺寸 1280x780 对应物理 2560x1560，截图尺寸会只有一半，注意别误判控件被裁。

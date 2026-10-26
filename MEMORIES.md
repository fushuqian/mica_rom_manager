# 经验记录（MEMORIES）

## 2026-10-26

- 本机原无 .NET SDK（仅运行时），`dotnet build` 报 "No .NET SDKs were found"；`winget install Microsoft.DotNet.SDK.9` 装 9.0.320 解决。构建用全路径 `C:\Program Files\dotnet\dotnet.exe` 更稳。
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

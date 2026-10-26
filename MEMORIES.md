# 经验记录（MEMORIES）

## 2026-10-26

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

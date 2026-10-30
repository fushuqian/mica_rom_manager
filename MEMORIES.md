# 经验记录（MEMORIES）

## 2026-10-30（AI 策略收口）

- 靠关键词判意图的规则放进服务主循环，改一次就要动状态机；抽成 `AiReplyPolicy` 后，策略可注入（测试里给空的标记表就能验证「不拦截」），说明真的解耦了。
- 中文按字符计数：`content.Length < 30` 里 30 个汉字就是一句话，很容易踩到阈值。写测试要按真实长度构造正文，别凭感觉。
- 截断反问句时，截断点所在的标点也会一起去掉（保留的是「年份 2009 有误」而不是「…有误。」），写断言前先跑一遍确认。
- xUnit 分析器会把 `Assert.Equal(1, collection.Count)` 报成 xUnit2013，用 `Assert.Single` 才干净；本项目 Release 构建要求 0 警告，这类提示会直接打断构建。

## 2026-10-30（安全与发布）

- **PowerShell 脚本里不要写中文**：无 BOM 的 `.ps1` 被系统按 ANSI 读，中文字符串会被截断成非法引号，直接 ParserError。`tools\*.ps1` 一律只用英文，中文提示放到 C# 或文档里。
- API Key 这类只在本机用的敏感值，用 `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)` 加密即可，net9.0-windows 直接可用，不必额外加包；密文带 `dpapi:` 前缀，读取时前缀判断能同时兼容旧明文。
- 加密迁移要幂等：`Migrate` 里先判断 `IsProtected` 再写，否则每次启动都重写 settings。
- 换机器/换用户后 DPAPI 解不开属正常，返回空值让用户重填，不能抛异常阻断启动。
- `HttpClient` 的「改配置重建」不要 `Dispose` 旧实例：可能还有并发请求握着它（标签/翻译是 2 路）。加锁换引用即可，泄漏一个连接池比 `ObjectDisposedException` 便宜。
- 工具/后台异常一定要落日志：只回给模型或只弹一次提示，事后完全无法复盘。

## 2026-10-30（CI 与补测）

- GitHub Actions 用 `windows-latest` 可以跑 `net9.0-windows` + `UseWPF` 的测试工程；`dotnet test --no-build` 必须和前一步 build 用同一个 `-c`，否则找不到产物。
- 需要测内部实现时用 csproj 的 `<InternalsVisibleTo Include="MicaRomManager.Tests" />` Item，比手写 AssemblyInfo 干净。
- 版本比较别用 `string.CompareOrdinal`：`0.9` 会被判成比 `0.10` 新。按数字段解析成 `Version` 再比。
- `Directory.Build.props` 一旦集中了 `Nullable`/`ImplicitUsings`，各 csproj 里的同名项要删掉，否则以后改一处忘一处。
- 给路径/哈希这类静态服务加「可注入阈值或基目录」的 internal 重载，成本很低，但让采样分支和便携分支第一次变得可测。
- 测试里造文件必须真实落到断言期望的目录：`CreateFile("RetroArch\\retroarch.exe")` 与 `Path.Combine(_dir, "retroarch.exe")` 是两个地方，写错不会报错，只会 Null。

## 2026-10-30

- AI 助手：取消或失败要在**同一次循环里**把本轮所有 tool_call 的响应补齐；只 append 了带 `tool_calls` 的 assistant 消息就抛 `OperationCanceledException`，历史里就会留下没有配对 tool 响应的消息，之后每次请求都会被端点 400 拒绝，且用户看不出来原因。
- 「执行失败」不能回填成「用户取消」，模型会据此告诉用户「已按你的意思取消」，事实相反；失败要带上原因并要求模型不要自动重试。
- OpenAI 兼容端点对 `tool_call_id: null`、`tool_calls: null` 并不都宽容；请求体序列化统一用 `DefaultIgnoreCondition = WhenWritingNull`。
- 覆盖正在使用的 SQLite 文件前必须 `SqliteConnection.ClearAllPools()`，覆盖后必须 `PRAGMA quick_check`，并且先自动备份、校验失败自动回滚，否则一次坏备份就把程序变成打不开的空壳。
- 发送前判断空间要用 `DriveInfo(Path.GetPathRoot(target)).AvailableFreeSpace` 现场探测；`DriveEntry.FreeBytes` 是扫描时快照，几十分钟后就是错的。
- 发布目录缺内容文件属于静默故障：`DeviceProfileStore` 找不到内置 JSON 直接返回空数组，界面只是「没有设备」。发布后必须校验 `Resources\*.json` 与 exe 版本，已脚本化到 `tools\verify-publish.ps1`。
- 本机工作区在同步盘下，实测出现过 `obj` 里 221 个 `*.sync-conflict-*`，以及单个源文件在两次读取之间被替换（`CancelPending` 的 `ToolCallId` 一会有一会没有）。改文件前重新读、改完用 `git diff` 复核，不要拿早前的读取结果当当前状态。

## 2026-10-27（AI 助手）

- 服务层的 `AiChatMessage` 与 UI 展示模型 `MicaRomManager.Models.AiChatMessage` 同名，引用两个命名空间会歧义；ViewModel 里用 `using AiChatItem = ...` 别名，测试里用 `AiWireMessage` 别名。
- OpenAI 工具调用一轮里如果有多个 `tool_calls`，必须在下一轮请求前给**每个** call id 补 tool 消息；遇到待确认操作时，后续未执行的 call 也要回一条「未执行」占位，否则历史不合法。
- 写操作不要在工具执行时直接落库；只返回 PendingAction，让 UI 确认后再执行，再把执行结果作为 tool 消息喂回模型继续总结。
- `JsonElement?` 用 `if (x == null) return` 之后编译器不会自动收窄可空结构体，后续要显式 `.Value`。
- 主窗口右侧面板用固定像素列 + GridSplitter；开关时同时改两个 ColumnDefinition 宽度（分隔条列与助手列），并把状态存 `ui.assistant_open`。
- `ScrollViewer` 里放 `ItemsControl` 做聊天流，集合变化后用 `Dispatcher.BeginInvoke(..., DispatcherPriority.Background)` 调 `ScrollToEnd()`，否则布局未完成会滚不到底。
- 启动推荐是独立卡片类型（启动/换一个/算了），不套用通用「确认执行」；「换一个」本地随机切换，不重新请求模型，避免额外等待和费用。
- 「随便推荐一个」必须从**全部游戏**里随机，不能用 `FilteredGames`（否则会被当前筛选限制）；只有用户明确要求按条件推荐时才走筛选。
- 库里大量 GBA 条目标题是纯数字（002、003…）且元数据空白；随机推荐要先排除纯数字标题，并优先未玩/在玩、文件存在的游戏。
- DeepSeek 官方联网搜索用 `POST https://api.deepseek.com/v1/tools/search`（Bearer 同一个 API Key），请求 `{query,count,need_content}`，结果有 `snippet/summary/site_name/date`；按成功请求计费，不是按 token。`need_content=false` 时用 `summary` 喂模型足够。
- 搜索只在 BaseUrl 为 `api.deepseek.com` 时可用；别用 `Contains("deepseek")` 粗略判断，统一走 `AiSearchOptions.IsDeepSeekEndpoint`。
- 刮削开启搜索时，单条 = 1 次搜索 + 1 次模型；先搜索再拼进提示词，搜索失败不能阻断 AI 兜底。
- 助手写操作不要在文字里先问「要不要我…」再等用户答应；直接调用工具出确认卡片，用户不需要会点取消，否则同一件事要确认两遍。回答也不主动追问下一步。
- 右键菜单里的 AI 入口收敛为一个「AI 问问」，点击时由 MainWindow 直接调助手 ViewModel（助手实例在窗口层，不在 MainViewModel）。
- 光靠提示词管不住模型反问「要我…吗？」；在 `RunLoopAsync` 里检测末尾征求同意的问句，补一轮「直接调用工具」的隐藏提示，并删掉正文里的反问句，才能稳定地先出卡片。
- 介绍类提问（介绍/说说/怎么样）要防止模型只弹写操作卡片：Pending 出现且正文为空或过短时，给该 tool call 回一条「先写介绍再出卡片」的 tool 结果并重跑一轮；已有 preface（拦下的反问前正文）时不要拦，否则会把正常修正卡片误伤。
- 提示词改成内置 `const` 模板 + `{{变量}}` 占位符（正则替换，未知变量原样保留），自定义值存 `ai.prompt.{id}.system/user`；`Store()` 允许在单元测试环境（AppServices 未初始化）为 null 并回落默认。

## 2026-10-27（AI 接入）

- 含 JSON 的提示词用 `$$"""..."""` 原始插值字符串：字面量花括号直接写，插值表达式用 `{{...}}`；用单个 `$` 会把 JSON 的 `{` 当成插值起始而编译失败。
- `AiChatClient` 每次调用从 `settings` 读最新配置；代理变更统一走 `AppServices.RebuildNetwork()`，那里要同时重建 `Ai` 客户端。
- 兼容各端点时不要依赖 `response_format`；要求模型输出 JSON，再用 `AiJson` 去围栏、取首尾花括号容错解析。
- `DeviceProfile` 是普通类、没有属性通知；用 AI 建议刷新表单时，要整体替换 `Profiles` 里的对象并重新赋值 `SelectedProfile`，不能只改字段。
- `Window.GetWindow(vm)` 只接受 `DependencyObject`；在 ViewModel 里找 Owner 用 `Application.Current.Windows.OfType<SettingsWindow>()`。
- `DriveInfo` 只接受盘根；扫描任意目录做测试或识别时先 `Path.GetPathRoot(fullPath)`。
- AI 设备识别只上传目录名与扩展名统计，不上传文件名；扫描限制 3 层 / 400 个目录，单目录最多统计 2000 个文件，避免卡死。
- 自然语言操作不生成 SQL：只接受受控 JSON 白名单（set_status / toggle_favorite / add_tag / remove_tag），校验后作用于当前筛选结果。

## 2026-10-27

- 发送任务队列的「最后一批完成后停队列」和「新任务入队时启动队列」要在同一把锁里判断 IsRunning，处理完再检查队列是否为空，否则收尾与新任务并发时会跑出两个消费循环。
- 大文件复制进度用 1MB 缓冲直接读写并在回调里节流（约 150ms 一次），既能看到字节级进度，也不会刷爆 UI；每游戏任务的百分比按累计字节算。
- 跨盘发送时目标路径必须校验在盘根之内，模板里的 `..` 段也要清洗掉；单文件失败只计数+警告，不能中断整批。
- 设备配置合并优先级：同 Id 用户 `data\devices\*.json` 覆盖内置 `Resources\DeviceProfiles.json`；删掉覆盖文件即恢复内置。
- TrimUI Smart Pro 的 SD 卡目录规则可看 `Emus\<系统>\config.json` 的 `rompath` 与 `extlist`：`Roms/FC`、`Roms/SFC`、`Roms/PS`、`Roms/MD` 等；`PSBIOS` 是 BIOS 目录、`FFMPEG` 是播放器，不能当游戏平台映射。
- 「特注」是独立字段，不是备注/标签的别名；整理文件名顺序固定为 `标题 [Disc-n] (区域)(语言)(特注).扩展名`，新增字段要同步 schema、仓储、编辑窗口与整理逻辑。
- DataGrid 行内编辑时不能直接 `ICollectionView.Refresh()`，否则切格会抛「AddNew 或 EditItem 事务过程中不允许 Refresh」；ApplyFilter 要先判断 `IEditableCollectionView.IsEditingItem`，挂起刷新并在 RowEditEnding / CurrentCellChanged 后补做。
- 左侧平台列表刷新不要 `Clear()` 后重建：选中项会被绑定写成 null，视觉上像筛选被重置。改为按名字增量增删/移动并更新计数，保持项目对象不变，选中状态自然保留；「清除筛选」也不应改 `SelectedPlatform`。
- 平台级启动参数是「额外参数」，语义为「模拟器默认参数 + 平台额外参数」，不是整体覆盖；平台留空就只用模拟器默认参数。界面不要自动把默认参数填进平台框，否则会重复。RetroArch 默认参数必须带 `-L "{core}" "{rom}"`。
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

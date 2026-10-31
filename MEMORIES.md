# 经验记录（MEMORIES）

## 2026-10-31（设置：macOS 三段式与扁平化）

- 老板要的「苹果风」= 左分类 → 中二级 → 右内容三段式 + 扁平（不要白卡片）。树形折叠那种一层内嵌一层不是他要的，别自作聪明。
- 单页分类把中间列收起很好实现：把「分隔线 + 二级列表 + 分隔线」装进一个 `Grid` 放在 `Width="Auto"` 的列里，整体 `Visibility=Collapsed` 就自动收成 0，不用改列宽。
- 扁平化最省事的做法是重定义 `SettingsCardStyle`：背景透明、`BorderThickness="0,1,0,0"`、去掉圆角，十来个页面一处改完全体生效。
- `ListBoxItem` 里的图标要用 `Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=ListBoxItem}}"` 才能跟着选中态变色，否则永远是默认色。

## 2026-10-31（设置二级菜单 / 排序按钮）

- WPF TreeView 做设置导航够用：`HierarchicalDataTemplate` + 自定义 `TreeViewItem` 模板（34 高、缩进 16、选中用 AccentSubtle）。图省事只设 `Background="Transparent"` 的 ToggleButton 会露出 Aero 的蓝色勾选块——折叠箭头必须自己写 `ToggleButton.Template`（只留 ContentPresenter）。
- 页签只当内容宿主：把 `TabControl` 模板改成 `ContentPresenter ContentSource="SelectedContent"`，页签头就消失了，左侧换什么导航都行，9 页 XAML 一行不用改。
- 列表拖动排序在 WPF 里不稳（Drop 目标判定、禁止符号），上移 / 下移按钮更省心：编辑模式 + 点到即存，比 DoDragDrop 可靠得多。
- 字典库这种「字段清单」页要能减项：把 `Kinds` 里的项去掉即可，数据（`lookup_items`）不用删，导入时 `LookupSync` 仍会补平台。

## 2026-10-31（设置内嵌主窗口）

- 模态窗口（`ShowDialog`）会把 owner 置为 disabled，Win11 对 disabled 窗口不再保持 Mica/Acrylic，于是主窗口看起来「褪成纯色」——靠重下发布景属性救不回来，改结构才根治。
- Window 改 UserControl 的改动面比想象小：换根元素、删掉 WindowChrome/标题栏/关闭条、`Owner as MainWindow` 改成 `Window.GetWindow(this) as MainWindow`（UserControl 也能拿宿主窗口）。剩下的 9 页 XAML 一行没动。
- 嵌进主窗口的页面要**懒构造**（`ContentControl` 第一次切过去再 `new`），否则设置页的查库/模板加载会回到启动路径上。
- 同一份列表既要「自定义顺序」又要「按厂商分组」时，分组用 `ListCollectionView.GroupDescriptions` 开关即可，别维护两份集合；分组只在打开开关时生效，平铺时顺序完全跟用户。

## 2026-10-31（平台拖放排序）

- WPF 列表拖放排序的稳妥写法：`PreviewMouseLeftButtonDown` 只记起点和命中的项，`PreviewMouseMove` 里超过 `SystemParameters.Minimum*DragDistance` 才 `DoDragDrop`——直接在 MouseDown 里拖会把单击选中吞掉。
- 「分组表头」和「用户自定义顺序」天然打架：分组会按分组键重排，用户的顺序就失效了。二选一时留手工顺序，厂商信息降级成行内浅色标记；真要分组就得接受组内排序。
- 顺序这种「用户偏好」放 settings JSON 数组（`ui.platform_order`）比加数据库列省事：不用迁移、不用每次开窗口写库，新主机还能靠兜底排序落到合理位置。

## 2026-10-31（平台排序）

- 机型顺序不能靠展示名：字典序会把 GameCube 塞进 Famicom 和 Super Famicom 之间。要单独一张「首发年份」表按年代排，同年再按展示名兜底，自建平台 `int.MaxValue` 排最后。
- 增量同步的下拉集合（只增删、不排序）换了排序规则不会生效，必须补一遍 Move 对齐顺序，否则用户要重启才能看到新顺序。
- 「自定义」标记别用 `PlatformCatalog.Common` 那种老清单判断，短名（GB/NGC/N64）会被误标；以预置清单里是否存在为准。

## 2026-10-31（平台名与分组）

- 日欧美叫法不同的主机用一张表按习惯切换，**不要把全称塞进预置清单**：`PlatformPresets.json` 在 `data\` 有一份用户副本，改随程序发布的 JSON 到不了老用户，写在代码里才稳。库里始终存短名（NES/SNES/PCE/MD），展示层再翻。
- 展示名这类全局静态映射改完必须主动刷界面：`Display` 是只读计算属性，集合项复用时不会触发 `PropertyChanged`。给列表项加 `RefreshDisplay()`，保存后遍历调一遍。
- WPF 列表分组用 `ListCollectionView` + `PropertyGroupDescription`，**分组顺序等于源集合里首次出现的顺序**，所以要先按「厂商权重 → 名字」排好源集合再交给视图；「所有游戏」这种不参与分组的项给空分组名，表头用 DataTrigger 折叠掉。
- 测试里别把厂商名当平台名传给 `VendorRank`：入参是平台，厂商不在表里会被算成「自定义」。
- 数据目录这类「程序自己的路径」不要摆在设置里给用户看：便携模式下存的是相对路径，显示绝对路径既没意义又容易误导。

## 2026-10-31（设置页视觉层次）

- 「没有层次感」的典型症状是：所有信息一个字号、一个颜色、一个间距直排。修法不是加线加框，而是建立**三级字号（20 页标题 / 13.5 分组标题 / 12.5 字段与正文 / 11.5 提示）+ 分组卡片 + 统一间距节奏**；卡片靠「比底色亮一档 + 1px 发丝线」分层，别用 DropShadowEffect（会把子树栅格化）。
- WPF 里想「控件最宽 560、左对齐、窗口拉宽也不跟着变长」，别给外层 StackPanel 设 `MaxWidth + HorizontalAlignment="Left"` —— StackPanel 会按内容的 DesiredWidth 收缩，结果同一页里有的输入框 440、有的 760。正解是给**列定义**设 `MaxWidth`：`<ColumnDefinition Width="*" MaxWidth="620"/>`。
- 页头右侧放主按钮（保存）比放页底更稳：内容再长也不会被挤到滚动区外，还顺手把「主操作」这件事说清楚了。
- 设置页出图用 `RenderTargetBitmap` 最省事：进程声明 `PerMonitorV2`（app.manifest）→ `VisualTreeHelper.GetDpi(root).PixelsPerDip` 是 2 → 位图尺寸 = DIP × dip、位图 DPI 声明 `96 × dip`，再叠一层模拟桌面渐变，就能拿到 2× 的稳定渲染图；直接 `BitBlt` 屏幕会受 DPI 虚拟化和遮挡影响。
- 高 DPI 下 `CopyFromScreen`/`BitBlt` 拿到的是物理像素，而未声明 DPI 感知的进程取 `GetWindowRect` 得到的是被缩放的逻辑坐标，两者不一致就会截到桌面而不是窗口。

## 2026-10-31（打开设置卡顿定位）

- 「窗口打开慢」先量再改：写个 `[STAThread]` 控制台程序引用主工程，`new App().InitializeComponent()` → `AppServices.Initialize()` → `new MainWindow().Show()` → 再 `new SettingsWindow()`，用 Stopwatch 打点，能直接把「构造 / 首帧 / 各页首次切换」拆开。本项目实测构造 154ms、首帧 241ms，**大头是 XAML 解析，不是查库**（平台补种 29 行只要 13ms）。
- `SettingsWindow` 里内联的子页面（`<views:EmulatorManagerView />`）在 XAML 解析时就会**立刻构造**，即使那个页签没被选中；它们的 ViewModel 构造、查库、读 JSON 全都算在「打开设置」头上。要按需加载就换成 `<ContentControl>` + 选中页签时再 `new`。
- `DriveInfo.IsReady` / `VolumeLabel` / `RootDirectory` 不是内存操作，会去问设备：映射的网络盘掉线、读卡器没插卡、光驱空仓时要等到系统超时（秒级到十几秒）。**绝不能在 UI 线程同步跑**，尤其不要在窗口构造里跑。
- `PlatformRepository.ListAll()` 原来对每个平台调一次 `EnsureExists`（各自开连接 + 各自写事务）；`Database.Open()` 每次都会带 `busy_timeout=3000`，库被同步盘/杀毒/别的进程占用时这些写会逐个排队。补行改成「一个连接 + 一个事务批量 INSERT OR IGNORE」，无缺项时零写。

## 2026-10-31（v0.29.0 三栏外壳）

- 悬停投影的阴影层**必须自带底色**：`Background="Transparent"` 的空 Border 加 `DropShadowEffect` 投不出阴影（没有任何 alpha 可投影）。做法是给阴影层一个低 alpha 的抬升色（`ShellPanelLiftBrush` / `MainPanelLiftBrush`），和面板同色相，既出阴影又只把面板抬一档。
- `Style` 触发器里的 `Storyboard.TargetName` 找不到同一 Grid 里的兄弟元素（namescope 只在模板内有效）。要么写在 `ControlTemplate` 里，要么在 code-behind 用 `MouseEnter/MouseLeave` + `BeginAnimation`，本项目用后者。
- 列表改版后旧列宽会“复活”：`RestoreViewState` 用保存的 `ColumnWidths` 覆盖 XAML 宽度，必须同时升 `LayoutVersion`（v3→v4）才会套用新默认列宽与默认隐藏列。
- 半透明面板叠在 Acrylic 上时，壁纸花纹会顶穿文字；在窗口最底层压一层 10% 薄雾（`ShellScrimBrush`），既保留透感又把对比度拉回来。
- 截图验证：`CopyFromScreen` 只能拍到最上层窗口，`SetForegroundWindow` 经常抢不到前台；可靠做法是 `SetWindowPos(HWND_TOPMOST)` → 截图 → 再设回 `HWND_NOTOPMOST`。`PrintWindow` 不抢焦点，但拍不到 DWM 的 Acrylic 背景。
- 深色调色板是按资源键覆盖的字典（`ThemePalette.Dark`），**新增画刷键必须同时补深色值**，否则深色模式下这些元素仍是浅色。

## 2026-10-31（WPF 深色 / 系统背景）

- 只改 `SolidColorBrush.Color` 不够：`StaticResource` 在样式里可能已被冻结，深色调色板会出现“背景变了、文字还是深色”。要么全量换 `DynamicResource`，要么重建样式。本项目把 24 个 XAML 的画刷引用统一换成 DynamicResource。
- WPF 给卡片加 `DropShadowEffect` 如果直接加在承载内容的 Border 上，会把整棵子树栅格化、文字发虚；正确做法是：**背景层 Border 单独承载 Effect，内容层透明 Border 叠在上面**。阴影层必须是可见的背景（75% 白），只给 10% 透明黑再乘 Effect Opacity 会几乎看不见。
- WPF `Border.CornerRadius` 不会裁剪子元素；AI 助手的头部/底部如果铺了不透明背景，会把圆角盖成直角。让这些行透明、只留外层的圆角背景即可。
- 右上角按钮重影不是自绘按钮画了两遍，而是 `WindowStyle=SingleBorderWindow` 时 DWM 仍绘制一套系统 caption 按钮，和自绘标题栏叠加；要自绘标题栏就保持 `WindowStyle=None`。
- WPF 给卡片加 `DropShadowEffect` 如果直接加在承载内容的 Border 上，会把整棵子树栅格化、文字发虚；要在后面放一个**空白同尺寸 Border** 承载阴影，内容卡片覆盖在上面。
- WPF 动效控制在 120–220ms：窗口淡入、表格切换淡入、抽屉滑入、按钮 1.02 微缩放即可；不要给 DataGrid 每行加 Effect，性能会崩。
- WPF 做系统背景：`WindowStyle=SingleBorderWindow` + `WindowChrome GlassFrameThickness=-1` + `Background=Transparent` + `CompositionTarget.BackgroundColor=Transparent`，**不要**再 `DwmExtendFrameIntoClientArea`，否则容易变成不透明黑/灰；在 SourceInitialized 和 Loaded 都应用一次 DWM 属性。
- Windows 11 的 Mica/Acrylic 采样桌面壁纸，不采样窗口后面的内容；壁纸比较素时看起来就像纯色。用 PrintWindow/截图判断“透明”不可靠，改壁纸也可能被 DWM 缓存。
- 深色系统下，主按钮/徽标要单独用 `OnAccentTextBrush`，否则白字压在亮青色上对比度不够。
- 用 PowerShell 批量改 XAML 极易把 UTF-8 中文写成乱码；批量替换统一用 Python `read_text/write_text(encoding='utf-8')`。

## 2026-10-31（Win11 Mica + WPF）

- WPF 做 Win11 Mica：`WindowStyle=None` + `WindowChrome CaptionHeight=0` + `Background=Transparent`，然后 `DwmSetWindowAttribute(hwnd, 38, DWMSBT_MAINWINDOW=2)` 和 `DwmExtendFrameIntoClientArea(-1)`；圆角用属性 33 = 2。`AllowsTransparency=True` 会让 Mica 失效，不能用。
- `PrintWindow` 抓不到 DWM 背景，透明区域会是黑色，不能拿截图判断 Mica 是否生效；用 `DwmGetWindowAttribute(hwnd, 38)` 验证返回值（2 = Mica）。
- `WindowChrome` 最大化会向外溢出一圈（本机 200% 缩放是 12px），根 Grid 在 `WindowState=Maximized` 时加 6px Margin 即可。
- 透明背景上的 ToolButton 在 `PrintWindow` 里文字会显灰，容易被误判成 Disabled；先查 UIA `IsEnabled`，再看控件模板 Foreground。
- 中文 XAML 绝对不要用 `Get-Content` + `Set-Content` 批量替换（Windows PowerShell 默认 ANSI 会把 UTF-8 中文写成乱码且丢字节）；用 edit/write 工具改。
- 旧列宽会在新布局里把状态列压成一条；用 `LayoutVersion` 做一次性列宽重置，再保存新版本。

## 2026-10-30（模拟器模板扩充）

- 模板平台名要和用户可能改过的字典对齐：只做同名精确匹配，老板把 `Game Boy` 改成 `GB`、`PlayStation` 改成 `PS1` 后就绑不上。加「常见简称别名 + 去空格符号归一化」后，`Xbox 360→XBOX360`、`WonderSwan→WS` 也能命中；匹配顺序仍是精确名优先。
- 有 GitHub 仓库不等于有 Windows Release：RetroArch 的 latest 只有 sourceonly tar.xz，Dolphin/Project64 没有 latest release。模板里加 `websiteUrl` 并留空仓库，比让「检查更新」误报更清楚。
- 同一项目的资产可能是 MSVC/MXE/AVX2/SSE2 多套：Azahar 官方推荐 MXE、Ymir 在不确定 CPU 时优先 SSE2；按发布方建议在评分里加偏好，而不是按资产顺序取第一个。
- 模板数量变多后，纯 ListBox 不好找；加搜索框要防 XAML 初始化时 `TextChanged` 先于其他控件触发（先判空）。

## 2026-10-30（更新选错资产 / 用户数据保留）

- GitHub Release 资产不能按「包含 win64 / windows 的第一个」来选：DuckStation 的 `windows-arm64-release-symbols.7z` 排在前面，会装成一堆 PDB；PCSX2 的 `windows-x64-Qt-symbols.7z` 同理。必须同时判断 Windows + x64 + release，并排除 symbols/pdb、source、arm64/armhf、macOS、Linux/AppImage。
- 没有 Windows 包时不要回退到「第一个资产」：RetroArch 的 latest 只有 `retroarch-sourceonly-1.22.2.tar.xz`，会被当成安装包再报「格式不支持」。返回 null，让界面引导打开官网。
- 滚动发布的 tag 可能是 `latest`（DuckStation），版本号要从 `published_at` 或资产名取；否则会把 `latest` 写进版本号，且每次都「有新版本」。
- 覆盖安装不能把旧目录直接换掉：用户的存档/设置/核心/纹理都在里面。旧目录移到 `.backup` 后，要把新包中没有的文件合并回新目录；新包自带的程序/DLL 保持新版。
- 已经踩坏的安装可以用 `.backup\<模拟器>-<时间>` 恢复：DuckStation 的备份里有完整 x64 程序和 `settings.ini`/存档，PPSSPP 备份里有 `PPSSPPWindows64.exe` 和 `memstick`。

## 2026-10-30（RPCS3 更新源）

- RPCS3 主仓库只有源码 tag（Release assets 为空），Windows 构建在 `RPCS3/rpcs3-binaries-win`；这类仓库的 tag 是 `build-<hash>`，版本号要从资产文件名里取（`rpcs3-v0.0.43-20511-....7z`），不能只信 tag。
- `IsNewer` 不要拿非数字 tag 与数字版本做字典序比较：`build-*` 的首字符会让它永远「更新」。数字版本对非数字版本要显式决定方向，并在写回版本时顺便修正成可比较形式。
- 从资产名提取版本前先去掉 `.zip/.7z/...` 扩展名，否则 `RetroArch-Win64.7z` 会被误解析成 `64.7`。

## 2026-10-30（v0.26.0 P0 打磨）

- 外置盘（E:）没插时 `File.Exists` 对全部 ROM 都是 false；任何「清理失效」都必须先判断盘根是否可用，否则一次误点会把评分、进度、元数据一起删掉。盘根探测做成可注入委托，测试不依赖真实盘符。
- 模拟器模板里的平台名是标准名（Game Boy / PlayStation），用户可能已把字典改成 GB / PS1；模板绑定只做同名精确匹配，匹配不上留给「设置 → 平台」，不要按扩展名猜。
- 一键更新先解压到临时目录，再由 `PrepareTargetWithBackup` 移走旧安装目录；要把备份路径返回给失败回滚用，只在复制成功后才算安装完成。
- WPF `Progress<T>` 在 `await` 返回后仍可能排队回调：取消/完成时用「当前 CTS 是否为空」拦住迟到的进度文本，否则「已取消」会被覆盖。
- DataGrid 列显隐用列 Header 文本作持久化键最简单；必须兜底「全部隐藏」和列改名后的未知键。
- `tools\verify-publish.ps1` 会校验 exe 的 ProductVersion 是否包含当前 HEAD，所以发布顺序必须是「先提交 → 再 publish」。

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

## 2026-10-31 v0.30.0 托盘与浮层踩坑

- **单文件发布不认 DLL**：publish 默认单文件，把 in 里的 MicaRomManager.dll 拷进去不生效；改代码后要么重新 dotnet publish，要么直接跑 in\\Release\\net9.0-windows\\MicaRomManager.exe 验证。
- **PowerShell 改源码文本会坏中文**：Get-Content | Set-Content 在非 UTF-8 控制台下会把个别汉字替换成 ?，写坏 XAML。改源码统一用 edit 工具或 Python 显式 encoding='utf-8', newline=''。
- **托盘叠在助手上必须是不透明底**：助手面板 ShellPanelBrush 只有 40% 白，托盘若用同系透明笔刷会透出对话文字；新增 TrayBrush（#FDF9FAFC / 深色 #FA18181D）。
- **点空白关闭不要用遮罩 Border**：遮罩会盖住筛选条上的「编辑」开关，导致点不动；改为窗口级 PreviewMouseDown，命中托盘与开关时放行。
- **这台机器 2K@200% 缩放**，窗口 1280 DIP 在 3840×2160 上仍能放下；截图必须先把 PowerShell 进程设为 Per-Monitor DPI 感知，否则 GetWindowRect 返回被虚拟化的坐标，只能拍到窗口左上角。

## 2026-10-31 v0.30.1 设置窗口与平台预置

- **只读计算属性不能 TwoWay 绑定**：`Emulator.ResolvedExeDisplay` 是只读，TextBox 默认 TwoWay 会在 `ShowDialog` 时抛 `InvalidOperationException`，窗口直接打不开；只读展示一律显式 `Mode=OneWay`。
- **UIA 自动化点按钮**：按 `Name` 找到的元素可能不是按钮（会是窗口标题或文本），要用 `AndCondition(Name, ControlType.Button)` 再取 `InvokePattern`；否则 `GetCurrentPattern` 抛「不支持的模式」，看起来像点了没反应。
- **资源文件有 data 副本**：`PlatformPresets.json` / `EmulatorTemplates.json` 首次运行会复制到 `data\`，改完资源要删掉 data 副本才会生效。
- **平台名用短名**：库里既有 `PS1/PS2/SNES/3DS/Wii U/街机` 这类写法，预置清单要按这些名字写（或加别名），否则分组会落到「自定义」。

## 2026-10-31 v0.30.2 常驻与同步

- **助手开关的语义**：`ui.assistant_open_v2` 既是最新状态也是启动默认值，设置里的文案要写「启动时展开」，不要写「默认常驻」，否则用户会疑惑为什么关掉后下次又是别的状态。
- **emoji 在 WPF 里不可靠**：⭐ 走字体回退常被渲染成单色轮廓，改成 `Path` 矢量五角星最稳（Fill 固定金色，Visibility 绑 Favorite）。
- **字典库自动同步要幂等**：`LookupSync` 先 `List(kind)` 比对（忽略大小写）再 `Add`，否则每次保存都会重复插入或触发唯一约束。

## 2026-10-31 v0.30.3 托盘动画与可见性

- **白色托盘上别用半透明输入框**：`InputBrush` 是 40% 白，配无边框样式在白底托盘里几乎看不到；托盘内的输入控件要显式白底 + 发丝线边框。
- **托盘开合动画**：`TranslateTransform` 从 24 滑到 0 配 180ms 淡入；关闭用 `Completed` 回调再 `Collapsed`，避免动画被直接打断成闪烁。
- **Ctrl+S**：在窗口级 `PreviewKeyDown` 里判断托盘可见且处于编辑态，调用 `Inspector.SaveCurrent()`，不要另开保存通道。

## 2026-10-31 v0.30.6 刮削 diff

- **diff 要在写入时记录**：`SetText` 只有真正改动才返回 1，顺手把 (字段, 旧值, 新值) 记进 `_lastDiff`，比事后比对控件状态稳。
- **别在生产库里跑真假削做验证**：会真扣 API 费用，UI 层用编译 + 既有表单同源保证，把「未实机验证」如实写进 CHANGELOG。

## 2026-10-31 v0.30.8 失焦变灰

- **失焦变灰是 DWM 的降级**：Win11 对 `DWMWA_SYSTEMBACKDROP_TYPE` 的 Mica/Acrylic 有「非激活实色」状态，没有任何 API 能直接关掉；可行做法是在 `WM_ACTIVATE`/`WM_NCACTIVATE` 后重新 `DwmSetWindowAttribute` 下发一次，实测聚焦与失焦的取样像素完全一致。
- **SWCA 亚克力在 Win11 上不吃 GradientColor 的 alpha**：0x99 和 0x40 两种透明度截出来一模一样（都偏白 242），还会盖掉原来的通透感；所以默认仍走 DWM，只把它留作开关。
- **截图验证失焦**：用 `SetWindowPos(HWND_TOPMOST)` 固定窗口位置，再点桌面空白处（避开任务栏，任务栏点击会把窗口最小化导致截到别的窗口）。

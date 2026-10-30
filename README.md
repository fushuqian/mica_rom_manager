# Mica ROM Manager

[![CI](https://github.com/fushuqian/mica_rom_manager/actions/workflows/ci.yml/badge.svg)](https://github.com/fushuqian/mica_rom_manager/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/fushuqian/mica_rom_manager)](https://github.com/fushuqian/mica_rom_manager/releases)

Windows 桌面端的模拟器 ROM 集中管理工具：把散落在各处的 ROM、模拟器和启动参数收进一个库，顺手能把游戏拷到掌机或打包分享出去。WPF / .NET 9 / SQLite，绿色便携，不写注册表。

## 能做什么

- **ROM 库**：导入（链接 / 复制 / 移动三种方式）、按平台归档、按「路径 + 内容指纹」双重去重、批量编辑、按标题/区域/语言/特注重命名文件、清理失效条目；所在盘未连接时提示并阻止误清理。
- **浏览与筛选**：EXCEL 风格表格，平台侧栏 + 区域/语言/开发商/状态/标签/评分细筛，列宽、排序、筛选条件、列显隐自动记忆。
- **模拟器管理**：内置 21 种模拟器模板（RetroArch、Dolphin、PCSX2、PPSSPP、DuckStation、RPCS3、Cemu、Xenia、Xemu、Ymir、Flycast、Azahar、melonDS、DeSmuME、mGBA、Mesen、FCEUX、Snes9x、BizHawk、ares、Project64），按模板创建、从 zip / 7z / rar / tar.gz 安装、自动识别主程序、配置启动参数、RetroArch 平台级核心、GitHub 一键检查更新/下载安装/版本回写（可取消、失败回滚、保留用户数据）。
- **一键启动**：双击即玩，自动记录游玩次数与时长，多碟游戏可合并/拆分/选碟。
- **发送到掌机**：按设备配置把 ROM 复制到 SD 卡（内置 TrimUI Smart Pro 目录规则），支持同名跳过/覆盖、字节级进度、任务队列。
- **分享打包**：ROM + 模拟器 + `Play-*.bat` 启动脚本打成文件夹，可选 zip。
- **AI 助手**：右侧聊天面板，OpenAI 兼容接口 + function calling，能查库、筛选、改元数据、刮削、推荐、启动、发送、识别设备结构。写操作先出确认卡片，删除类二次确认。

## 下载与运行

1. 到 [Releases](https://github.com/fushuqian/mica_rom_manager/releases) 下载最新的 `MicaRomManager-<版本>-win-x64.zip`。
2. 解压到任意目录，双击 `MicaRomManager.exe`。
3. 单文件自包含发布，**不需要**单独安装 .NET 运行时。

解压后 `Resources\` 不要删（内置模拟器模板与设备配置在里面）。数据保存在程序目录的 `data\` 下，整个文件夹拷走即可换机器。

## 快速上手

1. **设库目录**：设置 → 通用 → 库目录（便携模式下会存成相对路径）。
2. **配模拟器**：设置 → 模拟器管理 → 导入，选压缩包或已有安装目录；平台默认模拟器在「平台」页绑定。
3. **导入游戏**：工具栏「导入」，支持拖放文件和整个目录。
4. **玩**：双击行启动；`F2` 编辑，`Delete` 移除记录。
5. **发送**：工具栏「连接到设备」→ 选盘符 → 选中游戏右键「发送到设备」。

## 数据与目录

- 便携模式：`<程序目录>\data\`，包含 `mica.db`、`covers\`、`emulators\`、`downloads\`、`exports\`、`logs\`、`devices\`。
- 程序目录不可写时自动回退 `%LOCALAPPDATA%\MicaRomManager\`。
- 设置 → 通用里可备份/恢复数据库；恢复前会自动把当前库备份到 `data\backups\`，校验失败自动回滚。
- 删除 ROM 走回收站；「从库中移除」只删记录不动文件。

## AI 配置

设置 → AI，填 OpenAI 兼容的 BaseUrl / API Key / 模型（如 `https://api.deepseek.com/v1` + `deepseek-chat`）。

- API Key 只存本机 SQLite，落盘前用 Windows DPAPI 按当前用户加密；日志只记主机、模型与状态码。
- 联网搜索走 DeepSeek 官方 Search API，默认关闭，复用同一个 Key，按成功请求计费。
- 6 个提示词可在「设置 → 提示词」里整段改写，用 `{{变量}}` 占位。

## 从源码构建

需要 .NET 9 SDK。

```powershell
dotnet build MicaRomManager.sln -c Release
dotnet test  MicaRomManager.sln -c Release
```

单文件便携发布（会顺带校验资源与版本）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\publish.ps1
```

打包并创建 GitHub Release（需要 `gh` 已登录）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 0.25.2
```

## 项目结构

```
src/MicaRomManager/
  Data/          SQLite 仓储与 11 版 schema 迁移
  Models/        领域模型
  Services/      导入、整理、模拟器、发送、分享、AI（Services/Ai）
  ViewModels/    CommunityToolkit.Mvvm
  Views/         WPF 窗口
tests/MicaRomManager.Tests/   xUnit 单测
tools/           发布与校验脚本
```

## 已知限制

- 仅 Windows。
- MTP（安卓掌机）发送尚未实现，目前只支持盘符设备。
- 在线刮削数据源（LaunchBox / TheGamesDB / ScreenScraper）还没接，当前是文件名解析 + AI 补全。

## 许可证

[MIT](LICENSE)

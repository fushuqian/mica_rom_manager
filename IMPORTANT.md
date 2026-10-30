# 重要配置（IMPORTANT）

## 路径

- 项目目录：`D:\Dev\mica_rom_manager`（旧路径 `d:\Trae\MICA_ROM_MANAGER` 已废弃）
- 解决方案：`MicaRomManager.sln`
- 主程序：`src\MicaRomManager\MicaRomManager.csproj`（WPF，net9.0-windows）
- 运行期数据（便携）：`<程序目录>\data\`，含 `mica.db`、`covers\`、`emulators\`、`downloads\`（模拟器更新包临时目录）、`exports\`、`logs\`
- 回退数据目录：`%LOCALAPPDATA%\MicaRomManager\`
- 字体：`C:\Windows\Fonts\HarmonyOS_Sans_SC_Regular.ttf` / `_Bold.ttf`（系统已安装，直接引用）

## 凭据

- GitHub 推送走 Git Credential Manager，凭据存于 Windows 凭据管理器（target=`git:https://github.com`，user=`fushuqian`）。
- 2026-10-30：旧 `ghp_` 令牌已失效（API 401），已改用 `gh auth login`；现有令牌前缀 `gho_`，scopes 含 `repo`、`workflow`、`read:org`、`gist`。
- 推送命令（本机需走代理）：
  `git -c credential.https://github.com.helper=manager -c http.proxy=http://127.0.0.1:7890 push origin main`
- 查状态：`gh auth status`；调 API：`gh api user`（无需再手工 `git credential fill`）。
- 命令 `cmdkey /list` 可列出所有凭据条目。

## GitHub 仓库

- 远程：`https://github.com/fushuqian/mica_rom_manager`（Public）
- remote：`origin` → 同上；主分支 `main`，已设置 upstream。

## 发布

- 构建/发布前需 .NET 9 SDK：`winget install Microsoft.DotNet.SDK.9`（本机 9.0.320）。
- 发布命令：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\publish.ps1`（内部执行 `dotnet publish src\MicaRomManager -c Release -r win-x64 --self-contained true -o publish`，随后自动跑 `tools\verify-publish.ps1`）。
- 本机 PowerShell 执行策略为 Restricted，脚本必须带 `-ExecutionPolicy Bypass` 才能运行。
- 校验内容：`publish\Resources\` 下 `EmulatorTemplates.json` 与 `DeviceProfiles.json` 必须都在；exe `FileVersion` 等于 csproj `<Version>`；`ProductVersion` 含当前 git HEAD。任一不满足直接失败。
- 产物：`publish\` 打包为 `MicaRomManager-<版本>-win-x64.zip`；release note 取自 CHANGELOG。
- Release 页：`https://github.com/fushuqian/mica_rom_manager/releases`

## 外部服务

- GitHub Releases API：`https://api.github.com/repos/{owner}/{repo}/releases/latest`，免费，未认证限流 60 次/小时，仅在用户点击时调用。
- AI：OpenAI 兼容 `/chat/completions`，用户自行填写 BaseUrl / API Key / 模型（设置 → AI）。Key 只存本地 `settings` 表（`ai.api_key`），不写入仓库、文档或日志；日志只记主机、模型与状态码。
- AI 助手聊天记录：`data\ai-chat.json`（只含 user/assistant 文本，最多 100 条），清空聊天时删除。
- DeepSeek 联网搜索：`POST https://api.deepseek.com/v1/tools/search`，复用 `ai.api_key`；仅当 BaseUrl 为 `api.deepseek.com`。按成功请求计费（约 $0.75/1000 次），设置键 `ai.search.enabled`、`ai.search.max_results`。

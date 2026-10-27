# 重要配置（IMPORTANT）

## 路径

- 项目目录：`d:\Syncthing\Trae\mica_rom_manager_lite`
- lite（Rust + Tauri 2）工程：`app\`（前端 `app\web`，后端 `app\src-tauri`）
- 解决方案：`MicaRomManager.sln`
- 主程序（WPF）：`src\MicaRomManager\MicaRomManager.csproj`（net9.0-windows）
- 运行期数据（便携）：`<程序目录>\data\`，含 `mica.db`、`covers\`、`emulators\`、`downloads\`、`exports\`、`logs\`
- 回退数据目录：`%LOCALAPPDATA%\MicaRomManager\`
- 字体：`C:\Windows\Fonts\HarmonyOS_Sans_SC_Regular.ttf` / `_Bold.ttf`（系统已安装，直接引用）

## lite 构建

- 开发：`cd app\web; npm run tauri dev`（Vite 5173 + Rust 后端）
- 前端构建：`cd app\web; npm run build`
- 质量门：`cd app\src-tauri; cargo fmt --check; cargo clippy --all-targets -- -D warnings; cargo test --lib`
- 发布：`cd app; npm run tauri build`，产物在 `app\src-tauri\target\release\`
- 交付目录：`publish-lite\`（exe + `Resources\` + `data\`）
- 测试旧库兼容：设置环境变量 `MICA_COMPAT_DB=<mica.db 路径>` 后运行 `cargo test --lib real_db_is_compatible`
- 关键词：Rust 工具链为 msvc 1.99；7z 用 `sevenz-rust`，rar 依赖外部 7-Zip/WinRAR。

## 凭据

- GitHub 推送走 Git Credential Manager，凭据存于 Windows 凭据管理器（target=`git:https://github.com`，user=`fushuqian`）。
- `gh auth status` 返回未登录；API 调用需先 `git credential fill` 读 token（不落盘时 `Set-Content -Encoding UTF8 mica_cred.txt` 后再读）。
- 命令 `cmdkey /list` 可列出所有凭据条目。

## GitHub 仓库

- 远程：`https://github.com/fushuqian/mica_rom_manager`（Public）
- remote：`origin` → 同上；主分支 `main`，已设置 upstream。

## 发布

- 构建/发布前需 .NET 9 SDK：`winget install Microsoft.DotNet.SDK.9`（本机 9.0.320）。
- 发布命令：`dotnet publish src\MicaRomManager -c Release -r win-x64 --self-contained true -o publish`
- 产物：`publish\` 打包为 `MicaRomManager-<版本>-win-x64.zip`；release note 取自 CHANGELOG。
- Release 页：`https://github.com/fushuqian/mica_rom_manager/releases`

## 外部服务

- GitHub Releases API：`https://api.github.com/repos/{owner}/{repo}/releases/latest`，免费，未认证限流 60 次/小时，仅在用户点击时调用。

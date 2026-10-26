# 重要配置（IMPORTANT）

## 路径

- 项目目录：`d:\Trae\MICA_ROM_MANAGER`
- 解决方案：`MicaRomManager.sln`
- 主程序：`src\MicaRomManager\MicaRomManager.csproj`（WPF，net9.0-windows）
- 运行期数据（便携）：`<程序目录>\data\`，含 `mica.db`、`covers\`、`emulators\`、`exports\`、`logs\`
- 回退数据目录：`%LOCALAPPDATA%\MicaRomManager\`
- 字体：`C:\Windows\Fonts\HarmonyOS_Sans_SC_Regular.ttf` / `_Bold.ttf`（系统已安装，直接引用）

## 密钥 / Token

- 暂无。GitHub Release 检查使用公共 API，无需 Token。
- GitHub 登录：`gh` CLI 已登录账号 `fushuqian`（凭据存于系统 keyring，不落盘）。

## GitHub 仓库

- 远程：`https://github.com/fushuqian/mica_rom_manager`（Public）
- remote：`origin` → 同上；主分支 `main`，已设置 upstream。

## 外部服务

- GitHub Releases API：`https://api.github.com/repos/{owner}/{repo}/releases/latest`，免费，未认证限流 60 次/小时，仅在用户点击时调用。

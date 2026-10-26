# Mica ROM Manager 后续开发计划

## 摘要

接手 v0.2.0 项目。计划分三段：**v0.2.1 收尾发布**（验收下载链路、补图标、打包发 GitHub Release）→ **v0.3.0 批量操作与体验** → **v0.4.0 封面/元数据自动刮削**（老板确认要做但靠后）。每段结束同步 GitHub 并更新 QUEST/CHANGELOG。

## 现状分析

* 技术栈：WPF / .NET 9 / SQLite（Microsoft.Data.Sqlite）/ CommunityToolkit.Mvvm / SharpCompress，4 个测试文件 42 用例。

* 已完成（v0.2.0，git 干净、已推送 main）：

  * 游戏库 CRUD、筛选搜索、拖拽导入、导入预览、编辑窗口、Excel 风格列表。

  * 模拟器管理：模板、安装（zip/文件夹）、配置、默认绑定、GitHub Release 检查、应用内下载安装（zip/7z + 代理）、测试启动。

  * 分享打包：ROM + 模拟器 + `Play-*.bat`，可选 zip。

  * 设置：代理三态、库目录、DB 备份/恢复、CSV 导出。

* 遗留：

  * 模拟器"下载 → 7z 解压 → 安装"链路未端到端实测（QUEST.md Q-002 注明待手动验收）。

  * 无应用图标（[MicaRomManager.csproj](d:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/MicaRomManager.csproj) 中 `ApplicationIcon` 为空）。

  * 无发布打包流程，GitHub 上还没有 Release。

## v0.2.1 收尾发布（第一优先）

### 1. 手动验收模拟器下载链路（Q-002 关单）

* 步骤：设置里配代理（本机 127.0.0.1:7890 可用）→ 模拟器管理选模板（如 RetroArch）→ 检查更新 → 应用内下载（zip 资产优先）→ 安装 → 测试启动。

* 验收通过后在 QUEST.md 将 Q-002 置为"已验收"；发现问题则修复后重验。

* 说明：此步需要老板配合点弹窗（MEMORIES.md 记录过 MessageBox 自动化不可靠）。

### 2. 应用图标（建议项，不阻塞发布）

* 用生图工具生成一个简约 ROM/手柄主题方图 → 转 .ico（多尺寸 16/32/48/256）。

* 写入 csproj `ApplicationIcon`，窗口 `Icon` 一并生效。

### 3. 发布打包

* 命令：`dotnet publish src\MicaRomManager -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish\`。

* 决策：self-contained（目标机器无需装 .NET 9，符合"拷到别的电脑就跑"的定位），不单文件（WPF 单文件 + native 兼容性麻烦），zip 后约 100\~150MB 可接受。

* 产物：`MicaRomManager-0.2.0-win-x64.zip`。

* 发布：`git tag v0.2.0` → `gh release create v0.2.0` 附 zip + 从 CHANGELOG 生成的 release note。

### 4. 文档同步

* CHANGELOG.md 记 v0.2.1 收尾内容；QUEST.md Q-002 状态更新；IMPORTANT.md 补 publish 命令与 Release 地址；按规则 5 推送 GitHub。

## v0.3.0 批量操作与体验

* 批量操作（基础已有 `SelectedGames`）：多选 → 批量改状态/加标签/绑模拟器/收藏、批量删除（回收站）。

* 列表设置持久化：列显隐、列宽、排序、筛选条件记忆（存 SQLite settings 表）。

* 快捷操作：回车启动选中游戏、右键"打开 ROM 所在文件夹"、Ctrl+双击打开游戏目录。

* 统计轻量化：状态栏增加各状态计数；不做独立统计面板（避免过度设计）。

* 验证：新增批量操作单测；build 0 警告；实机截图核对。

## v0.4.0 封面/元数据自动刮削（靠后）

* 数据源优先级：LaunchBox 官方数据包（免费、离线、无限制）> ScreenScraper API（免费但需注册账号）。

* 匹配逻辑：文件名去 region/版本/格式标记 → 与数据源标题比对 → 回填标题、平台、年份、简介；封面下载到 `data\covers\`，已有封面不覆盖。

* 复用 `NetworkClientProvider` 代理设置；刮削走批量队列，带进度与跳过。

* 保持手动编辑优先：刮削结果先入预览，确认才写库。

## 远期（占位，不承诺时间）

* 应用自更新：检查 GitHub Release 提示新版本 + 下载替换。

* 按平台自动推荐模拟器排序（已有 Platforms/DefaultPlatforms 数据基础）。

## 假设与决策

| 决策   | 内容                             | 理由                           |
| ---- | ------------------------------ | ---------------------------- |
| 发布形式 | self-contained win-x64 zip 便携包 | 目标机器零依赖，符合工具定位               |
| 图标   | 生成免费 ico，不阻塞发版                 | 无外部成本                        |
| 刮削时点 | v0.4.0，排批量操作之后                 | 老板确认"要，但靠后"                  |
| 验收方式 | 下载链路手动验收                       | WPF 弹窗自动化不稳定（MEMORIES.md 经验） |

## 验证方式

* 每版：`dotnet build` 0 警告 0 错误 + `dotnet test` 全通过 + 实机冒烟。

* v0.2.1：验收清单跑通；GitHub Release 页面可下载且 zip 解压可运行（`MicaRomManager.exe` 启动、数据目录自动创建）。

* 规则同步：QUEST / CHANGELOG / MEMORIES / IMPORTANT 按规则 5/6/8/10 更新。

## 执行顺序

1. v0.2.1：图标 → 收尾文档 → publish 打包 → tag + Release → 推送。
2. 等老板手动验收下载链路（我提供操作清单），通过后关 Q-002。
3. v0.3.0 按上文逐项实现，每项完成即提交推送。
4. v0.4.0 刮削功能另行细化设计后再动工。


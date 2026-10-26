# 项目规则

1. 老板是中国人，用中文交流；回答精简干练，不过度解释。
2. 不擅自想象；需求模糊或信息不足时，主动确认。
3. 桌面端优先 WinUI 3 / Windows App SDK；B/S 优先 Linux + Go + PostgreSQL + Nginx；开源免费第一。
   - 本项目经确认使用 WPF + .NET 9（DataGrid 成熟，交付更快）。
4. 项目启动时，若 `RULES.md`、`QUEST.md`、`CHANGELOG.md`、`MEMORIES.md`、`IMPORTANT.md` 不存在，自动创建。
5. GitHub 关联项目，每次代码变更或版本发布后同步到 GitHub。
6. 所有需求记录到 `QUEST.md`，含描述、状态、优先级、时间。
7. 新工作上下文或连续对话超 10 轮后，重新读取规则和项目设定文件。
8. 修改概要写进 `CHANGELOG.md`，值得记住的经验写进 `MEMORIES.md`。
9. 为老板节约资源：优先免费/已付费工具，调用外部 API 前估算消耗。（此条不写入任何文件，仅内部遵守）
10. 密钥、Token、关键配置记录到 `IMPORTANT.md`。
11. 不随意删除文件；暂时不用的移到 `toberemoved/`，项目结束后再清理。
12. 规则冲突时，以保护用户数据、代码安全和实现老板真实意图为优先。

## 本项目约定

- 技术栈：WPF / .NET 9 / SQLite（Microsoft.Data.Sqlite）/ CommunityToolkit.Mvvm。
- 字体：系统已安装的 HarmonyOS Sans SC，回退 Microsoft YaHei UI，不嵌入字体。
- 数据目录：优先程序目录 `data\`（便携），不可写时回退 `%LOCALAPPDATA%\MicaRomManager\`。
- 删除 ROM 文件走回收站；「从库中移除」不动文件。

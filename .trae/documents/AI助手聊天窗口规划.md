# AI 助手聊天窗口规划

## 一、目标

把现有「分散的 AI 弹窗」升级为**常驻右侧的聊天助手**：

- 主界面工具栏提供「AI 助手」开关；打开后右侧展开聊天窗口，可拖拽调宽、可关闭。
- 用户用自然语言提出需求，助手通过 **OpenAI 工具调用（function calling）** 直接接入应用能力：
  - 筛选/查询游戏、读取游戏详情与统计；
  - 修改游戏元数据、评分、状态、标签；
  - 游戏咨询（好不好玩、介绍游戏、这是啥游戏）；
  - 学习新设备存储结构并生成设备配置；
  - 修改白名单设置；
  - 启动游戏、发送到设备、从库移除/移到回收站、打开应用窗口等全部功能。
- 所有**写操作先出确认卡片**；删除类操作二次确认。
- 聊天记录保存到本地 `data\ai-chat.json`，重启可恢复。

版本：**v0.22.0**。

## 二、现状分析

- 主界面 [MainWindow.xaml](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/MainWindow.xaml) 第一行内容区只有两列：左侧平台栏 210、右侧游戏列表 `*`，可直接扩成「平台栏 / 列表 / 分隔条 / 助手栏」四列。
- 命令集中在 [MainViewModel.cs](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/ViewModels/MainViewModel.cs)，已有 `ApplyAiFilter`、`ExecuteAiAction`、启动/发送/删除/打开窗口等能力，适合作为助手的宿主。
- AI 基础层 [AiChatClient.cs](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Services/Ai/AiChatClient.cs) 目前只支持普通 `chat/completions`，需要扩展 `tools` / `tool_calls`。
- 可复用：`AiQueryFilter`/`AiQueryMatcher`（[AiQueryPlan.cs](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Models/AiQueryPlan.cs)）、`AiJson`、`DriveTreeScanner`、`AiDeviceProfiler`、`RecommendationService`、`FileRecycleBin`、`LibraryOrganizer`、现有各 AI 预览窗口。
- 本期**不新增数据库表**；聊天记录用本地 JSON。

## 三、总体架构

```
AiAssistantView (右侧聊天 UI)
        │
AiAssistantViewModel (消息、输入、确认卡片、停止、清空)
        │
AiAssistantService (多轮工具调用循环，最多 5 轮)
        │            │
AiChatClient     AiToolExecutor ──> IAiAppHost (由 MainViewModel 实现)
(tools API)          │                    │
                 AiToolRegistry       现有命令/仓储/发送队列/设备扫描
```

- **只读工具**执行后把结果回给模型继续回答。
- **写工具**不直接执行，返回 `AiPendingAction`，聊天窗口显示确认卡片；用户确认后才执行，再把执行结果交给模型继续回复。
- 删除类 `PendingAction` 标记 `IsDestructive`，确认时再弹一次 Windows 确认框。

## 四、详细改动

### 4.1 扩展 AiChatClient 支持工具调用

修改 [AiChatClient.cs](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/Services/Ai/AiChatClient.cs)：

新增类型 `Services/Ai/AiChatModels.cs`：

```csharp
public sealed class AiChatMessage
{
    public string Role { get; init; } = "user";      // system/user/assistant/tool
    public string? Content { get; init; }
    public string? ToolCallId { get; init; }
    public List<AiToolCall>? ToolCalls { get; init; }
}

public sealed class AiToolCall
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ArgumentsJson { get; init; } = "{}";
}

public sealed class AiToolDefinition
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public object Parameters { get; init; } = new { type = "object", properties = new { } };
}

public sealed record AiToolChatResult(
    bool Success,
    string Content,
    string Error,
    List<AiToolCall> ToolCalls,
    int StatusCode,
    TimeSpan Elapsed);
```

新增方法：

```csharp
public Task<AiToolChatResult> CompleteWithToolsAsync(
    IReadOnlyList<AiChatMessage> messages,
    IReadOnlyList<AiToolDefinition> tools,
    CancellationToken token);
```

- 请求体增加 `tools: [{type:"function", function:{name, description, parameters}}]`、`tool_choice:"auto"`、`temperature:0.2`、`max_tokens:1024`。
- 解析 `choices[0].message.content` 与 `message.tool_calls[].{id, function.name, function.arguments}`。
- `finish_reason == "tool_calls"` 时允许 content 为空。
- 400 错误补充提示：「当前模型可能不支持工具调用，请改用支持 function calling 的模型（如 deepseek-chat / gpt-4o-mini）」。
- 现有 `CompleteAsync` 保持不变，供刮削/标签/翻译/设备推断复用。

### 4.2 工具定义与执行

新增 `Services/Ai/AiToolRegistry.cs`：

- `public static IReadOnlyList<AiToolDefinition> All { get; }`
- 工具清单：

| 工具 | 类型 | 作用 |
| --- | --- | --- |
| `query_games` | 只读 | 按平台/状态/区域/语言/类型/开发商/收藏/年份/关键词筛选，返回总数 + 前 20 条精简信息 |
| `get_game` | 只读 | 按 id 或当前选中游戏返回完整元数据、碟片、文件是否存在 |
| `get_statistics` | 只读 | 库内总数、平台分布、状态分布、收藏数、总游玩时长 |
| `list_emulators` | 只读 | 模拟器名称、平台、启用状态、版本 |
| `recommend_games` | 只读 | 本地相似推荐，返回前 N 条与理由 |
| `get_settings` | 只读 | 读取白名单设置项（不含 API Key） |
| `list_drives` | 只读 | 枚举就绪盘符、卷标、可用空间 |
| `scan_device_tree` | 只读 | 只读扫描盘内目录，返回目录名与扩展名统计 |
| `update_games` | 写 | 按 game_ids 批量修改元数据/评分/状态/标签/备注 |
| `delete_games` | 写（危险） | `remove_record` 只删记录；`recycle_files` 移到回收站并删记录 |
| `launch_game` | 写 | 启动指定游戏/碟片 |
| `send_games` | 写 | 把游戏加入发送队列（需已连接设备） |
| `set_settings` | 写 | 修改白名单设置项 |
| `save_device_profile` | 写 | 校验并保存 AI 推断的设备配置 |
| `open_window` | 立即 | 打开设置、导入、分享、任务列表、连接设备、AI 刮削/标签/翻译/推荐 |

- 不提供 `get_api_key`；`get_settings`/`set_settings` 白名单：
  `network.proxy.mode`、`network.proxy.address`、`library.import_mode`、
  `ai.base_url`、`ai.model`、`ai.timeout_seconds`、`ai.batch_limit`。
- 禁止通过工具修改 `ai.api_key`、`library.root`、`portable.enabled`。

新增 `Models/GamePatch.cs`：

```csharp
public sealed class GamePatch
{
    public string? Title, Genre, Region, Language, SpecialNote, Publisher, Developer, Notes, Tags, Status;
    public int? ReleaseYear, Rating;
    public bool? Favorite;
    public bool HasAny => ...;
}
```

新增 `Services/Ai/AiToolExecutor.cs`：

- `Task<AiToolExecution> ExecuteAsync(AiToolCall call, IAiAppHost host, CancellationToken token)`。
- `AiToolExecution` 包含：
  - `ToolCallId`
  - `ResultForModel`（JSON 字符串，回传给模型）
  - `AiPendingAction? Pending`
- 参数用 `JsonDocument` 读取，缺字段给默认值；数值/枚举越界返回错误 JSON，不抛异常。
- `update_games`：先按 id 找到游戏，生成字段 diff 摘要，返回 Pending；执行时调用 `host.UpdateGames`。
- `delete_games`：Pending 明确写「将删除 N 条记录，其中 M 个文件移到回收站」，`IsDestructive=true`。
- `set_settings`：逐项校验白名单与格式，生成 diff，Pending 执行时调用 `host.SetSetting`。
- `save_device_profile`：复用 `AiDeviceProfiler.Validate` 校验后再生成 Pending；保存走 `DeviceProfileStore.Save`。
- `send_games`：未连接设备时返回错误 JSON，提示助手让用户先说「连接设备」并打开连接窗口。
- `open_window`：立即调用 host 打开对应窗口/对话框。

### 4.3 助手服务与对话循环

新增 `Services/Ai/AiAssistantService.cs`：

```csharp
public sealed record AiTurnResult(string Text, AiPendingAction? Pending, string Error);

public sealed class AiAssistantService
{
    public Task<AiTurnResult> SendAsync(string userText, CancellationToken token);
    public Task<AiTurnResult> CompletePendingAsync(string executionResult, CancellationToken token);
    public void CancelPending();
    public void Reset();
}
```

流程：

1. 维护 `List<AiChatMessage>` 对话历史，最多保留最近 20 条（不含 system）。
2. 每次发送追加 user 消息，进入循环（最多 5 轮）：
   - 调用 `CompleteWithToolsAsync(history, AiToolRegistry.All)`。
   - 无 tool_calls：返回最终文本。
   - 有 tool_calls：依次执行；只读工具结果以 `role:"tool"` + `tool_call_id` 追加，继续下一轮。
   - 任一工具返回 Pending：记录 pending 对应的 tool_call_id，结束本轮，返回 `AiPendingAction`。
3. `CompletePendingAsync`：把执行结果作为 tool 消息追加，再次进入循环，让模型总结或继续调用工具。
4. 超过 5 轮返回「步骤过多，请拆成更具体的问题」。
5. System prompt 写明：
   - 中文回答；应用内一切写操作必须先调用写工具生成确认，不得声称已执行。
   - 游戏咨询可基于自身知识与 `get_game` 返回的元数据回答；不确定时明确说明。
   - 不编造年份/发行商/开发商；需要事实时先查询库内数据。
   - 删除类操作必须先解释后果。
   - 不读取、不输出 API Key。

### 4.4 宿主接口 IAiAppHost

新增 `Services/Ai/IAiAppHost.cs`，由 [MainViewModel.cs](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/ViewModels/MainViewModel.cs) 实现：

```csharp
public interface IAiAppHost
{
    IReadOnlyList<Game> AllGames { get; }
    IReadOnlyList<Game> FilteredGames { get; }
    Game? SelectedGame { get; }
    IReadOnlyList<Game> SelectedGames { get; }
    string ConnectedDevice { get; }

    void ApplyLibraryFilter(AiQueryFilter filter);
    void RefreshLibrary();

    int UpdateGames(IReadOnlyList<long> ids, GamePatch patch);
    int DeleteGames(IReadOnlyList<long> ids, bool recycleFiles);
    bool LaunchGame(long id, int discIndex, out string message);
    string SendGames(IReadOnlyList<long> ids);

    string GetSetting(string key);
    void SetSetting(string key, string value);
    void OpenWindow(string window);
}
```

MainViewModel 实现要点：

- `AllGames` = `Games`；`FilteredGames` = `GamesView.Cast<Game>()`。
- `ApplyLibraryFilter` 复用现有 `ApplyAiFilter` 的映射逻辑，抽出 `ApplyQueryFilter(AiQueryFilter)`。
- `UpdateGames`：应用 `GamePatch` → `GameRepo.Update` → `RefreshLookups` + `ApplyFilter`。
- `DeleteGames`：`remove_record` 只删库；`recycle_files` 先 `FileRecycleBin.MoveToRecycleBin` 再删库；完成后刷新。
- `LaunchGame`：复用 `EmulatorResolver` + `AppServices.Launcher`；碟号越界返回错误。
- `SendGames`：复用 `DeviceConnection` + `AppServices.SendQueue`；未连接返回中文错误。
- `OpenWindow`：调用现有命令或窗口（设置、导入、分享、任务列表、连接设备、AI 刮削/标签/翻译/推荐）。
- 所有 UI 操作确保在 Dispatcher 线程执行（服务循环在主线程 `async` 上下文，通常无需额外封送；跨线程调用时用 `Application.Current.Dispatcher.InvokeAsync`）。

### 4.5 聊天持久化

新增 `Services/Ai/AiChatStore.cs`：

- 路径：`PathService.AiChatPath`（新增属性，`data\ai-chat.json`）。
- `Load()` / `Save(IEnumerable<AiChatRecord>)` / `Clear()`。
- 记录字段：`role`、`text`、`time`；只存 user/assistant，不存工具原始 JSON 与 PendingAction。
- 最多 100 条；写失败不阻塞聊天。
- 清空聊天时删除文件。

### 4.6 聊天 UI

新增 `Views/AiAssistantView.xaml` + `.xaml.cs`，`ViewModels/AiAssistantViewModel.cs`。

界面结构：

- 顶部：标题「AI 助手」、模型名、清空按钮、收起按钮。
- 消息区：
  - 用户消息右对齐、强调色气泡；
  - 助手消息左对齐、白色气泡；
  - 工具动作灰色小字（「已筛选 23 款」「正在扫描 E 盘…」）；
  - 错误红色文本；
  - 确认卡片：标题、影响摘要、`确认执行` / `取消`；删除类卡片红色边框并显示「危险操作」。
- 空状态：3–4 个示例按钮（「筛选未通关的 PS1 游戏」「介绍一下选中的游戏」「扫描 E 盘并生成设备配置」「把选中的 SNES 游戏加标签 待补」）。
- 输入区：多行输入框 + 发送/停止按钮；Enter 发送、Shift+Enter 换行。
- 忙碌时显示进度文本与停止按钮。

修改 [MainWindow.xaml](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/MainWindow.xaml)：

- 内容区 Grid 列改为：`210 / * / 6 / 0`。
- 新增 `GridSplitter`（第 3 列）与 `AiAssistantView`（第 4 列）。
- 工具栏新增「AI 助手」开关按钮（图标 + 文字）。
- 关闭助手时列宽置 0、分隔条隐藏；打开时恢复 370 并可拖拽。
- 记住开关状态：`ui.assistant_open`（`settings` 表）。

修改 [MainWindow.xaml.cs](file:///D:/Trae/MICA_ROM_MANAGER/src/MicaRomManager/MainWindow.xaml.cs)：

- 创建 `AiAssistantViewModel` 并绑定到 `AiAssistantView`。
- 处理开关、关闭事件、恢复上次宽度状态。
- 监听消息集合变化自动滚动到底部。

### 4.7 聊天消息模型

新增 `Models/AiChatMessage.cs`：

```csharp
public enum AiChatKind { User, Assistant, Tool, Error, Pending }

public partial class AiChatMessage : ObservableObject
{
    public AiChatKind Kind { get; init; }
    public DateTime Time { get; init; } = DateTime.Now;
    [ObservableProperty] private string text = "";
    [ObservableProperty] private bool isResolved;
    public AiPendingAction? Pending { get; init; }
}
```

`AiPendingAction`（`Models/AiPendingAction.cs`）：

```csharp
public sealed class AiPendingAction
{
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public bool IsDestructive { get; init; }
    public Func<Task<string>> ExecuteAsync { get; init; } = () => Task.FromResult("");
}
```

## 五、实施顺序

1. `AiChatModels` + `AiChatClient.CompleteWithToolsAsync`；补解析单测。
2. `IAiAppHost` + MainViewModel 实现（筛选、更新、删除、启动、发送、设置、打开窗口）。
3. `AiToolRegistry` + `AiToolExecutor` + `AiPendingAction`；补工具校验单测。
4. `AiAssistantService` 多轮循环与确认后继续；用 FakeChatClient 补单测。
5. `AiChatStore` + `PathService.AiChatPath`；补读写/清空单测。
6. `AiAssistantViewModel` + `AiAssistantView` + MainWindow 三/四列布局与开关。
7. 打包、文档、GitHub。

## 六、安全与交互决策

- **写操作一律确认**：元数据、设置、设备配置、启动、发送、删除都先出卡片。
- **删除二次确认**：卡片确认后再弹 MessageBox，明确「记录删除」与「文件移到回收站」的区别。
- **工具白名单**：不允许改 API Key、库目录、便携开关；不允许执行任意 SQL/Shell。
- **参数校验**：未知工具、非法 id、越界年份/评分、未知设置项全部返回错误 JSON 给模型，不执行。
- **隐私**：聊天只发文本；本地聊天记录不含 API Key、ROM 内容；`get_settings` 不返回 Key。
- **模型要求**：需要支持 OpenAI function calling；不支持时给出明确提示，不静默降级。
- **游戏咨询**：基于模型自身知识与库内元数据；不额外接搜索 API（后续需要再评估）。

## 七、验证步骤

1. `dotnet build MicaRomManager.sln -c Release`：0 警告 0 错误。
2. `dotnet test`：全绿，新增测试覆盖：
   - 工具参数解析与非法值拒绝；
   - 设置白名单与危险操作标记；
   - `update_games` diff 与确认后执行；
   - 助手循环：工具调用 → tool 消息 → 最终回答；Pending 后确认继续；
   - 聊天记录保存/加载/清空。
3. 手动验收（配置真实 Key 后）：
   - 「筛选所有未通关的 PS1 游戏」→ 主列表筛选正确。
   - 「把选中的 3 个游戏类型改成角色扮演」→ 确认卡片 → 应用成功。
   - 「介绍一下选中的游戏」→ 返回该游戏的介绍与评价。
   - 「扫描 E 盘并生成 TrimUI 配置」→ 扫描 → 配置预览 → 确认保存。
   - 「把代理改成手动 127.0.0.1:7890」→ 确认 → 设置生效。
   - 「删除这条记录并把文件移到回收站」→ 危险卡片 + 二次确认。
   - 重启程序 → 聊天记录恢复；`data\ai-chat.json` 中无 Key。
4. `dotnet publish src\MicaRomManager -c Release -r win-x64 --self-contained true -o publish`，启动冒烟。
5. 更新 `CHANGELOG.md`、`QUEST.md`、`ROADMAP.md`、`MEMORIES.md`，提交并推送 GitHub。

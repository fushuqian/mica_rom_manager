namespace MicaRomManager.Services.Ai;

/// <summary>AI 助手的工具清单与 JSON Schema。</summary>
public static class AiToolRegistry
{
    /// <summary>允许 AI 读取/修改的设置项；不包含 API Key、库目录与便携开关。</summary>
    public static readonly string[] SettingsWhitelist =
    {
        "network.proxy.mode",
        "network.proxy.address",
        "library.import_mode",
        "ai.base_url",
        "ai.model",
        "ai.timeout_seconds",
        "ai.batch_limit",
    };

    public static IReadOnlyList<AiToolDefinition> All { get; } = Build();

    private static List<AiToolDefinition> Build() => new()
    {
        new AiToolDefinition
        {
            Name = "query_games",
            Description = "按条件查询游戏库。可把筛选条件应用到主列表；返回命中总数与前 20 条精简信息。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["filter"] = FilterSchema(),
                ["apply_to_list"] = Bool("是否把条件应用到主列表；用户说「筛选/只看」时为 true"),
                ["limit"] = Integer("返回条数，1-50，默认 20"),
            }),
        },
        new AiToolDefinition
        {
            Name = "get_game",
            Description = "读取单个游戏的完整元数据与文件状态；不传 game_id 时读取当前选中游戏。用于介绍游戏、回答这是啥游戏。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["game_id"] = Integer("游戏 id；不传则用当前选中游戏"),
            }),
        },
        new AiToolDefinition
        {
            Name = "get_statistics",
            Description = "读取游戏库统计：总数、平台分布、状态分布、收藏数、总游玩时长。",
            Parameters = Schema(new Dictionary<string, object>()),
        },
        new AiToolDefinition
        {
            Name = "list_emulators",
            Description = "列出已配置的模拟器及支持的平台。",
            Parameters = Schema(new Dictionary<string, object>()),
        },
        new AiToolDefinition
        {
            Name = "recommend_games",
            Description = "基于平台、开发商、类型、标签做本地相似推荐；不调用外部接口。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["seed_game_id"] = Integer("种子游戏 id；不传则用当前选中或正在玩的游戏"),
                ["limit"] = Integer("返回条数，1-20，默认 10"),
            }),
        },
        new AiToolDefinition
        {
            Name = "get_settings",
            Description = "读取允许 AI 访问的设置项；API Key 永远不返回。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["keys"] = StringArray("设置键列表；不传则返回全部允许项"),
            }),
        },
        new AiToolDefinition
        {
            Name = "list_drives",
            Description = "列出当前就绪的盘符、卷标与可用空间，用于设备识别。",
            Parameters = Schema(new Dictionary<string, object>()),
        },
        new AiToolDefinition
        {
            Name = "scan_device_tree",
            Description = "只读扫描指定盘的目录结构，只返回目录名与扩展名统计，不返回文件名或文件内容。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["drive_root"] = String("盘符根路径，例如 E:\\"),
            }, "drive_root"),
        },
        new AiToolDefinition
        {
            Name = "update_games",
            Description = "批量修改游戏元数据、评分、状态、标签等。执行前会生成确认卡片，必须等用户确认。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["game_ids"] = IntArray("要修改的游戏 id 列表"),
                ["fields"] = PatchSchema(),
            }, "game_ids", "fields"),
        },
        new AiToolDefinition
        {
            Name = "delete_games",
            Description = "删除游戏记录；可选择把 ROM 文件移到回收站。属于危险操作，执行前会生成红色确认卡片并二次确认。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["game_ids"] = IntArray("要删除的游戏 id 列表"),
                ["recycle_files"] = Bool("true=把 ROM 文件移到回收站；false=只删除记录"),
            }, "game_ids"),
        },
        new AiToolDefinition
        {
            Name = "launch_game",
            Description = "启动指定游戏；需要用户确认。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["game_id"] = Integer("游戏 id"),
                ["disc_index"] = Integer("碟片序号，从 0 开始；单碟不传"),
            }, "game_id"),
        },
        new AiToolDefinition
        {
            Name = "send_games",
            Description = "把游戏加入发送队列；需要已连接设备，并向用户确认。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["game_ids"] = IntArray("要发送的游戏 id 列表"),
            }, "game_ids"),
        },
        new AiToolDefinition
        {
            Name = "set_settings",
            Description = "修改允许的设置项；执行前生成确认卡片。API Key、库目录、便携开关不可修改。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["settings"] = new
                {
                    type = "object",
                    description = "设置键到新值的映射，值统一用字符串",
                    additionalProperties = new { type = "string" },
                },
            }, "settings"),
        },
        new AiToolDefinition
        {
            Name = "save_device_profile",
            Description = "校验并保存 AI 推断的设备配置；需要 drive_root 与 profile，执行前生成确认卡片。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["drive_root"] = String("扫描过的盘符根路径"),
                ["profile"] = DeviceProfileSchema(),
                ["confidence"] = String("high / medium / low"),
            }, "drive_root", "profile"),
        },
        new AiToolDefinition
        {
            Name = "open_window",
            Description = "打开应用内窗口或对话框，例如设置、导入、分享、任务列表、连接设备、AI 刮削。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["window"] = new
                {
                    type = "string",
                    description = "窗口标识",
                    @enum = new[]
                    {
                        "settings", "device_settings", "import", "share", "task_list",
                        "connect_device", "scrape", "ai_tags", "ai_translate", "recommend",
                    },
                },
            }, "window"),
        },
    };

    private static object Schema(Dictionary<string, object> properties, params string[] required) => new
    {
        type = "object",
        properties,
        required,
        additionalProperties = false,
    };

    private static object FilterSchema() => Schema(new Dictionary<string, object>
    {
        ["platform"] = String("平台名，必须来自库内实际平台"),
        ["status"] = String("状态：未玩 / 在玩 / 已通关 / 已放弃"),
        ["region"] = String("区域"),
        ["language"] = String("语言"),
        ["genre"] = String("类型"),
        ["developer"] = String("开发商"),
        ["publisher"] = String("发行商"),
        ["tag"] = String("标签"),
        ["keyword"] = String("标题/路径关键词"),
        ["favorite"] = Bool("true=仅收藏；false=排除收藏"),
        ["only_missing"] = Bool("仅文件缺失"),
        ["year_min"] = Integer("最早年份"),
        ["year_max"] = Integer("最晚年份"),
    });

    private static object PatchSchema() => new
    {
        type = "object",
        description = "要修改的字段；不需要的字段不要传",
        properties = new Dictionary<string, object>
        {
            ["title"] = String("标题"),
            ["genre"] = String("类型"),
            ["region"] = String("区域"),
            ["language"] = String("语言"),
            ["special_note"] = String("特注"),
            ["publisher"] = String("发行商"),
            ["developer"] = String("开发商"),
            ["notes"] = String("备注/简介"),
            ["tags"] = String("标签，逗号分隔；会整体替换"),
            ["status"] = String("未玩 / 在玩 / 已通关 / 已放弃"),
            ["release_year"] = Integer("发行年份 1950-2100"),
            ["rating"] = Integer("评分 0-5"),
            ["favorite"] = Bool("是否收藏"),
        },
        additionalProperties = false,
    };

    private static object DeviceProfileSchema() => new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["name"] = String("设备显示名"),
            ["brand"] = String("品牌"),
            ["model"] = String("型号"),
            ["rootFolder"] = String("盘内根目录，必须是扫描结果中的顶层目录"),
            ["folderTemplate"] = String("目录模板，必须包含 {root}"),
            ["allowUnmappedPlatforms"] = Bool("是否允许未映射平台"),
            ["platformFolders"] = new
            {
                type = "object",
                description = "平台名到设备目录名的映射",
                additionalProperties = new { type = "string" },
            },
            ["notes"] = String("备注"),
        },
        required = new[] { "rootFolder", "folderTemplate" },
        additionalProperties = false,
    };

    private static object String(string description) => new { type = "string", description };

    private static object Integer(string description) => new { type = "integer", description };

    private static object Bool(string description) => new { type = "boolean", description };

    private static object StringArray(string description) => new
    {
        type = "array",
        description,
        items = new { type = "string" },
    };

    private static object IntArray(string description) => new
    {
        type = "array",
        description,
        items = new { type = "integer" },
    };
}

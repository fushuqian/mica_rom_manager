using MicaRomManager.Models;

namespace MicaRomManager.Services.Ai;

/// <summary>集中维护各功能的提示词，便于后续调优。</summary>
public static class AiPrompts
{
    public const string PromptVersion = "1";

    public const string MetadataSystem =
        "你是复古游戏元数据整理助手。只输出一个 JSON 对象，不要输出 Markdown、解释或额外文字。"
        + "不确定的字段留空字符串，不要编造发行商或年份。";

    public static string MetadataUser(Game game, IEnumerable<string> regions,
        IEnumerable<string> languages, IEnumerable<string> genres, string searchContext = "")
    {
        var searchBlock = string.IsNullOrWhiteSpace(searchContext)
            ? ""
            : $"\n联网搜索结果（只作为事实参考，不要输出链接）：\n{searchContext}\n";

        return $$"""
            请根据以下信息补全游戏元数据：
            文件名：{{game.FileName}}
            平台：{{game.Platform}}
            现有标题：{{game.Title}}
            现有年份：{{game.ReleaseYear?.ToString() ?? ""}}
            {{searchBlock}}
            优先使用这些既有词典值：
            区域：{{Join(regions)}}
            语言：{{Join(languages)}}
            类型：{{Join(genres)}}

            只输出 JSON：
            {"title":"","genre":"","region":"","language":"","release_year":null,"publisher":"","developer":"","summary":""}

            规则：
            - title 用中文常用名；没有公认中文名时保留原名。
            - region / language / genre 尽量从词典值中选择；确无对应时给简短中文。
            - release_year 为四位整数，不确定填 null。
            - summary 用一两句中文介绍，不要剧透。
            - 有搜索结果时优先采用其中的年份、发行商、开发商；与你的知识冲突时优先权威来源，仍不确定就留空。
            """;
    }

    public const string TagSystem =
        "你是游戏标签整理助手。只输出一个 JSON 对象，不要输出 Markdown、解释或额外文字。";

    public static string TagUser(Game game, IEnumerable<string> existingTags)
    {
        return $$"""
            为下面的游戏生成 1–5 个简短中文标签，并给出一个类型：
            标题：{{game.Title}}
            平台：{{game.Platform}}
            现有类型：{{game.Genre}}
            现有标签：{{string.Join("、", existingTags)}}

            只输出 JSON：
            {"tags":[""],"genre":""}

            规则：
            - 标签每个不超过 6 个汉字，不要重复现有标签。
            - 优先使用常见标签（如 动作、角色扮演、横版、解谜、联机、汉化）。
            - genre 只给一个最贴切的类型。
            """;
    }

    public const string TranslateSystem =
        "你是游戏标题本地化助手。只输出一个 JSON 对象，不要输出 Markdown、解释或额外文字。";

    public static string TranslateUser(Game game)
    {
        return $$"""
            把下面的游戏标题转换为中文通用译名：
            原标题：{{game.Title}}
            平台：{{game.Platform}}
            区域：{{game.Region}}

            只输出 JSON：
            {"title":"","note":""}

            规则：
            - 优先使用官方中文名或玩家通用译名。
            - 没有通用译名时，title 保留原英文，note 写「暂无通用中文名」。
            - 不加书名号、不加拼音、不写长句解释。
            - 保留系列编号与副标题结构。
            """;
    }

    public const string QuerySystem =
        "你是游戏库查询助手。把用户的自然语言请求转换成受控的 JSON 查询计划，"
        + "只输出 JSON，不要输出 SQL、Markdown 或解释文字。无法理解时输出 {\"intent\":\"unknown\"}。";

    public static string QueryUser(string request, IEnumerable<string> platforms, IEnumerable<string> statuses,
        IEnumerable<string> regions, IEnumerable<string> languages, IEnumerable<string> genres,
        IEnumerable<string> developers, IEnumerable<string> tags)
    {
        return $$"""
            用户请求：{{request}}

            可选值：
            平台：{{Join(platforms)}}
            状态：{{Join(statuses)}}
            区域：{{Join(regions)}}
            语言：{{Join(languages)}}
            类型：{{Join(genres)}}
            开发商：{{Join(developers)}}
            标签：{{Join(tags)}}

            只输出 JSON：
            {"intent":"filter|action","filter":{"platform":"","status":"","region":"","language":"","genre":"","developer":"","tag":"","keyword":"","favorite":null,"only_missing":false,"year_min":null,"year_max":null},"sort_field":"","sort_direction":"asc","action":{"type":"","value":""},"explanation":""}

            规则：
            - intent=filter 时只填 filter；intent=action 时同时填 filter 和 action。
            - action.type 只能是 set_status / toggle_favorite / add_tag / remove_tag。
            - set_status 的 value 必须是状态可选值之一。
            - 不要生成删除、移动、启动游戏等危险操作。
            """;
    }

    public const string DeviceSystem =
        "你是掌机存储结构识别助手。根据目录扫描摘要推断设备 ROM 目录规则，只输出 JSON，不要输出 Markdown 或解释。";

    public static string DeviceUser(string summary, IEnumerable<string> platforms)
    {
        return $$"""
            目录扫描摘要（只包含目录名与扩展名统计）：
            {{summary}}

            平台词典：{{Join(platforms)}}

            只输出 JSON：
            {"name":"","brand":"","model":"","transport":"drive","rootFolder":"","folderTemplate":"{root}/{platform}","allowUnmappedPlatforms":true,"platformFolders":{},"notes":"","confidence":"high|medium|low"}

            规则：
            - rootFolder 必须是摘要中出现的顶层目录名。
            - folderTemplate 必须包含 {root}，不要使用 .. 或绝对路径。
            - platformFolders 的 key 用平台词典中的名字，value 用摘要中存在的目录名。
            - 无法确定时 confidence 用 low，并在 notes 说明。
            """;
    }

    private static string Join(IEnumerable<string> values) =>
        string.Join("、", values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase));
}

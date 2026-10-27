using System.Text.RegularExpressions;
using MicaRomManager.Data;

namespace MicaRomManager.Services.Ai;

public sealed record AiPromptDefinition(
    string Id,
    string Name,
    string Description,
    string Variables,
    string DefaultSystem,
    string DefaultUser);

/// <summary>设置 → 提示词：读取/保存各 AI 功能的自定义提示词；未自定义或用空时回落内置版本。</summary>
public static class AiPromptCatalog
{
    public const string AssistantId = "assistant";

    private const string KeyPrefix = "ai.prompt.";

    private static readonly Regex Placeholder = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    public static IReadOnlyList<AiPromptDefinition> Definitions { get; } = new[]
    {
        new AiPromptDefinition(
            AssistantId, "AI 助手",
            "右侧聊天助手的系统提示词。修改后下一轮对话生效。",
            "{{GameCount}} 游戏总数、{{Platforms}} 平台分布、{{Selected}} 当前选中、{{FilteredCount}} 筛选结果数、{{Device}} 已连接设备",
            AiPrompts.AssistantSystemTemplate, ""),
        new AiPromptDefinition(
            "metadata", "元数据刮削",
            "根据文件名、平台与联网搜索结果补全游戏元数据，要求只输出 JSON。",
            "{{FileName}} 文件名、{{Platform}} 平台、{{Title}} 现有标题、{{ReleaseYear}} 现有年份、{{Regions}} 区域词典、{{Languages}} 语言词典、{{Genres}} 类型词典、{{SearchBlock}} 联网搜索结果",
            AiPrompts.MetadataSystem, AiPrompts.MetadataUserTemplate),
        new AiPromptDefinition(
            "tag", "标签生成",
            "为选中游戏生成标签与类型，要求只输出 JSON。",
            "{{Title}} 标题、{{Platform}} 平台、{{Genre}} 现有类型、{{Tags}} 现有标签",
            AiPrompts.TagSystem, AiPrompts.TagUserTemplate),
        new AiPromptDefinition(
            "translate", "标题翻译",
            "生成游戏的中文通用译名，要求只输出 JSON。",
            "{{Title}} 原标题、{{Platform}} 平台、{{Region}} 区域",
            AiPrompts.TranslateSystem, AiPrompts.TranslateUserTemplate),
        new AiPromptDefinition(
            "query", "自然语言查询",
            "把自然语言请求转成受控 JSON 查询计划，要求只输出 JSON。",
            "{{Request}} 用户请求、{{Platforms}} 平台、{{Statuses}} 状态、{{Regions}} 区域、{{Languages}} 语言、{{Genres}} 类型、{{Developers}} 开发商、{{Tags}} 标签",
            AiPrompts.QuerySystem, AiPrompts.QueryUserTemplate),
        new AiPromptDefinition(
            "device", "设备结构识别",
            "根据目录扫描摘要推断掌机存储结构，要求只输出 JSON。",
            "{{Summary}} 目录扫描摘要、{{Platforms}} 平台词典",
            AiPrompts.DeviceSystem, AiPrompts.DeviceUserTemplate),
    };

    public static AiPromptDefinition Find(string id) =>
        Definitions.FirstOrDefault(definition => definition.Id == id)
        ?? throw new ArgumentException($"未知的提示词：{id}", nameof(id));

    public static string SystemFor(string id)
    {
        var definition = Find(id);
        var custom = Read(Key(id, "system"));
        return string.IsNullOrWhiteSpace(custom) ? definition.DefaultSystem : custom;
    }

    public static string UserFor(string id)
    {
        var definition = Find(id);
        var custom = Read(Key(id, "user"));
        return string.IsNullOrWhiteSpace(custom) ? definition.DefaultUser : custom;
    }

    public static bool IsCustomized(string id) =>
        !string.IsNullOrWhiteSpace(Read(Key(id, "system"))) ||
        !string.IsNullOrWhiteSpace(Read(Key(id, "user")));

    public static void Save(string id, string system, string user)
    {
        var store = Store();
        if (store == null) return;

        store.Set(Key(id, "system"), string.IsNullOrWhiteSpace(system) ? null : system);
        store.Set(Key(id, "user"), string.IsNullOrWhiteSpace(user) ? null : user);
    }

    public static void Reset(string id)
    {
        var store = Store();
        if (store == null) return;

        store.Set(Key(id, "system"), null);
        store.Set(Key(id, "user"), null);
    }

    public static string RenderSystem(string id, IReadOnlyDictionary<string, string> variables) =>
        Render(SystemFor(id), variables);

    public static string RenderUser(string id, IReadOnlyDictionary<string, string> variables) =>
        Render(UserFor(id), variables);

    /// <summary>替换 {{Name}} 变量；未知变量原样保留，方便用户看出占位符写错了。</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> variables)
    {
        if (template.Length == 0) return template;

        return Placeholder.Replace(template,
            match => variables.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }

    private static string Key(string id, string part) => $"{KeyPrefix}{id}.{part}";

    private static string? Read(string key) => Store()?.Get(key);

    private static SettingsRepository? Store() => AppServices.Settings;
}

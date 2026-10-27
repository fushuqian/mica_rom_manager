using System.IO;
using System.Text.Json.Serialization;
using MicaRomManager.Data;
using MicaRomManager.Models;
using MicaRomManager.Services.Ai;

namespace MicaRomManager.Services.Devices;

public sealed record DeviceProfileResult(
    DeviceProfile? Profile,
    string Confidence,
    List<string> Warnings,
    string Error)
{
    public bool CanApply => Profile != null && Error.Length == 0;
}

/// <summary>根据目录扫描摘要让 AI 推断设备配置；只生成建议，不直接写文件。</summary>
public sealed class AiDeviceProfiler
{
    public async Task<DeviceProfileResult> ProfileAsync(DeviceScanSummary summary, CancellationToken token)
    {
        if (!AppServices.Ai.Options.IsConfigured)
            return new DeviceProfileResult(null, "", new List<string>(),
                "还没有配置 AI，请先到 设置 → AI 填写 BaseUrl、API Key 和模型。");

        var platforms = AppServices.Lookups.List(LookupRepository.Platform);
        var response = await AppServices.Ai.CompleteAsync(AiPrompts.DeviceSystem,
            AiPrompts.DeviceUser(summary.ToPromptText(), platforms), token);
        if (!response.Success)
            return new DeviceProfileResult(null, "", new List<string>(), response.Error);

        var payload = AiJson.Parse<DeviceProfilePayload>(response.Content);
        if (payload == null)
            return new DeviceProfileResult(null, "", new List<string>(),
                $"返回内容不是有效 JSON：{AiJson.Snippet(response.Content, 160)}");

        var draft = new DeviceProfile
        {
            Name = (payload.Name ?? "").Trim(),
            Brand = (payload.Brand ?? "").Trim(),
            Model = (payload.Model ?? "").Trim(),
            Transport = "drive",
            RootFolder = (payload.RootFolder ?? "").Trim(),
            FolderTemplate = (payload.FolderTemplate ?? "").Trim(),
            AllowUnmappedPlatforms = payload.AllowUnmappedPlatforms ?? true,
            PlatformFolders = payload.PlatformFolders ?? new Dictionary<string, string>(),
            Notes = (payload.Notes ?? "").Trim(),
        };

        return Validate(summary, draft, (payload.Confidence ?? "").Trim(), platforms);
    }

    /// <summary>校验 AI 建议；非法根目录/模板直接拒绝，映射问题降级为警告。</summary>
    public static DeviceProfileResult Validate(DeviceScanSummary summary, DeviceProfile draft,
        string confidence, IEnumerable<string> platforms)
    {
        var warnings = new List<string>();
        var topLevel = summary.TopLevelDirectories.ToList();
        if (string.IsNullOrWhiteSpace(draft.RootFolder))
            return Invalid("没有识别出盘内根目录。", warnings);

        var root = topLevel.FirstOrDefault(name =>
            string.Equals(name, draft.RootFolder, StringComparison.OrdinalIgnoreCase));
        if (root == null)
            return Invalid($"根目录「{draft.RootFolder}」不在扫描结果中，请手动指定。", warnings);
        draft.RootFolder = root;

        var template = draft.FolderTemplate.Length == 0 ? "{root}/{platform}" : draft.FolderTemplate;
        if (!template.Contains("{root}", StringComparison.OrdinalIgnoreCase))
            return Invalid("目录模板必须包含 {root}。", warnings);
        if (template.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(template)
            || template.Contains(':'))
            return Invalid("目录模板不能使用绝对路径或 ..。", warnings);
        draft.FolderTemplate = template;

        var allowedPlatforms = platforms.ToList();
        var directoryNames = summary.DirectoryNames.ToList();
        var cleaned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in draft.PlatformFolders)
        {
            var platform = allowedPlatforms.FirstOrDefault(name =>
                string.Equals(name, pair.Key, StringComparison.OrdinalIgnoreCase));
            if (platform == null)
            {
                warnings.Add($"平台「{pair.Key}」不在平台词典中，已忽略。");
                continue;
            }

            var folder = (pair.Value ?? "").Trim();
            if (folder.Length == 0) continue;
            if (folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                warnings.Add($"目录名「{folder}」不合法，已忽略。");
                continue;
            }

            if (!directoryNames.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                warnings.Add($"目录「{folder}」不在扫描结果中，已忽略。");
                continue;
            }

            cleaned[platform] = folder;
        }

        draft.PlatformFolders = cleaned;
        draft.Transport = "drive";
        if (string.IsNullOrWhiteSpace(draft.Name)) draft.Name = "新设备";

        var level = confidence.ToLowerInvariant();
        if (level is not ("high" or "medium" or "low")) level = "medium";
        if (level == "low") warnings.Add("模型置信度较低，请人工核对根目录与平台映射。");

        return new DeviceProfileResult(draft, level, warnings, "");
    }

    private static DeviceProfileResult Invalid(string error, List<string> warnings) =>
        new(null, "", warnings, error);

    private sealed class DeviceProfilePayload
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("brand")] public string? Brand { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("rootFolder")] public string? RootFolder { get; set; }
        [JsonPropertyName("folderTemplate")] public string? FolderTemplate { get; set; }
        [JsonPropertyName("allowUnmappedPlatforms")] public bool? AllowUnmappedPlatforms { get; set; }
        [JsonPropertyName("platformFolders")] public Dictionary<string, string>? PlatformFolders { get; set; }
        [JsonPropertyName("notes")] public string? Notes { get; set; }
        [JsonPropertyName("confidence")] public string? Confidence { get; set; }
    }
}

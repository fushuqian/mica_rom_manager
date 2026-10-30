using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using MicaRomManager.Models;

namespace MicaRomManager.Services.Devices;

/// <summary>
/// 设备配置仓库：内置 Resources\DeviceProfiles.json + 用户 data\devices\*.json。
/// 同 Id 的用户配置覆盖内置配置；用户配置按单个对象存一个文件，方便复制分享。
/// </summary>
public sealed class DeviceProfileStore
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _userDir;
    private readonly string _builtinFile;

    /// <summary>内置配置文件缺失（发布目录漏带 Resources\DeviceProfiles.json 时会命中）。</summary>
    public bool BuiltInMissing { get; private set; }

    public DeviceProfileStore(string userDir, string builtinFile)
    {
        _userDir = userDir;
        _builtinFile = builtinFile;
        Directory.CreateDirectory(_userDir);
    }

    public List<DeviceProfile> Load()
    {
        var result = new List<DeviceProfile>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in LoadBuiltIn())
        {
            profile.IsBuiltIn = true;
            if (string.IsNullOrWhiteSpace(profile.Id)) continue;
            index[profile.Id] = result.Count;
            result.Add(profile);
        }

        foreach (var profile in LoadUser())
        {
            profile.IsBuiltIn = false;
            if (string.IsNullOrWhiteSpace(profile.Id)) continue;
            if (index.TryGetValue(profile.Id, out var existing))
            {
                result[existing] = profile;
            }
            else
            {
                index[profile.Id] = result.Count;
                result.Add(profile);
            }
        }

        return result;
    }

    public DeviceProfile? Find(string id) =>
        Load().FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>保存到用户目录；返回最终使用的 Id。</summary>
    public string Save(DeviceProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            profile.Id = MakeUniqueId(profile.Name);
        }

        var path = Path.Combine(_userDir, profile.Id + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(profile, WriteOptions));
        return profile.Id;
    }

    /// <summary>删除用户配置；内置配置本身不可删除，删除覆盖文件后会恢复内置。</summary>
    public bool Delete(DeviceProfile profile)
    {
        var path = Path.Combine(_userDir, profile.Id + ".json");
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    public bool HasUserOverride(string id) =>
        !string.IsNullOrWhiteSpace(id) && File.Exists(Path.Combine(_userDir, id + ".json"));

    private IEnumerable<DeviceProfile> LoadBuiltIn()
    {
        if (!File.Exists(_builtinFile))
        {
            // 静默返回空会让「设备」页看起来像没有内置配置，问题却查不到原因，这里留一条痕迹。
            BuiltInMissing = true;
            LogMissingBuiltIn();
            return Array.Empty<DeviceProfile>();
        }

        BuiltInMissing = false;

        try
        {
            return JsonSerializer.Deserialize<List<DeviceProfile>>(File.ReadAllText(_builtinFile), ReadOptions)
                   ?? new List<DeviceProfile>();
        }
        catch
        {
            return Array.Empty<DeviceProfile>();
        }
    }

    private IEnumerable<DeviceProfile> LoadUser()
    {
        var result = new List<DeviceProfile>();
        if (!Directory.Exists(_userDir)) return result;

        foreach (var file in Directory.EnumerateFiles(_userDir, "*.json").OrderBy(f => f))
        {
            try
            {
                var text = File.ReadAllText(file);
                if (text.TrimStart().StartsWith('['))
                {
                    var list = JsonSerializer.Deserialize<List<DeviceProfile>>(text, ReadOptions);
                    if (list != null) result.AddRange(list);
                }
                else
                {
                    var profile = JsonSerializer.Deserialize<DeviceProfile>(text, ReadOptions);
                    if (profile != null) result.Add(profile);
                }
            }
            catch
            {
                // 单个文件格式错误不影响其它配置
            }
        }

        return result;
    }

    private void LogMissingBuiltIn()
    {
        try
        {
            var dataDir = Directory.GetParent(_userDir)?.FullName;
            if (string.IsNullOrEmpty(dataDir)) return;

            var logs = Path.Combine(dataDir, "logs");
            Directory.CreateDirectory(logs);
            File.AppendAllText(Path.Combine(logs, "app.log"),
                $"[{DateTime.Now:O}] 缺少内置设备配置：{_builtinFile}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败不影响使用
        }
    }

    private string MakeUniqueId(string name)
    {
        var slug = Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug)) slug = "device";

        var existing = new HashSet<string>(Load().Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(slug)) return slug;

        for (var i = 2; ; i++)
        {
            var candidate = $"{slug}-{i}";
            if (!existing.Contains(candidate)) return candidate;
        }
    }
}

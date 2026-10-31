using System.Text.Json;
using MicaRomManager.Data;
using MicaRomManager.Models;

namespace MicaRomManager.Services;

/// <summary>
/// 用户自定义的主机顺序（设置 → 平台 里拖出来的）。
/// 没存过、或新导入的主机，回落到「厂商 → 首发年代」的默认顺序。
/// </summary>
public static class PlatformOrderStore
{
    public const string SettingKey = "ui.platform_order";

    private static List<string> _order = new();

    /// <summary>启动时读一次并缓存，之后 Save/Reset 直接改缓存。</summary>
    public static void Initialize(SettingsRepository settings) =>
        _order = Parse(settings.Get(SettingKey));

    public static IReadOnlyList<string> Current => _order;

    public static void Save(SettingsRepository settings, IEnumerable<string> names)
    {
        var list = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _order = list;
        settings.Set(SettingKey, JsonSerializer.Serialize(list));
    }

    /// <summary>清空自定义顺序，回到「厂商 → 年代」。</summary>
    public static void Reset(SettingsRepository settings)
    {
        _order = new List<string>();
        settings.Set(SettingKey, "");
    }

    /// <summary>主机改名后同步自定义顺序，别让它掉到末位。</summary>
    public static void Rename(SettingsRepository settings, string oldName, string newName)
    {
        var index = _order.FindIndex(item => string.Equals(item, oldName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;

        _order[index] = newName.Trim();
        settings.Set(SettingKey, JsonSerializer.Serialize(_order));
    }

    /// <summary>主机被删除后把它从顺序里摘掉。</summary>
    public static void Remove(SettingsRepository settings, string name)
    {
        if (_order.RemoveAll(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)) == 0) return;
        settings.Set(SettingKey, JsonSerializer.Serialize(_order));
    }

    public static bool HasCustomOrder => _order.Count > 0;

    /// <summary>用户顺序里的位置；不在里面的排到最后。</summary>
    public static int RankOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return int.MaxValue;
        var index = _order.FindIndex(item => string.Equals(item, name.Trim(), StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : int.MaxValue;
    }

    /// <summary>先按用户顺序，再按「厂商 → 年代」兜底（新主机不会乱插）。</summary>
    public static int Compare(string? left, string? right)
    {
        var byUser = RankOf(left).CompareTo(RankOf(right));
        return byUser != 0 ? byUser : PlatformOrder.CompareByVendorThenEra(left, right);
    }

    public static IComparer<string> Comparer { get; } = System.Collections.Generic.Comparer<string>.Create(Compare);

    private static List<string> Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(stored) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}

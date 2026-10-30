namespace MicaRomManager.Models;

/// <summary>主界面列表的视图状态（列宽、排序、筛选、列显隐），用于下次启动还原。</summary>
public sealed class ListViewState
{
    public string Search { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Status { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Region { get; set; } = "";
    public string Language { get; set; } = "";
    public string Developer { get; set; } = "";
    public bool OnlyFavorites { get; set; }
    public bool OnlyMissing { get; set; }
    public string SortColumn { get; set; } = "";
    public string SortDirection { get; set; } = "";
    public Dictionary<string, double> ColumnWidths { get; set; } = new();
    /// <summary>被隐藏的列（用列 Header 文本作键）；旧版本 JSON 没有该字段时视为空。</summary>
    public List<string> HiddenColumns { get; set; } = new();

    /// <summary>视图布局版本：2 起默认隐藏低频列，避免一打开就横向滚动。</summary>
    public int LayoutVersion { get; set; }

    public static IReadOnlyList<string> DefaultHiddenColumns { get; } = new[]
    {
        "指纹", "标签", "特注", "类型", "光碟数", "格式", "次数", "时长",
    };

    /// <summary>
    /// 过滤掉已经不存在/已改名的列，并保证至少保留一列；
    /// 避免旧配置或全部隐藏把表格变成空白。
    /// </summary>
    public void PruneHidden(IReadOnlyCollection<string> availableHeaders)
    {
        var available = availableHeaders
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        HiddenColumns = HiddenColumns
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .Distinct(StringComparer.Ordinal)
            .Where(header => available.Contains(header, StringComparer.Ordinal))
            .ToList();

        if (available.Count > 0 && HiddenColumns.Count >= available.Count)
            HiddenColumns.Clear();
    }
}

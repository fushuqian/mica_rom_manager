namespace MicaRomManager.Models;

/// <summary>主界面列表的视图状态（列宽、排序、筛选），用于下次启动还原。</summary>
public sealed class ListViewState
{
    public string Search { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Status { get; set; } = "";
    public string Tag { get; set; } = "";
    public bool OnlyFavorites { get; set; }
    public bool OnlyMissing { get; set; }
    public string SortColumn { get; set; } = "";
    public string SortDirection { get; set; } = "";
    public Dictionary<string, double> ColumnWidths { get; set; } = new();
}

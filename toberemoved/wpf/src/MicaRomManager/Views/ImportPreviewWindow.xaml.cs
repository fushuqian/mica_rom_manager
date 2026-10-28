using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using MicaRomManager.Helpers;
using MicaRomManager.Services;

namespace MicaRomManager.Views;

public partial class ImportPreviewWindow : Window
{
    public ObservableCollection<ImportCandidate> Items { get; }
    public ObservableCollection<string> Platforms { get; } = new(PlatformCatalog.Common);
    public string Summary => $"共 {Items.Count} 个文件，其中 {Items.Count(i => i.NeedsSpecify)} 个需要指定平台";

    public ImportPreviewWindow(List<ImportCandidate> candidates)
    {
        InitializeComponent();
        Items = new ObservableCollection<ImportCandidate>(candidates);

        foreach (var platform in candidates.Select(c => c.Platform)
                     .Where(p => !string.IsNullOrWhiteSpace(p) && p != ExtensionMap.UnspecifiedPlatform)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Platforms.Contains(platform)) Platforms.Add(platform);
        }

        DataContext = this;
    }

    private void ApplyToAll_Click(object sender, RoutedEventArgs e)
    {
        var platform = BulkPlatformCombo.Text?.Trim();
        if (string.IsNullOrWhiteSpace(platform)) return;

        foreach (var item in Items) item.Platform = platform;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        PreviewGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        PreviewGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var missing = Items.Count(item => string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.Platform));
        if (missing > 0)
        {
            MessageBox.Show($"还有 {missing} 条记录的标题或平台为空，请补充后再导入。", "导入预览",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}

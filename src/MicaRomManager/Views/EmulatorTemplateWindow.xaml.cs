using System.Windows;
using System.Windows.Controls;
using MicaRomManager.Models;

namespace MicaRomManager.Views;

/// <summary>新增模拟器时从内置模板里挑一个；SelectedTemplate 为 null 表示空白模拟器。</summary>
public partial class EmulatorTemplateWindow : Window
{
    private sealed class Choice
    {
        public string Name { get; init; } = "";
        public string PlatformsText { get; init; } = "";
        public string Notes { get; init; } = "";
        public EmulatorTemplate? Template { get; init; }
    }

    private readonly List<Choice> _choices = new();

    public EmulatorTemplate? SelectedTemplate { get; private set; }

    public EmulatorTemplateWindow(IReadOnlyList<EmulatorTemplate> templates)
    {
        InitializeComponent();

        foreach (var template in templates.Where(template => !string.IsNullOrWhiteSpace(template.Name)))
        {
            _choices.Add(new Choice
            {
                Name = template.Name,
                PlatformsText = template.Platforms.Count == 0
                    ? "平台：未指定"
                    : "平台：" + string.Join("、", template.Platforms),
                Notes = template.Notes,
                Template = template,
            });
        }

        _choices.Add(new Choice
        {
            Name = "空白模拟器",
            PlatformsText = "不预填平台与参数",
            Notes = "创建一个只有名称和默认启动参数的模拟器，之后手动配置。",
        });

        ApplyFilter("");
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ApplyFilter(SearchBox.Text);

    private void ApplyFilter(string text)
    {
        // XAML 初始化期间 TextChanged 可能先于其他控件赋值触发
        if (TemplateList == null || TemplateCountText == null) return;

        var query = text.Trim();
        var filtered = query.Length == 0
            ? _choices
            : _choices.Where(choice =>
                    choice.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    choice.PlatformsText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    choice.Notes.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        TemplateList.ItemsSource = filtered;
        TemplateList.SelectedIndex = filtered.Count > 0 ? 0 : -1;
        TemplateCountText.Text = query.Length == 0
            ? $"共 {_choices.Count} 个"
            : $"{filtered.Count} / {_choices.Count} 个";
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedItem is not Choice choice) return;

        SelectedTemplate = choice.Template;
        DialogResult = true;
    }
}

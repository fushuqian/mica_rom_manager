using System.Windows;
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

    public EmulatorTemplate? SelectedTemplate { get; private set; }

    public EmulatorTemplateWindow(IReadOnlyList<EmulatorTemplate> templates)
    {
        InitializeComponent();

        var choices = templates
            .Where(template => !string.IsNullOrWhiteSpace(template.Name))
            .Select(template => new Choice
            {
                Name = template.Name,
                PlatformsText = template.Platforms.Count == 0
                    ? "平台：未指定"
                    : "平台：" + string.Join("、", template.Platforms),
                Notes = template.Notes,
                Template = template,
            })
            .ToList();

        choices.Add(new Choice
        {
            Name = "空白模拟器",
            PlatformsText = "不预填平台与参数",
            Notes = "创建一个只有名称和默认启动参数的模拟器，之后手动配置。",
        });

        TemplateList.ItemsSource = choices;
        TemplateList.SelectedIndex = 0;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedItem is not Choice choice) return;

        SelectedTemplate = choice.Template;
        DialogResult = true;
    }
}

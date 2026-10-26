using System.IO;
using System.Windows;
using MicaRomManager.Models;

namespace MicaRomManager.Views;

/// <summary>多碟游戏启动前选择碟片。</summary>
public partial class DiscSelectWindow : Window
{
    public sealed record DiscItem(int Index, string Path, string Display);

    public string? SelectedPath { get; private set; }

    public DiscSelectWindow(Game game)
    {
        InitializeComponent();

        TitleText.Text = game.Title;
        var items = game.Discs
            .Select((path, index) => new DiscItem(index + 1, path,
                $"碟 {index + 1}：{Path.GetFileName(path)}{(File.Exists(path) ? "" : "（文件缺失）")}"))
            .ToList();

        DiscList.ItemsSource = items;
        DiscList.SelectedIndex = 0;
        HintText.Text = $"该游戏包含 {items.Count} 张碟片，请选择要启动的碟片。";
    }

    private void OnLaunch(object sender, RoutedEventArgs e)
    {
        if (DiscList.SelectedItem is not DiscItem item) return;

        SelectedPath = item.Path;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnListDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        OnLaunch(sender, e);
}

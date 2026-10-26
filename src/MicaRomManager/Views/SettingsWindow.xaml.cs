using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MicaRomManager.Models;
using MicaRomManager.Services;
using Microsoft.Win32;

namespace MicaRomManager.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        LoadGeneral();
        DataDirText.Text = AppServices.Paths.DataDir;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"版本 {version?.ToString(3) ?? "0.1.0"}";
    }

    private void LoadGeneral()
    {
        var copyOnImport = AppServices.Settings.GetBool("library.copy_on_import");
        ImportModeCombo.SelectedIndex = copyOnImport ? 1 : 0;
        LibraryRootBox.Text = AppServices.Settings.Get("library.root") ?? "";

        var mode = AppServices.Settings.Get(NetworkClientProvider.ModeKey);
        ProxyModeCombo.SelectedIndex = mode switch { "none" => 0, "manual" => 2, _ => 1 };
        ProxyAddressBox.Text = AppServices.Settings.Get(NetworkClientProvider.AddressKey) ?? NetworkClientProvider.DefaultAddress;
        ProxyAddressBox.IsEnabled = ProxyModeCombo.SelectedIndex == 2;
    }

    private string CurrentProxyMode() => ProxyModeCombo.SelectedIndex switch
    {
        0 => "none",
        2 => "manual",
        _ => "system",
    };

    private void SaveGeneral_Click(object sender, RoutedEventArgs e)
    {
        var mode = CurrentProxyMode();
        if (mode == "manual" && !NetworkClientProvider.TryParseProxyAddress(ProxyAddressBox.Text, out _, out _))
        {
            MessageBox.Show("手动代理地址格式不正确，应为 host:port，例如 127.0.0.1:7890。",
                "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppServices.Settings.Set("library.copy_on_import", (ImportModeCombo.SelectedIndex == 1).ToString());
        AppServices.Settings.Set("library.root", LibraryRootBox.Text.Trim());
        AppServices.Settings.Set(NetworkClientProvider.ModeKey, mode);
        AppServices.Settings.Set(NetworkClientProvider.AddressKey, ProxyAddressBox.Text.Trim());
        AppServices.RebuildNetwork();
        MessageBox.Show("常规设置已保存。", "设置", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ProxyMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProxyAddressBox != null) ProxyAddressBox.IsEnabled = CurrentProxyMode() == "manual";
    }

    private async void TestNetwork_Click(object sender, RoutedEventArgs e)
    {
        var mode = CurrentProxyMode();
        if (mode == "manual" && !NetworkClientProvider.TryParseProxyAddress(ProxyAddressBox.Text, out _, out _))
        {
            NetworkTestText.Text = "代理地址格式不正确，应为 host:port。";
            return;
        }

        // 测试前先应用当前界面上的代理设置，避免必须先点保存
        AppServices.Settings.Set(NetworkClientProvider.ModeKey, mode);
        AppServices.Settings.Set(NetworkClientProvider.AddressKey, ProxyAddressBox.Text.Trim());
        AppServices.RebuildNetwork();

        NetworkTestText.Text = "正在测试…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await AppServices.Network.ApiClient.GetAsync("https://api.github.com/rate_limit", cts.Token);
            NetworkTestText.Text = response.IsSuccessStatusCode
                ? $"连接正常（HTTP {(int)response.StatusCode}）"
                : $"GitHub 返回 HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }
        catch (OperationCanceledException)
        {
            NetworkTestText.Text = "连接超时（10 秒），请检查代理设置。";
        }
        catch (Exception ex)
        {
            NetworkTestText.Text = $"连接失败：{ex.Message}";
        }
    }

    private void BrowseLibraryRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择 ROM 库目录", InitialDirectory = LibraryRootBox.Text };
        if (dialog.ShowDialog() == true) LibraryRootBox.Text = dialog.FolderName;
    }

    private void OpenDataDir_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{AppServices.Paths.DataDir}\"")
        {
            UseShellExecute = true,
        });
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "备份数据库",
            Filter = "SQLite 数据库|*.db",
            FileName = $"mica-backup-{DateTime.Now:yyyyMMdd-HHmm}.db",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            File.Copy(AppServices.Paths.DbPath, dialog.FileName, true);
            MaintenanceStatus.Text = $"已备份到：{dialog.FileName}";
        }
        catch (Exception ex)
        {
            MaintenanceStatus.Text = $"备份失败：{ex.Message}";
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择备份数据库",
            Filter = "SQLite 数据库|*.db|所有文件|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        var answer = MessageBox.Show("用所选备份覆盖当前数据库？\n\n建议先备份当前数据库，恢复后需要重启程序。",
            "恢复数据库", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            File.Copy(dialog.FileName, AppServices.Paths.DbPath, true);
            MaintenanceStatus.Text = "数据库已恢复，请重启程序生效。";
        }
        catch (Exception ex)
        {
            MaintenanceStatus.Text = $"恢复失败：{ex.Message}";
        }
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出 CSV",
            Filter = "CSV 文件|*.csv",
            FileName = $"rom-library-{DateTime.Now:yyyyMMdd}.csv",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine("标题,平台,类型,区域,语言,年份,状态,收藏,游玩次数,游玩时长(秒),最近游玩,文件大小,标签,封面,ROM路径,备注");
            foreach (var game in AppServices.GameRepo.ListAll())
            {
                builder.AppendLine(string.Join(",",
                    Quote(game.Title), Quote(game.Platform), Quote(game.Genre), Quote(game.Region),
                    Quote(game.Language),
                    game.ReleaseYear?.ToString() ?? "", Quote(game.Status), game.Favorite ? "是" : "否",
                    game.PlayCount.ToString(), game.PlaySeconds.ToString(), Quote(game.LastPlayedDisplay),
                    game.FileSize.ToString(), Quote(game.Tags), Quote(game.CoverPath), Quote(game.RomPath),
                    Quote(game.Notes)));
            }

            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
            MaintenanceStatus.Text = $"已导出：{dialog.FileName}";
        }
        catch (Exception ex)
        {
            MaintenanceStatus.Text = $"导出失败：{ex.Message}";
        }
    }

    private static string Quote(string? value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";

    private void CleanMissing_Click(object sender, RoutedEventArgs e)
    {
        var missing = AppServices.GameRepo.ListAll().Where(g => !File.Exists(g.RomPath)).ToList();
        if (missing.Count == 0)
        {
            MaintenanceStatus.Text = "没有失效路径记录。";
            return;
        }

        var answer = MessageBox.Show($"发现 {missing.Count} 条记录的 ROM 文件不存在，移除这些记录？\n\n只删数据库记录，不动文件。",
            "清理失效记录", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        foreach (var game in missing) AppServices.GameRepo.Delete(game.Id);
        MaintenanceStatus.Text = $"已清理 {missing.Count} 条失效记录。";
    }
}

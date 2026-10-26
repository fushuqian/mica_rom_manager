using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MicaRomManager.Data;
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
        LoadLookups();
        LoadPlatforms();
        DataDirText.Text = AppServices.Paths.DataDir;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"版本 {version?.ToString(3) ?? "0.1.0"}";
    }

    private void LoadGeneral()
    {
        var importMode = AppServices.Settings.Get("library.import_mode");
        ImportModeCombo.SelectedIndex = importMode switch
        {
            "copy" => 1,
            "move" => 2,
            "link" => 0,
            _ => AppServices.Settings.GetBool("library.copy_on_import") ? 1 : 0,
        };
        LibraryRootBox.Text = PortablePath.Expand(AppServices.Settings.Get("library.root") ?? "");
        PortableModeCheck.IsChecked = AppServices.Settings.GetBool(PortablePath.SettingKey);

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

    private void LoadLookups()
    {
        FillList(RegionList, AppServices.Lookups.List(LookupRepository.Region));
        FillList(LanguageList, AppServices.Lookups.List(LookupRepository.Language));

        static void FillList(ListBox list, IReadOnlyList<string> values)
        {
            list.Items.Clear();
            foreach (var value in values) list.Items.Add(value);
        }
    }

    private void AddRegion_Click(object sender, RoutedEventArgs e) =>
        AddLookup(LookupRepository.Region, NewRegionBox, RegionList);

    private void AddLanguage_Click(object sender, RoutedEventArgs e) =>
        AddLookup(LookupRepository.Language, NewLanguageBox, LanguageList);

    private void RemoveRegion_Click(object sender, RoutedEventArgs e) =>
        RemoveLookup(LookupRepository.Region, RegionList);

    private void RemoveLanguage_Click(object sender, RoutedEventArgs e) =>
        RemoveLookup(LookupRepository.Language, LanguageList);

    private void AddLookup(string kind, TextBox input, ListBox list)
    {
        var value = input.Text.Trim();
        if (value.Length == 0) return;

        AppServices.Lookups.Add(kind, value);
        input.Text = "";
        LoadLookups();
        list.SelectedItem = value;
    }

    private void RemoveLookup(string kind, ListBox list)
    {
        if (list.SelectedItem is not string value) return;
        AppServices.Lookups.Remove(kind, value);
        LoadLookups();
    }

    private void LoadPlatforms(string? selectName = null)
    {
        var selected = selectName ?? (PlatformList.SelectedItem as PlatformSetting)?.Name;
        PlatformList.ItemsSource = AppServices.Platforms.ListAll();

        PlatformEmulatorCombo.Items.Clear();
        PlatformEmulatorCombo.Items.Add(new ComboBoxItem { Content = "（不指定）", Tag = null });
        foreach (var emulator in AppServices.EmulatorRepo.ListAll().Where(e => e.Enabled))
            PlatformEmulatorCombo.Items.Add(new ComboBoxItem { Content = emulator.Name, Tag = emulator.Id });

        if (PlatformList.ItemsSource is List<PlatformSetting> platforms && !string.IsNullOrEmpty(selected))
        {
            var match = platforms.FirstOrDefault(p => string.Equals(p.Name, selected, StringComparison.OrdinalIgnoreCase));
            if (match != null) PlatformList.SelectedItem = match;
        }
    }

    private void PlatformList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlatformList.SelectedItem is not PlatformSetting setting)
        {
            PlatformNameText.Text = "（未选择平台）";
            PlatformFolderBox.Text = "";
            return;
        }

        PlatformNameText.Text = setting.Name;
        PlatformFolderBox.Text = setting.Folder;
        PlatformEmulatorCombo.SelectedItem = PlatformEmulatorCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => Equals(item.Tag, setting.EmulatorId));
    }

    private void AddPlatform_Click(object sender, RoutedEventArgs e)
    {
        var name = NewPlatformBox.Text.Trim();
        if (name.Length == 0) return;

        AppServices.Platforms.Save(new PlatformSetting { Name = name, Folder = name });
        NewPlatformBox.Text = "";
        LoadPlatforms(name);
    }

    private void RemovePlatform_Click(object sender, RoutedEventArgs e)
    {
        if (PlatformList.SelectedItem is not PlatformSetting setting) return;

        var result = MessageBox.Show($"删除平台「{setting.Name}」的配置？\n（不影响已入库的游戏）", "删除平台",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        AppServices.Platforms.Delete(setting.Name);
        LoadPlatforms();
    }

    private void SavePlatform_Click(object sender, RoutedEventArgs e)
    {
        if (PlatformList.SelectedItem is not PlatformSetting setting)
        {
            MessageBox.Show("请先在左侧选择一个平台。", "平台设置", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        setting.EmulatorId = (PlatformEmulatorCombo.SelectedItem as ComboBoxItem)?.Tag as long?;
        setting.Folder = PlatformFolderBox.Text.Trim();
        AppServices.Platforms.Save(setting);
        MessageBox.Show($"平台「{setting.Name}」已保存。", "平台设置", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void ScanPlatformLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (PlatformList.SelectedItem is not PlatformSetting setting)
        {
            MessageBox.Show("请先在左侧选择一个平台。", "扫描平台库",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var libraryRoot = AppServices.LibraryRoot;
        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            MessageBox.Show("尚未配置库目录，请先到「常规」里设置库目录。", "扫描平台库",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var folderName = string.IsNullOrWhiteSpace(setting.Folder) ? setting.Name : setting.Folder;
        var dir = Path.Combine(libraryRoot, RomImporter.SanitizeFolderName(folderName));
        if (!Directory.Exists(dir))
        {
            MessageBox.Show($"该平台库目录不存在：\n{dir}", "扫描平台库",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ScanPlatformButton.IsEnabled = false;
        MaintenanceStatus.Text = "正在扫描并计算指纹…";

        try
        {
            var (scan, removed) = await Task.Run(() =>
            {
                var platformGames = AppServices.GameRepo.ListAll()
                    .Where(g => string.Equals(g.Platform, setting.Name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var cleaned = LibraryOrganizer.RemoveMissing(AppServices.GameRepo, platformGames);

                // 清理后重新读取，避免被删记录的路径/指纹继续参与去重
                var existingPaths = AppServices.GameRepo.ListAll().Select(g => g.RomPath).ToList();
                var existingHashes = AppServices.GameRepo.ListHashes();
                return (RomImporter.Scan(new[] { dir }, true, existingPaths, existingHashes), cleaned);
            });

            if (scan.Candidates.Count == 0)
            {
                MaintenanceStatus.Text = "";
                MessageBox.Show(
                    $"该平台库里没有需要补充的 ROM。\n已存在 {scan.DuplicateCount} 个，忽略 {scan.ExcludedCount} 个，"
                    + $"清理失效记录 {removed} 条。",
                    "扫描平台库", MessageBoxButton.OK, MessageBoxImage.Information);
                (Owner as MainWindow)?.RefreshGames();
                return;
            }

            foreach (var candidate in scan.Candidates) candidate.Platform = setting.Name;

            var preview = new ImportPreviewWindow(scan.Candidates) { Owner = this };
            if (preview.ShowDialog() != true)
            {
                MaintenanceStatus.Text = "";
                return;
            }

            var added = 0;
            var failed = 0;
            foreach (var candidate in scan.Candidates)
            {
                try
                {
                    AppServices.GameRepo.Insert(new Game
                    {
                        Title = candidate.Title.Trim(),
                        Platform = candidate.Platform,
                        RomPath = candidate.FilePath,
                        RomHash = candidate.RomHash,
                        FileSize = candidate.FileSize,
                        Status = GameStatus.NotPlayed,
                    });
                    added++;
                }
                catch
                {
                    failed++;
                }
            }

            MaintenanceStatus.Text = "";
            var message = $"已从该平台库补充 {added} 条" + (failed > 0 ? $"，失败 {failed} 条" : "")
                          + $"，清理失效记录 {removed} 条。";
            MessageBox.Show(message, "扫描平台库", MessageBoxButton.OK, MessageBoxImage.Information);

            (Owner as MainWindow)?.RefreshGames();
        }
        catch (Exception ex)
        {
            MaintenanceStatus.Text = "";
            MessageBox.Show($"扫描失败：{ex.Message}", "扫描平台库", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ScanPlatformButton.IsEnabled = true;
        }
    }

    private async void OrganizePlatform_Click(object sender, RoutedEventArgs e)
    {
        if (PlatformList.SelectedItem is not PlatformSetting setting)
        {
            MessageBox.Show("请先在左侧选择一个平台。", "整理平台库",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var libraryRoot = AppServices.LibraryRoot;
        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            MessageBox.Show("尚未配置库目录，请先到「常规」里设置库目录。", "整理平台库",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var folderName = string.IsNullOrWhiteSpace(setting.Folder) ? setting.Name : setting.Folder;
        var dir = Path.Combine(libraryRoot, RomImporter.SanitizeFolderName(folderName));
        if (!Directory.Exists(dir))
        {
            MessageBox.Show($"该平台库目录不存在：\n{dir}", "整理平台库",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var games = AppServices.GameRepo.ListAll()
            .Where(g => string.Equals(g.Platform, setting.Name, StringComparison.OrdinalIgnoreCase)
                        && g.Discs.Any(d => LibraryOrganizer.IsUnderDirectory(d, dir)))
            .ToList();

        if (games.Count == 0)
        {
            MessageBox.Show("该平台子库里没有可整理的已入库文件。", "整理平台库",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            $"将按「标题 (区域)(语言)」重命名该平台子库内的 ROM 文件，涉及 {games.Count} 条记录。\n"
            + "只改文件名和库内路径，不动文件内容，是否继续？",
            "整理平台库", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        ScanPlatformButton.IsEnabled = false;
        OrganizePlatformButton.IsEnabled = false;
        var originalHint = PlatformScanHint.Text;
        PlatformScanHint.Text = "正在整理文件…";

        try
        {
            var result = await Task.Run(() => LibraryOrganizer.Organize(AppServices.GameRepo, games, dir));

            var message = $"重命名 {result.Renamed} 个文件，跳过 {result.Skipped} 条，失败 {result.Failed} 个。";
            if (result.FailedFiles.Count > 0)
                message += "\n失败文件：" + string.Join("、", result.FailedFiles.Take(5))
                           + (result.FailedFiles.Count > 5 ? " 等" : "");

            MessageBox.Show(message, "整理平台库", MessageBoxButton.OK, MessageBoxImage.Information);
            (Owner as MainWindow)?.RefreshGames();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"整理失败：{ex.Message}", "整理平台库", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ScanPlatformButton.IsEnabled = true;
            OrganizePlatformButton.IsEnabled = true;
            PlatformScanHint.Text = originalHint;
        }
    }

    private async void HashMissing_Click(object sender, RoutedEventArgs e)
    {
        var games = AppServices.GameRepo.ListAll()
            .Where(g => string.IsNullOrEmpty(g.RomHash)
                        && !string.IsNullOrWhiteSpace(g.RomPath)
                        && File.Exists(g.RomPath))
            .ToList();

        if (games.Count == 0)
        {
            MessageBox.Show("没有需要补算指纹的记录。", "补算指纹",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MaintenanceStatus.Text = $"正在补算指纹（0/{games.Count}）…";
        var computed = 0;
        var skipped = 0;

        await Task.Run(() =>
        {
            for (var i = 0; i < games.Count; i++)
            {
                var hash = RomHasher.Compute(games[i].RomPath);
                if (hash.Length == 0)
                {
                    skipped++;
                }
                else
                {
                    AppServices.GameRepo.SetHash(games[i].Id, hash);
                    computed++;
                }

                var progress = i + 1;
                Dispatcher.BeginInvoke(() => MaintenanceStatus.Text = $"正在补算指纹（{progress}/{games.Count}）…");
            }
        });

        MaintenanceStatus.Text = $"指纹补算完成：{computed} 条，跳过 {skipped} 条。";
        MessageBox.Show(MaintenanceStatus.Text, "补算指纹", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveGeneral_Click(object sender, RoutedEventArgs e)
    {
        var mode = CurrentProxyMode();
        if (mode == "manual" && !NetworkClientProvider.TryParseProxyAddress(ProxyAddressBox.Text, out _, out _))
        {
            MessageBox.Show("手动代理地址格式不正确，应为 host:port，例如 127.0.0.1:7890。",
                "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var portable = PortableModeCheck.IsChecked == true;
        var previousRoot = PortablePath.Expand(AppServices.Settings.Get("library.root") ?? "");
        var libraryRoot = PortablePath.Expand(LibraryRootBox.Text.Trim());

        if (portable && string.IsNullOrWhiteSpace(libraryRoot))
        {
            MessageBox.Show("便携模式下请指定库目录（程序目录下的库文件夹，例如 game_library）。",
                "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(libraryRoot))
        {
            try
            {
                Directory.CreateDirectory(libraryRoot);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"库目录不可用：{ex.Message}", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        AppServices.Settings.Set(PortablePath.SettingKey, portable.ToString());

        var relocated = 0;
        if (!string.IsNullOrWhiteSpace(previousRoot) && !string.IsNullOrWhiteSpace(libraryRoot) &&
            !string.Equals(previousRoot, libraryRoot, StringComparison.OrdinalIgnoreCase))
        {
            relocated = AppServices.GameRepo.RelocateLibraryPaths(previousRoot, libraryRoot);
        }

        var converted = AppServices.GameRepo.NormalizePaths(portable);

        AppServices.Settings.Set("library.import_mode", ImportModeCombo.SelectedIndex switch
        {
            1 => "copy",
            2 => "move",
            _ => "link",
        });
        AppServices.Settings.Set("library.copy_on_import", (ImportModeCombo.SelectedIndex == 1).ToString());
        AppServices.Settings.Set("library.root", portable ? PortablePath.ToStored(libraryRoot, true) : libraryRoot);
        AppServices.Settings.Set(NetworkClientProvider.ModeKey, mode);
        AppServices.Settings.Set(NetworkClientProvider.AddressKey, ProxyAddressBox.Text.Trim());
        AppServices.RebuildNetwork();
        AppServices.EmulatorRepo.NormalizePaths();
        (Owner as MainWindow)?.RefreshGames();
        MessageBox.Show($"常规设置已保存。\n已重定位 {relocated} 条记录，已转换 {converted} 条游戏路径。",
            "设置", MessageBoxButton.OK, MessageBoxImage.Information);
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

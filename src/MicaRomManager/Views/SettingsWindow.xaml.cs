using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MicaRomManager.Data;
using MicaRomManager.Models;
using MicaRomManager.Services;
using MicaRomManager.Services.Ai;
using Microsoft.Win32;

namespace MicaRomManager.Views;

public partial class SettingsWindow : Window
{
    private List<Emulator> _emulators = new();
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _hashCts;

    public SettingsWindow()
    {
        InitializeComponent();
        WindowBackdrop.Apply(this, WindowBackdrop.BackdropKind.Acrylic);
        LoadGeneral();
        LoadAiSettings();
        LoadLookupKinds();
        LoadPlatforms();
        LoadPrompts();
        DataDirText.Text = AppServices.Paths.DataDir;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"版本 {version?.ToString(3) ?? "0.1.0"}";
    }

    private AiPromptDefinition? _currentPrompt;

    private void LoadPrompts()
    {
        PromptList.ItemsSource = AiPromptCatalog.Definitions;
        PromptList.DisplayMemberPath = "Name";
        PromptList.SelectedIndex = 0;
    }

    private void PromptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _currentPrompt = PromptList.SelectedItem as AiPromptDefinition;
        LoadPromptEditor();
    }

    private void LoadPromptEditor()
    {
        if (_currentPrompt == null) return;

        PromptTitleText.Text = _currentPrompt.Name;
        PromptDescText.Text = _currentPrompt.Description;
        PromptVarsText.Text = _currentPrompt.Variables.Length == 0
            ? ""
            : $"可用变量：{_currentPrompt.Variables}（写错或删掉的变量会原样保留）";
        PromptSystemBox.Text = AiPromptCatalog.SystemFor(_currentPrompt.Id);
        PromptUserBox.Text = AiPromptCatalog.UserFor(_currentPrompt.Id);

        var hasUserTemplate = _currentPrompt.DefaultUser.Length > 0;
        PromptUserLabel.Visibility = hasUserTemplate ? Visibility.Visible : Visibility.Collapsed;
        PromptUserBox.Visibility = hasUserTemplate ? Visibility.Visible : Visibility.Collapsed;
        PromptUserLabelRow.Height = hasUserTemplate ? GridLength.Auto : new GridLength(0);
        PromptUserBoxRow.Height = hasUserTemplate ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        PromptStatusText.Text = AiPromptCatalog.IsCustomized(_currentPrompt.Id)
            ? "已自定义。保存后下次 AI 调用生效"
            : "使用内置默认。保存后下次 AI 调用生效";
    }

    private void PromptSave_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPrompt == null) return;

        AiPromptCatalog.Save(_currentPrompt.Id, PromptSystemBox.Text, PromptUserBox.Text);
        PromptStatusText.Text = "已保存，下次 AI 调用生效";
    }

    private void PromptReset_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPrompt == null) return;

        AiPromptCatalog.Reset(_currentPrompt.Id);
        LoadPromptEditor();
        PromptStatusText.Text = "已恢复内置默认";
    }

    private void PromptResetAll_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show("把所有提示词都恢复为内置默认？", "提示词",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        foreach (var definition in AiPromptCatalog.Definitions)
            AiPromptCatalog.Reset(definition.Id);

        LoadPromptEditor();
        PromptStatusText.Text = "全部提示词已恢复默认";
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

    private LookupKind? _currentLookupKind;

    private void LoadLookupKinds()
    {
        LookupKindList.ItemsSource = LookupRepository.Kinds;
        LookupKindList.DisplayMemberPath = "Title";
        LookupKindList.SelectedIndex = 0;
    }

    private void LookupKindList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _currentLookupKind = LookupKindList.SelectedItem as LookupKind;
        NewLookupValueBox.Text = "";
        LoadLookupValues();
    }

    private void LoadLookupValues()
    {
        LookupValueList.Items.Clear();
        if (_currentLookupKind == null) return;

        foreach (var value in AppServices.Lookups.List(_currentLookupKind.Key))
            LookupValueList.Items.Add(value);
    }

    private void AddLookupValue_Click(object sender, RoutedEventArgs e)
    {
        if (_currentLookupKind == null) return;

        var result = AppServices.Lookups.Add(_currentLookupKind.Key, NewLookupValueBox.Text);
        if (!result.Success)
        {
            MessageBox.Show(result.Message, "字典库", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        NewLookupValueBox.Text = "";
        LoadLookupValues();
        if (_currentLookupKind.Key == LookupRepository.Platform) LoadPlatforms();
    }

    private void RenameLookupValue_Click(object sender, RoutedEventArgs e)
    {
        if (_currentLookupKind == null) return;
        if (LookupValueList.SelectedItem is not string oldValue)
        {
            MessageBox.Show("请先在右侧列表选中要重命名的值。", "字典库",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = AppServices.Lookups.Rename(_currentLookupKind.Key, oldValue, NewLookupValueBox.Text);
        MessageBox.Show(result.Message, "字典库", MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        if (!result.Success) return;

        NewLookupValueBox.Text = "";
        LoadLookupValues();
        if (_currentLookupKind.Key == LookupRepository.Platform) LoadPlatforms();
    }

    private void RemoveLookupValue_Click(object sender, RoutedEventArgs e)
    {
        if (_currentLookupKind == null) return;
        if (LookupValueList.SelectedItem is not string value)
        {
            MessageBox.Show("请先在右侧列表选中要删除的值。", "字典库",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = AppServices.Lookups.Delete(_currentLookupKind.Key, value);
        MessageBox.Show(result.Message, "字典库", MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        if (!result.Success) return;

        LoadLookupValues();
        if (_currentLookupKind.Key == LookupRepository.Platform) LoadPlatforms();
    }

    private void LoadPlatforms(string? selectName = null)
    {
        var selected = selectName ?? (PlatformList.SelectedItem as PlatformSetting)?.Name;
        PlatformList.ItemsSource = AppServices.Platforms.ListAll();

        PlatformEmulatorCombo.Items.Clear();
        PlatformEmulatorCombo.Items.Add(new ComboBoxItem { Content = "（不指定）", Tag = null });
        _emulators = AppServices.EmulatorRepo.ListAll().Where(e => e.Enabled).ToList();
        foreach (var emulator in _emulators)
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
            PlatformFormatsBox.Text = "";
            PlatformArgsBox.Text = "";
            return;
        }

        PlatformNameText.Text = setting.Name;
        PlatformFolderBox.Text = setting.Folder;
        PlatformFormatsBox.Text = setting.Formats;
        PlatformArgsBox.Text = setting.ArgsTemplate;
        PlatformEmulatorCombo.SelectedItem = PlatformEmulatorCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => Equals(item.Tag, setting.EmulatorId));

        RefreshCorePanel(setting);
    }

    private void PlatformEmulator_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PlatformList.SelectedItem is PlatformSetting setting) RefreshCorePanel(setting);
    }

    private Emulator? SelectedPlatformEmulator()
    {
        var emulatorId = (PlatformEmulatorCombo.SelectedItem as ComboBoxItem)?.Tag as long?;
        return emulatorId is long id ? _emulators.FirstOrDefault(e => e.Id == id) : null;
    }

    private static bool EmulatorNeedsCore(Emulator? emulator) =>
        emulator != null &&
        (emulator.Name.Contains("RetroArch", StringComparison.OrdinalIgnoreCase)
         || ArgsBuilder.RequiresCore(emulator.ArgsTemplate));

    /// <summary>只有需要核心的模拟器（RetroArch 等）才显示核心下拉。</summary>
    private void RefreshCorePanel(PlatformSetting? setting)
    {
        if (PlatformCorePanel == null || PlatformCoreCombo == null) return;

        PlatformCoreCombo.Items.Clear();
        PlatformCoreCombo.Items.Add(new ComboBoxItem { Content = "（使用模拟器自带核心）", Tag = "" });

        var emulator = SelectedPlatformEmulator();
        var supportsCore = EmulatorNeedsCore(emulator);

        PlatformCorePanel.Visibility = supportsCore ? Visibility.Visible : Visibility.Collapsed;
        if (!supportsCore) return;

        var cores = EmulatorCores.List(emulator!);
        foreach (var core in cores)
            PlatformCoreCombo.Items.Add(new ComboBoxItem { Content = core, Tag = core });

        var current = setting?.CorePath ?? "";
        if (current.Length > 0 && !cores.Contains(current, StringComparer.OrdinalIgnoreCase))
            PlatformCoreCombo.Items.Add(new ComboBoxItem { Content = $"{current}（当前配置）", Tag = current });

        PlatformCoreCombo.SelectedItem = PlatformCoreCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, current, StringComparison.OrdinalIgnoreCase))
            ?? PlatformCoreCombo.Items[0];
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
        setting.Formats = PlatformFormats.Normalize(PlatformFormatsBox.Text);
        setting.CorePath = (PlatformCoreCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        setting.ArgsTemplate = PlatformArgsBox.Text.Trim();
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

        var originalHint = PlatformScanHint.Text;
        var cancelled = false;
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        var progress = new Progress<RomImporter.ScanProgress>(value =>
        {
            if (_scanCts == null) return;
            PlatformScanHint.Text = $"正在扫描并计算指纹（{value.Processed}/{value.Total}）：{Path.GetFileName(value.FilePath)}";
            PlatformScanProgress.Value = value.Total == 0 ? 0 : value.Processed * 100d / value.Total;
        });

        ScanPlatformButton.IsEnabled = false;
        CancelScanButton.IsEnabled = true;
        PlatformScanProgress.Visibility = Visibility.Visible;
        PlatformScanProgress.Value = 0;
        MaintenanceStatus.Text = "正在扫描并计算指纹…";

        try
        {
            // 用界面当前输入值，避免"填了格式但没保存就扫描"时仍按旧值过滤
            var allowed = PlatformFormats.Parse(PlatformFormatsBox.Text);
            var (scan, removed) = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var platformGames = AppServices.GameRepo.ListAll()
                    .Where(g => string.Equals(g.Platform, setting.Name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var cleaned = LibraryOrganizer.RemoveMissing(AppServices.GameRepo, platformGames);

                // 清理后重新读取，避免被删记录的路径/指纹继续参与去重
                var existingPaths = AppServices.GameRepo.ListAll().Select(g => g.RomPath).ToList();
                var existingHashes = AppServices.GameRepo.ListHashes();
                return (RomImporter.Scan(new[] { dir }, true, existingPaths, existingHashes,
                    allowed.Count > 0 ? allowed : null, null, progress, token), cleaned);
            });

            if (scan.Candidates.Count == 0)
            {
                MaintenanceStatus.Text = "";
                var formatsText = PlatformFormats.Normalize(PlatformFormatsBox.Text);
                MessageBox.Show(
                    $"该平台库（{dir}）里没有需要补充的 ROM。\n"
                    + $"当前格式：{(formatsText.Length == 0 ? "不限制" : formatsText)}\n"
                    + $"已存在 {scan.DuplicateCount} 个，忽略 {scan.ExcludedCount} 个，"
                    + $"格式不匹配跳过 {scan.FormatSkippedCount} 个，清理失效记录 {removed} 条。\n\n"
                    + "若刚修改过格式，请先点「保存平台设置」再扫描。",
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
                          + (scan.FormatSkippedCount > 0 ? $"，格式不匹配跳过 {scan.FormatSkippedCount} 个" : "")
                          + $"，清理失效记录 {removed} 条。";
            MessageBox.Show(message, "扫描平台库", MessageBoxButton.OK, MessageBoxImage.Information);

            (Owner as MainWindow)?.RefreshGames();
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            MaintenanceStatus.Text = "";
            PlatformScanHint.Text = "已取消扫描，未新增任何记录。";
        }
        catch (Exception ex)
        {
            MaintenanceStatus.Text = "";
            MessageBox.Show($"扫描失败：{ex.Message}", "扫描平台库", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ScanPlatformButton.IsEnabled = true;
            CancelScanButton.IsEnabled = false;
            PlatformScanProgress.Visibility = Visibility.Collapsed;
            PlatformScanProgress.Value = 0;
            _scanCts?.Dispose();
            _scanCts = null;
            if (!cancelled) PlatformScanHint.Text = originalHint;
        }
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        CancelScanButton.IsEnabled = false;
        PlatformScanHint.Text = "正在取消扫描…";
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

        var computed = 0;
        var skipped = 0;
        _hashCts?.Dispose();
        _hashCts = new CancellationTokenSource();
        var token = _hashCts.Token;
        var progress = new Progress<(int Completed, int Total)>(value =>
        {
            if (_hashCts == null) return;
            MaintenanceStatus.Text = $"正在补算指纹（{value.Completed}/{value.Total}）…";
            MaintenanceProgress.Value = value.Total == 0 ? 0 : value.Completed * 100d / value.Total;
        });
        IProgress<(int Completed, int Total)> reporter = progress;

        CancelHashButton.IsEnabled = true;
        MaintenanceProgress.Visibility = Visibility.Visible;
        MaintenanceProgress.Value = 0;
        MaintenanceStatus.Text = $"正在补算指纹（0/{games.Count}）…";

        try
        {
            await Task.Run(() =>
            {
                for (var i = 0; i < games.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

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

                    reporter.Report((i + 1, games.Count));
                    token.ThrowIfCancellationRequested();
                }
            }, token);
        }
        catch (OperationCanceledException)
        {
            MaintenanceStatus.Text = $"已取消补算：已补算 {computed} 条，跳过 {skipped} 条。";
            (Owner as MainWindow)?.RefreshGames();
            return;
        }
        finally
        {
            CancelHashButton.IsEnabled = false;
            MaintenanceProgress.Visibility = Visibility.Collapsed;
            MaintenanceProgress.Value = 0;
            _hashCts?.Dispose();
            _hashCts = null;
        }

        MaintenanceStatus.Text = $"指纹补算完成：{computed} 条，跳过 {skipped} 条。";
        (Owner as MainWindow)?.RefreshGames();
        MessageBox.Show(MaintenanceStatus.Text, "补算指纹", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CancelHash_Click(object sender, RoutedEventArgs e)
    {
        _hashCts?.Cancel();
        CancelHashButton.IsEnabled = false;
        MaintenanceStatus.Text = "正在取消补算…";
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

        var answer = MessageBox.Show(
            "用所选备份覆盖当前数据库？\n\n恢复前会自动把当前库备份到 data\\backups\\；恢复后需要重启程序。",
            "恢复数据库", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        var outcome = AppServices.Db.RestoreFrom(dialog.FileName);
        MaintenanceStatus.Text = outcome.Success
            ? $"{outcome.Message}（恢复前备份：{outcome.PreRestoreBackupPath}）"
            : outcome.Message;

        if (!outcome.Success) return;

        MessageBox.Show("数据库已恢复，请关闭并重新启动程序。", "恢复数据库",
            MessageBoxButton.OK, MessageBoxImage.Information);
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
        var all = AppServices.GameRepo.ListAll();
        var health = LibraryHealth.Inspect(all);
        if (!health.CanCleanMissing)
        {
            var roots = string.Join("、", health.UnavailableRoots);
            MaintenanceStatus.Text = $"已中止清理：{roots} 未连接。";
            MessageBox.Show(
                $"这些盘当前未连接：{roots}\n\n"
                + "无法判断文件是否真的失效；为避免误删记录（进度、评分、元数据会一起丢失），已中止清理。\n"
                + "请插上盘后重试，或先到主界面点「刷新文件状态」确认。",
                "清理失效记录", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var missing = all.Where(g => !File.Exists(g.RomPath)).ToList();
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

    private bool _loadingAi;

    private void LoadAiSettings()
    {
        _loadingAi = true;
        var options = AiOptions.Load(AppServices.Settings);
        AiBaseUrlBox.Text = options.BaseUrl;
        AiApiKeyBox.Password = options.ApiKey;
        AiApiKeyPlainBox.Text = options.ApiKey;
        AiModelBox.Text = options.Model;
        AiTimeoutBox.Text = options.TimeoutSeconds.ToString();
        AiBatchLimitBox.Text = options.BatchLimit.ToString();
        AiPresetCombo.SelectedIndex = 0;

        var search = AiSearchOptions.Load(AppServices.Settings);
        AiSearchEnabledCheck.IsChecked = search.Enabled;
        AiSearchMaxBox.Text = search.MaxResults.ToString();
        _loadingAi = false;
    }

    private void AiPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingAi || AiBaseUrlBox == null || AiModelBox == null) return;

        var (url, model) = AiPresetCombo.SelectedIndex switch
        {
            1 => ("https://api.deepseek.com/v1", "deepseek-chat"),
            2 => ("https://api.moonshot.cn/v1", "moonshot-v1-8k"),
            3 => ("https://api.openai.com/v1", "gpt-4o-mini"),
            _ => ("", ""),
        };

        if (url.Length == 0) return;
        AiBaseUrlBox.Text = url;
        AiModelBox.Text = model;
    }

    private void AiShowKey_Changed(object sender, RoutedEventArgs e)
    {
        if (AiShowKeyCheck == null) return;

        if (AiShowKeyCheck.IsChecked == true)
        {
            AiApiKeyPlainBox.Text = AiApiKeyBox.Password;
            AiApiKeyPlainBox.Visibility = Visibility.Visible;
            AiApiKeyBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            AiApiKeyBox.Password = AiApiKeyPlainBox.Text;
            AiApiKeyPlainBox.Visibility = Visibility.Collapsed;
            AiApiKeyBox.Visibility = Visibility.Visible;
        }
    }

    private string CurrentAiKey() => AiShowKeyCheck.IsChecked == true
        ? AiApiKeyPlainBox.Text.Trim()
        : AiApiKeyBox.Password.Trim();

    private bool SaveAiSettings(bool showMessage)
    {
        var baseUrl = AiBaseUrlBox.Text.Trim().TrimEnd('/');
        if (baseUrl.Length > 0 &&
            (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
             || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            MessageBox.Show("BaseUrl 必须是 http:// 或 https:// 开头的完整地址。", "AI 设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(AiTimeoutBox.Text.Trim(), out var timeout) || timeout is < 10 or > 600)
        {
            MessageBox.Show("超时必须是 10–600 之间的整数秒。", "AI 设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(AiBatchLimitBox.Text.Trim(), out var limit) || limit is < 1 or > 200)
        {
            MessageBox.Show("单次批量上限必须是 1–200 之间的整数。", "AI 设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(AiSearchMaxBox.Text.Trim(), out var searchLimit) || searchLimit is < 1 or > 10)
        {
            MessageBox.Show("搜索结果条数必须是 1–10 之间的整数。", "AI 设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        AppServices.Settings.Set(AiOptions.BaseUrlKey, baseUrl);
        // Key 落库前用 DPAPI 加密，避免明文躺在 mica.db 里（目录还可能被同步盘带走）
        AppServices.Settings.Set(AiOptions.ApiKeyKey, SecretProtector.Protect(CurrentAiKey()));
        AppServices.Settings.Set(AiOptions.ModelKey, AiModelBox.Text.Trim());
        AppServices.Settings.Set(AiOptions.TimeoutKey, timeout.ToString());
        AppServices.Settings.Set(AiOptions.BatchLimitKey, limit.ToString());
        AppServices.Settings.Set(AiSearchOptions.EnabledKey, (AiSearchEnabledCheck.IsChecked == true).ToString());
        AppServices.Settings.Set(AiSearchOptions.MaxResultsKey, searchLimit.ToString());

        if (showMessage)
            MessageBox.Show("AI 设置已保存。", "AI 设置", MessageBoxButton.OK, MessageBoxImage.Information);

        return true;
    }

    private void SaveAi_Click(object sender, RoutedEventArgs e) => SaveAiSettings(true);

    private async void TestSearch_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveAiSettings(false)) return;

        if (!AppServices.Search.CanSearch)
        {
            AiTestText.Text = "搜索不可用：请确认已勾选联网搜索、BaseUrl 为 https://api.deepseek.com/v1、API Key 已填写。";
            return;
        }

        AiSearchTestButton.IsEnabled = false;
        AiTestText.Text = "正在测试搜索…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var result = await AppServices.Search.SearchAsync("超级银河战士 SNES 游戏 发售年份", 3, cts.Token);
            AiTestText.Text = !result.Success
                ? $"搜索失败：{result.Error}"
                : result.Results.Count == 0
                    ? "搜索正常，但没有返回结果。"
                    : $"搜索正常：{result.Results[0].Title}"
                      + (result.Results[0].SiteName.Length == 0 ? "" : $"（{result.Results[0].SiteName}）");
        }
        catch (Exception ex)
        {
            AiTestText.Text = $"搜索失败：{ex.Message}";
        }
        finally
        {
            AiSearchTestButton.IsEnabled = true;
        }
    }

    private async void TestAi_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveAiSettings(false)) return;

        var options = AiOptions.Load(AppServices.Settings);
        if (!options.IsConfigured)
        {
            AiTestText.Text = "请先填写 BaseUrl、API Key 与模型名。";
            return;
        }

        AiTestButton.IsEnabled = false;
        AiTestText.Text = "正在测试…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds + 5));
            var result = await AppServices.Ai.CompleteAsync("你是连接测试助手。", "只回复 OK", cts.Token);
            AiTestText.Text = result.Success
                ? $"连接正常（{result.Elapsed.TotalSeconds:0.0} 秒）：{AiJson.Snippet(result.Content, 60)}"
                : $"测试失败：{result.Error}";
        }
        catch (Exception ex)
        {
            AiTestText.Text = $"测试失败：{ex.Message}";
        }
        finally
        {
            AiTestButton.IsEnabled = true;
        }
    }
}

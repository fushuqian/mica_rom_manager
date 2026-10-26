using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MicaRomManager.Helpers;
using MicaRomManager.Models;
using MicaRomManager.Services;
using Microsoft.Win32;

namespace MicaRomManager.Views;

/// <summary>单页导入向导：添加来源 → 扫描预览 → 选择入库方式 → 导入。</summary>
public partial class ImportWizardWindow : Window, INotifyPropertyChanged
{
    public ObservableCollection<string> Sources { get; } = new();
    public ObservableCollection<ImportCandidate> Candidates { get; } = new();
    public ObservableCollection<string> Platforms { get; } = new(PlatformCatalog.Common);

    public ImportWizardResult? Result { get; private set; }

    private readonly List<string> _existingPaths;
    private readonly string _libraryRoot;
    private int _duplicates;
    private int _excluded;
    private bool _scanning;

    public string Summary =>
        Candidates.Count == 0
            ? Sources.Count == 0 ? "拖放或添加 ROM 文件 / 文件夹" : "没有可导入的 ROM"
            : $"共 {Candidates.Count} 个文件，其中 {Candidates.Count(c => c.NeedsSpecify)} 个需要指定平台；"
              + $"重复 {_duplicates} 个，忽略 {_excluded} 个";

    public string LibraryHint =>
        string.IsNullOrWhiteSpace(_libraryRoot)
            ? "尚未设置库目录：复制 / 移动不可用，请到「设置 → 常规」配置库目录。"
            : $"库目录：{_libraryRoot}";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ImportWizardWindow(
        IEnumerable<string> initialSources,
        IEnumerable<string> existingPaths,
        string libraryRoot,
        ImportMode defaultMode,
        bool recursive = true)
    {
        InitializeComponent();

        _existingPaths = existingPaths.ToList();
        _libraryRoot = libraryRoot;

        var hasLibrary = !string.IsNullOrWhiteSpace(_libraryRoot);

        CopyRadio.IsEnabled = hasLibrary;
        MoveRadio.IsEnabled = hasLibrary;
        LinkRadio.IsChecked = defaultMode == ImportMode.Link || !hasLibrary;
        CopyRadio.IsChecked = hasLibrary && defaultMode == ImportMode.Copy;
        MoveRadio.IsChecked = hasLibrary && defaultMode == ImportMode.Move;
        RecursiveCheck.IsChecked = recursive;

        foreach (var source in initialSources)
            if (!string.IsNullOrWhiteSpace(source)) Sources.Add(source);

        DataContext = this;
        Sources.CollectionChanged += (_, _) => ScheduleScan();
        ScheduleScan();
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private async void ScheduleScan()
    {
        if (_scanning) return;
        _scanning = true;

        try
        {
            await Task.Delay(120);
            while (true)
            {
                var snapshot = Sources.ToList();
                var recursive = RecursiveCheck.IsChecked == true;
                var scan = await Task.Run(() => RomImporter.Scan(snapshot, recursive, _existingPaths));

                if (!Sources.SequenceEqual(snapshot))
                {
                    await Task.Delay(120);
                    continue;
                }

                Candidates.Clear();
                foreach (var candidate in scan.Candidates)
                {
                    Candidates.Add(candidate);
                    if (!string.IsNullOrWhiteSpace(candidate.Platform)
                        && candidate.Platform != ExtensionMap.UnspecifiedPlatform
                        && !Platforms.Contains(candidate.Platform))
                    {
                        Platforms.Add(candidate.Platform);
                    }
                }

                _duplicates = scan.DuplicateCount;
                _excluded = scan.ExcludedCount;
                OnPropertyChanged(nameof(Summary));
                break;
            }
        }
        finally
        {
            _scanning = false;
        }
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 ROM 文件",
            Filter = ExtensionMap.FileDialogFilter,
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true) return;

        foreach (var file in dialog.FileNames) AddSource(file);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择 ROM 文件夹" };
        if (dialog.ShowDialog() != true) return;

        AddSource(dialog.FolderName);
    }

    private void AddSource(string path)
    {
        if (Sources.Contains(path)) return;
        Sources.Add(path);
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = SourceList.SelectedItems.Cast<string>().ToList();
        foreach (var item in selected) Sources.Remove(item);
    }

    private void ClearSources_Click(object sender, RoutedEventArgs e)
    {
        Sources.Clear();
        Candidates.Clear();
        _duplicates = 0;
        _excluded = 0;
        OnPropertyChanged(nameof(Summary));
    }

    private void ApplyToAll_Click(object sender, RoutedEventArgs e)
    {
        var platform = BulkPlatformCombo.Text?.Trim();
        if (string.IsNullOrWhiteSpace(platform)) return;

        foreach (var item in Candidates) item.Platform = platform;
        OnPropertyChanged(nameof(Summary));
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;

        foreach (var path in paths)
            if (File.Exists(path) || Directory.Exists(path))
                AddSource(path);
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        CandidateGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        CandidateGrid.CommitEdit(DataGridEditingUnit.Row, true);

        if (Candidates.Count == 0)
        {
            MessageBox.Show("还没有可导入的 ROM，请先添加文件或文件夹。", "导入向导",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var missing = Candidates.Count(c =>
            string.IsNullOrWhiteSpace(c.Title) || string.IsNullOrWhiteSpace(c.Platform));
        if (missing > 0)
        {
            MessageBox.Show($"还有 {missing} 条记录的标题或平台为空，请补充后再导入。", "导入向导",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var mode = MoveRadio.IsChecked == true ? ImportMode.Move
            : CopyRadio.IsChecked == true ? ImportMode.Copy
            : ImportMode.Link;

        if (mode != ImportMode.Link && string.IsNullOrWhiteSpace(_libraryRoot))
        {
            MessageBox.Show("复制 / 移动需要先设置库目录（设置 → 常规）。", "导入向导",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 等待进行中的扫描结束，避免把旧的候选带入
        while (_scanning) await Task.Delay(60);

        Result = new ImportWizardResult
        {
            Candidates = Candidates.ToList(),
            Mode = mode,
            Recursive = RecursiveCheck.IsChecked == true,
        };

        DialogResult = true;
    }
}

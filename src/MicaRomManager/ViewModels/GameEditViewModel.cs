using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaRomManager.Data;
using MicaRomManager.Helpers;
using MicaRomManager.Models;
using MicaRomManager.Services;
using Microsoft.Win32;

namespace MicaRomManager.ViewModels;

public partial class GameEditViewModel : ObservableObject
{
    public Game Model { get; }
    public bool IsNew { get; }
    public ObservableCollection<string> Platforms { get; } = new();
    public ObservableCollection<string> Regions { get; } = new();
    public ObservableCollection<string> Languages { get; } = new();
    public ObservableCollection<Emulator> Emulators { get; } = new();
    public IReadOnlyList<string> Statuses { get; } = GameStatus.All;
    public ObservableCollection<string> DiscItems { get; } = new();

    [ObservableProperty] private Emulator? selectedEmulator;
    [ObservableProperty] private int selectedDiscIndex;
    [ObservableProperty] private string error = "";

    private readonly List<string> _discs = new();

    public event Action<bool>? RequestClose;

    public GameEditViewModel(Game game, bool isNew)
    {
        Model = game;
        IsNew = isNew;

        foreach (var platform in PlatformCatalog.Common) Platforms.Add(platform);
        foreach (var platform in AppServices.GameRepo.ListAll()
                     .Select(g => g.Platform)
                     .Where(p => !string.IsNullOrWhiteSpace(p))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(p => p))
        {
            if (!Platforms.Contains(platform, StringComparer.OrdinalIgnoreCase)) Platforms.Add(platform);
        }

        foreach (var region in AppServices.Lookups.List(LookupRepository.Region)) Regions.Add(region);
        foreach (var language in AppServices.Lookups.List(LookupRepository.Language)) Languages.Add(language);

        foreach (var emulator in AppServices.EmulatorRepo.ListAll()) Emulators.Add(emulator);
        SelectedEmulator = Emulators.FirstOrDefault(e => e.Id == game.EmulatorId);

        _discs.AddRange(game.Discs);
        if (_discs.Count == 0 && !string.IsNullOrWhiteSpace(game.RomPath)) _discs.Add(game.RomPath);
        RefreshDiscItems();
        SelectedDiscIndex = _discs.Count > 0 ? 0 : -1;
    }

    private void RefreshDiscItems()
    {
        DiscItems.Clear();
        for (var i = 0; i < _discs.Count; i++)
        {
            var path = _discs[i];
            var missing = File.Exists(path) ? "" : "（文件缺失）";
            DiscItems.Add($"碟 {i + 1}：{Path.GetFileName(path)}{missing}");
        }
    }

    /// <summary>把碟片列表写回 Model（第一碟即 RomPath）。</summary>
    private void SyncDiscsToModel()
    {
        if (_discs.Count == 0)
        {
            Model.RomPath = "";
            Model.DiscPaths = "";
            return;
        }

        Model.RomPath = _discs[0];
        Model.DiscPaths = string.Join("\n", _discs.Skip(1));
        if (File.Exists(_discs[0])) Model.FileSize = new FileInfo(_discs[0]).Length;
    }

    [RelayCommand]
    private void AddDiscs()
    {
        var dialog = new OpenFileDialog
        {
            Title = "添加碟片",
            Filter = ExtensionMap.FileDialogFilter,
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true) return;

        foreach (var file in dialog.FileNames)
        {
            if (_discs.Contains(file, StringComparer.OrdinalIgnoreCase)) continue;
            _discs.Add(file);
        }

        RefreshDiscItems();
        SyncDiscsToModel();
    }

    [RelayCommand]
    private void RemoveDisc()
    {
        if (SelectedDiscIndex < 0 || SelectedDiscIndex >= _discs.Count) return;
        if (_discs.Count <= 1) return;

        var removed = SelectedDiscIndex;
        _discs.RemoveAt(removed);
        RefreshDiscItems();
        SyncDiscsToModel();
        SelectedDiscIndex = Math.Min(removed, _discs.Count - 1);
    }

    [RelayCommand]
    private void MoveDiscUp()
    {
        if (SelectedDiscIndex <= 0) return;

        var index = SelectedDiscIndex;
        (_discs[index - 1], _discs[index]) = (_discs[index], _discs[index - 1]);
        RefreshDiscItems();
        SyncDiscsToModel();
        SelectedDiscIndex = index - 1;
    }

    [RelayCommand]
    private void MoveDiscDown()
    {
        if (SelectedDiscIndex < 0 || SelectedDiscIndex >= _discs.Count - 1) return;

        var index = SelectedDiscIndex;
        (_discs[index + 1], _discs[index]) = (_discs[index], _discs[index + 1]);
        RefreshDiscItems();
        SyncDiscsToModel();
        SelectedDiscIndex = index + 1;
    }

    [RelayCommand]
    private void BrowseRom()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 ROM 文件",
            Filter = ExtensionMap.FileDialogFilter,
        };
        if (dialog.ShowDialog() != true) return;

        Model.RomPath = dialog.FileName;
        if (File.Exists(dialog.FileName))
        {
            Model.FileSize = new FileInfo(dialog.FileName).Length;
        }

        if (_discs.Count == 0) _discs.Add(dialog.FileName);
        else _discs[0] = dialog.FileName;
        RefreshDiscItems();
        SyncDiscsToModel();

        if (string.IsNullOrWhiteSpace(Model.Title) || Model.Title == "新建游戏")
        {
            Model.Title = Path.GetFileNameWithoutExtension(dialog.FileName);
        }

        if (string.IsNullOrWhiteSpace(Model.Platform) || Model.Platform == ExtensionMap.UnspecifiedPlatform)
        {
            var info = ExtensionMap.Detect(dialog.FileName);
            if (info.Platform != null) Model.Platform = info.Platform;
        }
    }

    [RelayCommand]
    private void BrowseCover()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择封面图片",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif|所有文件|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            Model.CoverPath = AppServices.Covers.Import(dialog.FileName);
            Error = "";
        }
        catch (Exception ex)
        {
            Error = $"设置封面失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearCover() => Model.CoverPath = "";

    [RelayCommand]
    private void Save()
    {
        Error = "";
        if (string.IsNullOrWhiteSpace(Model.Title))
        {
            Error = "标题不能为空。";
            return;
        }

        if (string.IsNullOrWhiteSpace(Model.RomPath))
        {
            Error = "ROM 路径不能为空。";
            return;
        }

        if (File.Exists(Model.RomPath))
        {
            Model.FileSize = new FileInfo(Model.RomPath).Length;
        }

        Model.Title = Model.Title.Trim();
        Model.EmulatorId = SelectedEmulator?.Id;
        Model.UpdatedAt = DateTime.Now;
        SyncDiscsToModel();

        try
        {
            if (IsNew)
            {
                AppServices.GameRepo.Insert(Model);
            }
            else
            {
                AppServices.GameRepo.Update(Model);
            }
        }
        catch (Exception ex)
        {
            Error = $"保存失败：{ex.Message}";
            return;
        }

        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}

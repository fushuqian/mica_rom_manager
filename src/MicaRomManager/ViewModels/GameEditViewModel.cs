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

    [ObservableProperty] private Emulator? selectedEmulator;
    [ObservableProperty] private string error = "";

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

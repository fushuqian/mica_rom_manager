using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MicaRomManager.Models;
using MicaRomManager.Services.Ai;
using MicaRomManager.Services.Scraping;
using MicaRomManager.ViewModels;

namespace MicaRomManager.Views;

/// <summary>右侧「详情 / 编辑」托盘：只读详情、就地编辑、单条 AI 刮削。</summary>
public partial class GameInspectorView : UserControl
{
    private Game? _game;
    private bool _loading;
    private bool _dirty;
    private bool _overwrite;
    private int _rating;
    private readonly List<(string Field, string Before, string After)> _lastDiff = new();

    public GameInspectorView()
    {
        InitializeComponent();
        ModeFillEmpty.IsChecked = true;
    }

    public bool IsEditing => EditPanel.Visibility == Visibility.Visible;

    public bool HasUnsavedChanges => _dirty;

    /// <summary>取消编辑时通知宿主关掉「编辑」开关。</summary>
    public event EventHandler? EditCanceled;

    /// <summary>保存成功后通知宿主刷新（筛选可能受字段变化影响）。</summary>
    public event EventHandler? Saved;

    private MainViewModel? Vm => DataContext as MainViewModel;

    public void Load(Game? game, bool edit)
    {
        _game = game;
        MultiPanel.Visibility = Visibility.Collapsed;
        _loading = true;
        try
        {
            FillRead(game);
            FillEdit(game);
        }
        finally
        {
            _loading = false;
        }

        SetDirty(false);
        ScrapeStatus.Text = "";
        ClearScrapeDiff();
        if (edit) BeginEdit();
        else EndEdit();
    }

    /// <summary>多选：显示汇总与批量入口，不进入单条编辑。</summary>
    public void LoadMulti(IReadOnlyList<Game> games)
    {
        _game = null;
        _loading = true;
        try
        {
            SetDirty(false);
            ScrapeStatus.Text = "";
            EndEdit();

            ReadPanel.Visibility = Visibility.Collapsed;
            ReadFooter.Visibility = Visibility.Collapsed;
            MultiPanel.Visibility = Visibility.Visible;

            HeaderText.Text = "已选";
            MultiCountText.Text = $"已选 {games.Count} 项";

            var total = games.Sum(g => g.FileSize);
            var platforms = games
                .Where(g => !string.IsNullOrWhiteSpace(g.Platform))
                .GroupBy(g => g.Platform)
                .OrderByDescending(group => group.Count())
                .Take(4)
                .Select(group => $"{group.Key} × {group.Count()}");
            var platformText = platforms.Any() ? string.Join("、", platforms) : "—";
            MultiSummaryText.Text = $"合计 {FormatSize(total)}；{platformText}";
        }
        finally
        {
            _loading = false;
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
    }

    public void BeginEdit()
    {
        if (_game == null)
        {
            EndEdit();
            return;
        }

        ReadPanel.Visibility = Visibility.Collapsed;
        ReadFooter.Visibility = Visibility.Collapsed;
        EditPanel.Visibility = Visibility.Visible;
        EditFooter.Visibility = Visibility.Visible;
        HeaderText.Text = "编辑";
        ApplyModeLabel();
        EditTitle.Focus();
        EditTitle.CaretIndex = EditTitle.Text.Length;
    }

    public void EndEdit()
    {
        EditPanel.Visibility = Visibility.Collapsed;
        EditFooter.Visibility = Visibility.Collapsed;
        ReadPanel.Visibility = Visibility.Visible;
        ReadFooter.Visibility = Visibility.Visible;
        HeaderText.Text = "详情";
    }

    private void FillRead(Game? game)
    {
        var hasGame = game != null;
        ReadEmpty.Visibility = hasGame ? Visibility.Collapsed : Visibility.Visible;
        ReadBody.Visibility = hasGame ? Visibility.Visible : Visibility.Collapsed;
        ReadFooter.IsEnabled = hasGame;
        if (game == null) return;

        ReadTitle.Text = game.Title;
        ReadSubtitle.Text = game.Subtitle;
        CoverImage.Source = LoadCover(game.CoverPath);

        ReadKvList.Children.Clear();
        AddKv("类型", game.Genre);
        AddKv("年份", game.ReleaseYear?.ToString() ?? "");
        AddKv("发行商", game.Publisher);
        AddKv("开发商", game.Developer);
        AddKv("评分", game.RatingStars);
        AddKv("状态", string.IsNullOrWhiteSpace(game.LastPlayedDisplay)
            ? game.Status
            : $"{game.Status} · {game.LastPlayedDisplay}");
        AddKv("大小", $"{game.FileSizeDisplay} · {game.Format}");
        AddKv("模拟器", game.EmulatorDisplay);
        AddKv("标签", game.Tags);
        AddKv("备注", game.Notes);
        AddKv("路径", game.RomPath);
    }

    private void AddKv(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        row.Children.Add(new TextBlock
        {
            Text = key,
            Width = 58,
            FontSize = 11.5,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 1, 0, 0),
        });
        row.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 196,
        });
        ReadKvList.Children.Add(row);
    }

    private void FillEdit(Game? game)
    {
        var vm = Vm;
        if (vm != null)
        {
            EditPlatform.ItemsSource = vm.Platforms;
            EditRegion.ItemsSource = vm.RegionFilters;
            EditLanguage.ItemsSource = vm.LanguageFilters;
            EditStatus.ItemsSource = vm.EditStatuses;
        }

        if (game == null)
        {
            EditTitle.Text = "";
            return;
        }

        EditTitle.Text = game.Title;
        EditYear.Text = game.ReleaseYear?.ToString() ?? "";
        EditPlatform.Text = game.Platform;
        EditGenre.Text = game.Genre;
        EditRegion.Text = game.Region;
        EditLanguage.Text = game.Language;
        EditPublisher.Text = game.Publisher;
        EditDeveloper.Text = game.Developer;
        EditStatus.Text = game.Status;
        _rating = game.Rating;
        UpdateRatingStars();
        EditTags.Text = game.Tags;
        EditNotes.Text = game.Notes;
    }

    private static BitmapImage? LoadCover(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>点星设置评分；再点同一颗清空。</summary>
    private void RatingStar_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || !int.TryParse(element.Tag as string, out var star)) return;

        _rating = _rating == star ? 0 : star;
        UpdateRatingStars();
        SetDirty(true);
        e.Handled = true;
    }

    private void UpdateRatingStars()
    {
        if (RatingStarsPanel == null) return;

        var filled = (Brush)FindResource("AccentBrush");
        var empty = (Brush)FindResource("ScrollThumbBrush");
        var index = 0;
        foreach (var child in RatingStarsPanel.Children)
        {
            if (child is not TextBlock star) continue;
            index++;
            var on = index <= _rating;
            star.Text = on ? "★" : "☆";
            star.Foreground = on ? filled : empty;
        }
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SetDirty(true);
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        DirtyBadge.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveCurrent();

    /// <summary>供宿主在关闭托盘前自动保存。</summary>
    public void SaveCurrent()
    {
        if (_game == null) return;

        _game.Title = EditTitle.Text.Trim();
        _game.ReleaseYear = int.TryParse(EditYear.Text.Trim(), out var year) ? year : null;
        _game.Platform = EditPlatform.Text.Trim();
        _game.Genre = EditGenre.Text.Trim();
        _game.Region = EditRegion.Text.Trim();
        _game.Language = EditLanguage.Text.Trim();
        _game.Publisher = EditPublisher.Text.Trim();
        _game.Developer = EditDeveloper.Text.Trim();
        _game.Status = (EditStatus.SelectedItem as string) ?? EditStatus.Text.Trim();
        if (_game.Status.Length == 0) _game.Status = GameStatus.NotPlayed;
        _game.Rating = Math.Clamp(_rating, 0, 5);
        _game.Tags = EditTags.Text.Trim();
        _game.Notes = EditNotes.Text;

        Vm?.SaveInline(_game);
        Vm?.RefreshFilterAfterEdit();

        SetDirty(false);
        FillRead(_game);
        EndEdit();
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        FillEdit(_game);
        SetDirty(false);
        FillRead(_game);
        EndEdit();
        EditCanceled?.Invoke(this, EventArgs.Empty);
    }

    private void ScrapeMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu == null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void ModeFillEmpty_Click(object sender, RoutedEventArgs e)
    {
        _overwrite = false;
        ApplyModeLabel();
    }

    private void ModeOverwrite_Click(object sender, RoutedEventArgs e)
    {
        _overwrite = true;
        ApplyModeLabel();
    }

    private void ApplyModeLabel()
    {
        ModeFillEmpty.IsChecked = !_overwrite;
        ModeOverwrite.IsChecked = _overwrite;
        ScrapeModeButton.ToolTip = _overwrite ? "覆盖已有" : "只补空字段";
    }

    private async void Scrape_Click(object sender, RoutedEventArgs e)
    {
        if (_game == null) return;

        ScrapeButton.IsEnabled = false;
        ScrapeStatus.Text = "刮削中…";
        try
        {
            var useAi = AppServices.Ai.Options.IsConfigured;
            var search = AiSearchOptions.Load(AppServices.Settings).Enabled;
            var service = new ScraperService();
            var results = await service.ScrapeAsync(
                new[] { _game }, useAi, search, _overwrite, null, CancellationToken.None);
            var result = results.FirstOrDefault();
            if (result == null)
            {
                ScrapeStatus.Text = "没有返回结果";
                return;
            }

            if (result.Error.Length > 0) ScrapeStatus.Text = $"AI 返回错误：{result.Error}";
            var filled = ApplyScrape(result);
            if (filled > 0)
            {
                SetDirty(true);
                var source = string.IsNullOrWhiteSpace(result.Source) ? "未知来源" : result.Source;
                ScrapeStatus.Text = $"已填入 {filled} 项 · 来源 {source}";
            }
            else if (result.Error.Length == 0)
            {
                ScrapeStatus.Text = "没有可补充的字段";
            }
        }
        catch (Exception ex)
        {
            ScrapeStatus.Text = $"刮削失败：{ex.Message}";
        }
        finally
        {
            ScrapeButton.IsEnabled = true;
        }
    }

    private int ApplyScrape(ScrapeResult result)
    {
        _lastDiff.Clear();
        var filled = 0;
        filled += SetText("标题", EditTitle, result.Title);
        filled += SetText("类型", EditGenre, result.Genre);
        filled += SetText("区域", EditRegion, result.Region);
        filled += SetText("语言", EditLanguage, result.Language);
        filled += SetText("发行商", EditPublisher, result.Publisher);
        filled += SetText("开发商", EditDeveloper, result.Developer);
        filled += SetText("备注", EditNotes, result.Summary);
        if (result.ReleaseYear is { } year && (_overwrite || string.IsNullOrWhiteSpace(EditYear.Text)))
        {
            var before = EditYear.Text;
            EditYear.Text = year.ToString();
            if (!string.Equals(before.Trim(), EditYear.Text, StringComparison.Ordinal))
            {
                _lastDiff.Add(("年份", before, EditYear.Text));
                filled++;
            }
        }

        RenderScrapeDiff();
        return filled;
    }

    private void ClearScrapeDiff()
    {
        _lastDiff.Clear();
        ScrapeDiffList.Children.Clear();
        ScrapeDiffPanel.Visibility = Visibility.Collapsed;
    }

    private void RenderScrapeDiff()
    {
        ScrapeDiffList.Children.Clear();
        if (_lastDiff.Count == 0)
        {
            ScrapeDiffPanel.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var (field, before, after) in _lastDiff)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            row.Children.Add(new TextBlock
            {
                Text = field, Width = 48, FontSize = 11,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
            });
            row.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(before) ? "—" : before,
                FontSize = 11.5, TextDecorations = TextDecorations.Strikethrough,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                MaxWidth = 90, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            row.Children.Add(new TextBlock
            {
                Text = " → ", FontSize = 11,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
            });
            row.Children.Add(new TextBlock
            {
                Text = after, FontSize = 11.5, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("AccentBrush"),
                MaxWidth = 130, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            ScrapeDiffList.Children.Add(row);
        }

        ScrapeDiffPanel.Visibility = Visibility.Visible;
    }

    private int SetText(string field, TextBox box, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        if (!_overwrite && !string.IsNullOrWhiteSpace(box.Text)) return 0;
        if (string.Equals(box.Text.Trim(), value.Trim(), StringComparison.Ordinal)) return 0;

        var before = box.Text.Trim();
        box.Text = value.Trim();
        _lastDiff.Add((field, before, box.Text));
        return 1;
    }

    private int SetText(string field, ComboBox box, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        if (!_overwrite && !string.IsNullOrWhiteSpace(box.Text)) return 0;
        if (string.Equals(box.Text.Trim(), value.Trim(), StringComparison.Ordinal)) return 0;

        var before = box.Text.Trim();
        box.Text = value.Trim();
        _lastDiff.Add((field, before, box.Text));
        return 1;
    }
}

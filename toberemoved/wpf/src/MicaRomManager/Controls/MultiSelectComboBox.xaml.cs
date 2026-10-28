using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MicaRomManager.Controls;

/// <summary>
/// 多选下拉（用 ContextMenu 承载，避开 DataGrid/Popup 的焦点冲突）。
/// Value 用逗号分隔存储，显示为顿号分隔。
/// </summary>
public partial class MultiSelectComboBox : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<string>), typeof(MultiSelectComboBox),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(MultiSelectComboBox),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty CanEditProperty =
        DependencyProperty.Register(nameof(CanEdit), typeof(bool), typeof(MultiSelectComboBox),
            new PropertyMetadata(true));

    /// <summary>菜单关闭后触发，宿主可据此落库。</summary>
    public event EventHandler? ValueCommitted;

    private List<CheckBox> _checks = new();
    private ContextMenu? _menu;

    public IEnumerable<string>? ItemsSource
    {
        get => (IEnumerable<string>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool CanEdit
    {
        get => (bool)GetValue(CanEditProperty);
        set => SetValue(CanEditProperty, value);
    }

    public MultiSelectComboBox()
    {
        InitializeComponent();
        RefreshDisplay();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MultiSelectComboBox)d).RefreshDisplay();

    private static string[] Split(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void RefreshDisplay()
    {
        var items = Split(Value);
        DisplayText.Text = items.Length == 0 ? "" : string.Join("、", items);
    }

    private void Box_PreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanEdit) return;

        // 吞掉事件，避免 DataGrid 把它当作单元格切换/提交。
        e.Handled = true;
        ShowMenu();
    }

    private void ShowMenu()
    {
        var selected = new HashSet<string>(Split(Value), StringComparer.OrdinalIgnoreCase);
        var options = new List<string>();
        if (ItemsSource != null) options.AddRange(ItemsSource);
        foreach (var extra in selected.Where(x => !options.Contains(x, StringComparer.OrdinalIgnoreCase)))
            options.Add(extra);

        _checks = new List<CheckBox>();

        var listPanel = new StackPanel();
        foreach (var option in options)
        {
            var check = new CheckBox
            {
                Content = option,
                IsChecked = selected.Contains(option),
                Margin = new Thickness(6, 3, 6, 3),
            };
            check.Checked += OnCheckChanged;
            check.Unchecked += OnCheckChanged;
            _checks.Add(check);
            listPanel.Children.Add(check);
        }
        if (options.Count == 0)
            listPanel.Children.Add(new TextBlock { Text = "（暂无选项）", Opacity = 0.6, Margin = new Thickness(6) });

        var scroll = new ScrollViewer
        {
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = listPanel,
        };

        var input = new TextBox { Width = 118 };
        var addButton = new Button { Content = "添加", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
        addButton.Click += (_, _) =>
        {
            var text = input.Text.Trim();
            if (text.Length == 0) return;

            if (ItemsSource is ObservableCollection<string> collection && !collection.Contains(text))
                collection.Add(text);

            if (_checks.All(c => !string.Equals(c.Content?.ToString(), text, StringComparison.OrdinalIgnoreCase)))
            {
                var check = new CheckBox
                {
                    Content = text,
                    IsChecked = true,
                    Margin = new Thickness(6, 3, 6, 3),
                };
                check.Checked += OnCheckChanged;
                check.Unchecked += OnCheckChanged;
                _checks.Add(check);
                listPanel.Children.Add(check);
            }

            input.Text = "";
            ApplyCheckedValues();
        };

        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 8, 6, 6) };
        addRow.Children.Add(input);
        addRow.Children.Add(addButton);

        var panel = new StackPanel { Width = 200 };
        panel.Children.Add(scroll);
        panel.Children.Add(addRow);

        _menu = new ContextMenu
        {
            PlacementTarget = Box,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
        };
        _menu.Items.Add(panel);
        _menu.Closed += (_, _) => ValueCommitted?.Invoke(this, EventArgs.Empty);
        _menu.IsOpen = true;

        input.Focus();
    }

    private void OnCheckChanged(object sender, RoutedEventArgs e) => ApplyCheckedValues();

    private void ApplyCheckedValues()
    {
        var values = _checks
            .Where(c => c.IsChecked == true)
            .Select(c => c.Content?.ToString())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        SetCurrentValue(ValueProperty, string.Join(",", values!));
        RefreshDisplay();
    }
}

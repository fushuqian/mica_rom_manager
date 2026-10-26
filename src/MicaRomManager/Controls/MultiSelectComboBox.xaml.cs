using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MicaRomManager.Controls;

/// <summary>多选下拉：Value 用逗号分隔存储，选项来自 ItemsSource，可现场追加。</summary>
public partial class MultiSelectComboBox : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<string>), typeof(MultiSelectComboBox),
            new PropertyMetadata(null, (_, _) => { }));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(MultiSelectComboBox),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

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

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MultiSelectComboBox)d).RefreshDisplay();

    public MultiSelectComboBox()
    {
        InitializeComponent();
        RefreshDisplay();
    }

    private static string[] Split(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void RefreshDisplay()
    {
        var items = Split(Value);
        DisplayText.Text = items.Length == 0 ? "" : string.Join("、", items);
        if (DropPopup.IsOpen) BuildOptions();
    }

    private void Box_PreviewDown(object sender, MouseButtonEventArgs e)
    {
        // 必须吞掉事件：否则 DataGrid 会把它当成"切换/提交单元格"，弹层随即被销毁。
        e.Handled = true;
        if (DropPopup.IsOpen) return;

        BuildOptions();
        DropPopup.IsOpen = true;
        NewBox.Focus();
    }

    private void Box_PreviewUp(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void BuildOptions()
    {
        OptionsPanel.Children.Clear();

        var selected = new HashSet<string>(Split(Value), StringComparer.OrdinalIgnoreCase);
        var options = new List<string>();
        if (ItemsSource != null) options.AddRange(ItemsSource);
        foreach (var extra in selected.Where(x => !options.Contains(x, StringComparer.OrdinalIgnoreCase)))
            options.Add(extra);

        if (options.Count == 0)
        {
            OptionsPanel.Children.Add(new TextBlock { Text = "（暂无选项）", Opacity = 0.6, Margin = new Thickness(2) });
            return;
        }

        foreach (var option in options)
        {
            var check = new CheckBox
            {
                Content = option,
                IsChecked = selected.Contains(option),
                Margin = new Thickness(2, 3, 2, 3),
            };
            check.Checked += OnCheckChanged;
            check.Unchecked += OnCheckChanged;
            OptionsPanel.Children.Add(check);
        }
    }

    private void OnCheckChanged(object sender, RoutedEventArgs e)
    {
        var values = OptionsPanel.Children.OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => c.Content?.ToString())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        SetCurrentValue(ValueProperty, string.Join(",", values!));
        DisplayText.Text = values.Count == 0 ? "" : string.Join("、", values!);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var text = NewBox.Text.Trim();
        if (text.Length == 0) return;

        if (ItemsSource is ObservableCollection<string> collection && !collection.Contains(text))
            collection.Add(text);

        var values = Split(Value).ToList();
        if (!values.Contains(text, StringComparer.OrdinalIgnoreCase)) values.Add(text);
        SetCurrentValue(ValueProperty, string.Join(",", values));
        DisplayText.Text = string.Join("、", values);

        NewBox.Text = "";
        BuildOptions();
    }
}


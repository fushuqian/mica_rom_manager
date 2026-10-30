using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MicaRomManager.Controls;

/// <summary>Win11 风格自绘标题栏：拖动、双击最大化、最小化/最大化/关闭。</summary>
public partial class FluentTitleBar : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(FluentTitleBar),
        new PropertyMetadata("", (d, e) => ((FluentTitleBar)d).TitleText.Text = (string)e.NewValue));

    private Window? _window;

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public FluentTitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window == null) return;

        _window.StateChanged -= OnWindowStateChanged;
        _window.StateChanged += OnWindowStateChanged;
        UpdateMaximizeButton();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window != null) _window.StateChanged -= OnWindowStateChanged;
        _window = null;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateMaximizeButton();

    private void UpdateMaximizeButton()
    {
        if (_window == null) return;

        var maximized = _window.WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_window == null) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                _window.DragMove();
            }
            catch
            {
                // 拖动过程中窗口状态变化会抛异常，忽略
            }
        }
    }

    private void ToggleMaximize()
    {
        if (_window == null) return;
        _window.WindowState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (_window != null) _window.WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => _window?.Close();
}

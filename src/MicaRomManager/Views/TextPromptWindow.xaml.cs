using System.Windows;

namespace MicaRomManager.Views;

/// <summary>只有一个输入框的小对话框（重命名等）。</summary>
public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string hint, string value)
    {
        InitializeComponent();

        TitleText.Text = title;
        HintText.Text = hint;
        ValueBox.Text = value;

        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text.Trim();

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

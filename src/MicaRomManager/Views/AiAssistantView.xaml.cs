using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MicaRomManager.Models;
using MicaRomManager.ViewModels;

namespace MicaRomManager.Views;

public partial class AiAssistantView : UserControl
{
    private AiAssistantViewModel? _viewModel;

    public event EventHandler? CloseRequested;

    public AiAssistantView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null) _viewModel.Messages.CollectionChanged -= OnMessagesChanged;

        _viewModel = DataContext as AiAssistantViewModel;
        if (_viewModel != null) _viewModel.Messages.CollectionChanged += OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => MessageScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AiChatMessage message } && _viewModel != null)
            _ = _viewModel.ConfirmAsync(message);
    }

    private void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AiChatMessage message } && _viewModel != null)
            _ = _viewModel.ShuffleAsync(message);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AiChatMessage message })
            _viewModel?.CancelPending(message);
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string text } || _viewModel == null) return;

        _viewModel.Input = text;
        InputBox.Focus();
        InputBox.CaretIndex = InputBox.Text.Length;
    }

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers == ModifierKeys.Shift) return;

        e.Handled = true;
        if (_viewModel?.SendCommand.CanExecute(null) == true)
            _viewModel.SendCommand.Execute(null);
    }

    public void FocusInput()
    {
        InputBox.Focus();
        InputBox.CaretIndex = InputBox.Text.Length;
    }
}

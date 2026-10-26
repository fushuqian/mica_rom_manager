using System.IO;
using System.Windows;
using MicaRomManager.Services;

namespace MicaRomManager.Views;

public sealed record ExeOption(string Display, string FullPath);

public partial class ExePickWindow : Window
{
    public List<ExeOption> Options { get; }
    private string? _picked;

    public ExePickWindow(IReadOnlyList<string> candidates, string installDirectory, string emulatorName)
    {
        InitializeComponent();
        Options = candidates.Select(path => new ExeOption(Path.GetRelativePath(installDirectory, path), path)).ToList();
        DataContext = this;

        var suggested = EmulatorInstaller.PickBestExe(candidates, emulatorName);
        ExeList.SelectedItem = Options.FirstOrDefault(option => option.FullPath == suggested) ?? Options.FirstOrDefault();
    }

    public string? Pick()
    {
        return ShowDialog() == true ? _picked : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ExeList.SelectedItem is not ExeOption option)
        {
            MessageBox.Show("请选择一个可执行文件。", "选择可执行文件", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _picked = option.FullPath;
        DialogResult = true;
    }
}

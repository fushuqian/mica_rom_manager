using System.IO;
using System.Windows;
using System.Windows.Threading;
using MicaRomManager.Themes;

namespace MicaRomManager;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "MicaRomManager.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Mica ROM Manager 已经在运行。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogError(args.ExceptionObject as Exception);

        ThemePalette.Apply();

        try
        {
            AppServices.Initialize();
        }
        catch (Exception ex)
        {
            LogError(ex);
            MessageBox.Show($"初始化失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);
        MessageBox.Show($"发生未处理的错误：{e.Exception.Message}\n\n详细信息已记录到日志。", "错误",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void LogError(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var dir = AppServices.Paths?.LogsDir ?? Path.Combine(AppContext.BaseDirectory, "data", "logs");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"error-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(file, $"[{DateTime.Now:O}]{Environment.NewLine}{ex}");
        }
        catch
        {
            // 日志失败时不覆盖原始错误
        }
    }
}

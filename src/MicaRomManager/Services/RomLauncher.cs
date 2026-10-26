using System.Diagnostics;
using System.IO;
using MicaRomManager.Data;
using MicaRomManager.Models;

namespace MicaRomManager.Services;

public sealed class RomLauncher
{
    private readonly GameRepository _games;

    public RomLauncher(GameRepository games) => _games = games;

    /// <summary>游戏进程退出后触发：(游戏, 本次秒数)。在后台线程触发。</summary>
    public event Action<Game, long>? SessionEnded;

    public bool TryLaunch(Game game, Emulator? emulator, out string message)
    {
        message = "";
        if (string.IsNullOrWhiteSpace(game.RomPath) || !File.Exists(game.RomPath))
        {
            message = "ROM 文件不存在，请检查路径或刷新文件状态。";
            return false;
        }

        if (emulator == null)
        {
            message = $"还没有为平台「{game.Platform}」配置模拟器。请到设置 → 模拟器管理中安装或绑定。";
            return false;
        }

        var exePath = EmulatorPaths.ResolveExe(emulator);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            message = $"模拟器「{emulator.Name}」的可执行文件不存在，请在模拟器管理中检查配置。";
            return false;
        }

        var corePath = EmulatorPaths.ResolveCore(emulator);
        if (ArgsBuilder.RequiresCore(emulator.ArgsTemplate) && string.IsNullOrWhiteSpace(corePath))
        {
            message = $"模拟器「{emulator.Name}」的启动参数需要核心文件，但核心路径未配置。";
            return false;
        }

        try
        {
            var arguments = ArgsBuilder.Build(emulator.ArgsTemplate, game.RomPath, corePath, EmulatorPaths.ResolveConfig(emulator));
            var startInfo = new ProcessStartInfo(exePath)
            {
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
            };

            var process = Process.Start(startInfo);
            if (process == null)
            {
                message = "启动进程失败。";
                return false;
            }

            TrackSession(process, game);
            return true;
        }
        catch (Exception ex)
        {
            message = $"启动失败：{ex.Message}";
            return false;
        }
    }

    private void TrackSession(Process process, Game game)
    {
        _ = Task.Run(() =>
        {
            var startedAt = DateTime.Now;
            try
            {
                process.WaitForExit();
            }
            catch
            {
                // 进程信息不可用时仍然记录一次启动
            }

            var seconds = (long)Math.Max(0, (DateTime.Now - startedAt).TotalSeconds);
            try
            {
                _games.AddPlayStats(game.Id, seconds);
            }
            catch
            {
                // 统计写入失败不影响使用
            }

            SessionEnded?.Invoke(game, seconds);
        });
    }
}

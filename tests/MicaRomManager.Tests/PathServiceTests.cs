using System.IO;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class PathServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-paths-" + Guid.NewGuid().ToString("N"));

    public PathServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // 清理失败不影响测试结果
        }
    }

    [Fact]
    public void PortableMode_UsesDataFolderNextToProgram()
    {
        var paths = new PathService(_dir);

        Assert.Equal(Path.Combine(_dir, "data"), paths.DataDir);
        Assert.Equal(Path.Combine(paths.DataDir, "mica.db"), paths.DbPath);
    }

    [Fact]
    public void Constructor_CreatesAllRuntimeDirectories()
    {
        var paths = new PathService(_dir);

        foreach (var directory in new[]
                 {
                     paths.DataDir, paths.CoversDir, paths.EmulatorsDir, paths.ExportsDir,
                     paths.LogsDir, paths.DownloadsDir, paths.BackupsDir, paths.DevicesDir,
                 })
        {
            Assert.True(Directory.Exists(directory), directory);
        }

        Assert.Equal(Path.Combine(paths.DataDir, "ai-chat.json"), paths.AiChatPath);
    }
}

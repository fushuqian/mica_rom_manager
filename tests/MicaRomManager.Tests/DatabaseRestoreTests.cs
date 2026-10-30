using System.IO;
using MicaRomManager.Data;
using MicaRomManager.Models;
using Xunit;

namespace MicaRomManager.Tests;

public class DatabaseRestoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-restore-" + Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly Database _db;

    public DatabaseRestoreTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "mica.db");
        _db = new Database(_dbPath);
    }

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

    private void AddGame(string title) =>
        new GameRepository(_db).Insert(new Game { Title = title, RomPath = Path.Combine(_dir, title + ".chd") });

    [Fact]
    public void RestoreFrom_BringsBackBackedUpRowsAndKeepsPreRestoreCopy()
    {
        AddGame("first");
        var backup = Path.Combine(_dir, "backup.db");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Copy(_dbPath, backup);

        AddGame("second");
        Assert.Equal(2, new GameRepository(_db).ListAll().Count);

        var outcome = _db.RestoreFrom(backup);

        Assert.True(outcome.Success, outcome.Message);
        Assert.True(File.Exists(outcome.PreRestoreBackupPath));
        Assert.Single(new GameRepository(_db).ListAll());
    }

    [Fact]
    public void RestoreFrom_RejectsMissingFile()
    {
        var outcome = _db.RestoreFrom(Path.Combine(_dir, "nope.db"));

        Assert.False(outcome.Success);
        Assert.Contains("不存在", outcome.Message);
    }

    [Fact]
    public void RestoreFrom_RollsBackWhenBackupIsCorrupt()
    {
        AddGame("keep-me");
        var broken = Path.Combine(_dir, "broken.db");
        File.WriteAllText(broken, "this is definitely not a sqlite database");

        var outcome = _db.RestoreFrom(broken);

        Assert.False(outcome.Success);
        Assert.Contains("回滚", outcome.Message);
        Assert.Single(new GameRepository(_db).ListAll());
    }
}

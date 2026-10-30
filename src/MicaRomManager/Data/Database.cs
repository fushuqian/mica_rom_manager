using System.IO;
using MicaRomManager.Helpers;
using MicaRomManager.Services;
using Microsoft.Data.Sqlite;

namespace MicaRomManager.Data;

/// <summary>数据库恢复结果；PreRestoreBackupPath 是恢复前自动备份的路径。</summary>
public sealed record RestoreOutcome(bool Success, string Message, string PreRestoreBackupPath);

public sealed class Database
{
    public string DbPath { get; }

    public Database(string dbPath)
    {
        DbPath = dbPath;
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        Initialize();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 3000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    /// <summary>
    /// 用备份文件替换当前库：先把现有库自动备份，清空连接池后覆盖，再做完整性校验；
    /// 校验失败自动回滚到恢复前的库，避免把程序变成一个打不开的空壳。
    /// </summary>
    public RestoreOutcome RestoreFrom(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
            return new RestoreOutcome(false, "备份文件不存在。", "");

        var preRestore = "";
        try
        {
            var backupsDir = Path.Combine(Path.GetDirectoryName(DbPath) ?? ".", "backups");
            Directory.CreateDirectory(backupsDir);
            preRestore = Path.Combine(backupsDir, $"mica-pre-restore-{DateTime.Now:yyyyMMdd-HHmmss}.db");
            File.Copy(DbPath, preRestore, true);
        }
        catch (Exception ex)
        {
            return new RestoreOutcome(false, $"备份当前库失败，已中止恢复：{ex.Message}", "");
        }

        // 连接池里还握着旧文件的句柄，必须先清干净，否则复制会被占用或读到旧页。
        SqliteConnection.ClearAllPools();

        try
        {
            File.Copy(backupPath, DbPath, true);
            if (!QuickCheck(out var detail))
            {
                Rollback(preRestore);
                return new RestoreOutcome(false, $"备份库校验未通过（{detail}），已回滚到恢复前的库。", preRestore);
            }

            return new RestoreOutcome(true, "数据库已恢复，请重启程序生效。", preRestore);
        }
        catch (Exception ex)
        {
            Rollback(preRestore);
            return new RestoreOutcome(false, $"恢复失败：{ex.Message}（已回滚到恢复前的库）", preRestore);
        }
    }

    /// <summary>恢复失败时用自动备份覆盖回去；失败也不抛，交给上层提示。</summary>
    private void Rollback(string preRestore)
    {
        if (string.IsNullOrWhiteSpace(preRestore) || !File.Exists(preRestore)) return;

        try
        {
            SqliteConnection.ClearAllPools();
            File.Copy(preRestore, DbPath, true);
        }
        catch
        {
            // 回滚失败时保留自动备份文件，供用户手动处理
        }
    }

    private bool QuickCheck(out string detail)
    {
        try
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check;";
            var result = cmd.ExecuteScalar() as string ?? "";
            detail = result;
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    private void Initialize()
    {
        using var connection = Open();
        var version = 0;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version;";
            version = Convert.ToInt32(cmd.ExecuteScalar());
        }

        if (version < 1)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = SchemaV1 + " PRAGMA user_version = 1;";
            cmd.ExecuteNonQuery();
        }

        if (version < 2)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = SchemaV2;
            cmd.ExecuteNonQuery();
        }

        if (version < 3)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = SchemaV3;
            cmd.ExecuteNonQuery();
        }

        if (version < 4)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = SchemaV4;
            cmd.ExecuteNonQuery();
        }

        if (version < 5)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = SchemaV5;
                cmd.ExecuteNonQuery();
            }

            foreach (var platform in PlatformCatalog.Common)
            {
                using var seed = connection.CreateCommand();
                seed.CommandText = "INSERT OR IGNORE INTO platforms(name, folder) VALUES (@name, @name);";
                seed.Parameters.AddWithValue("@name", platform);
                seed.ExecuteNonQuery();
            }

            using var versionCmd = connection.CreateCommand();
            versionCmd.CommandText = "PRAGMA user_version = 5;";
            versionCmd.ExecuteNonQuery();
        }

        if (version < 6)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = SchemaV6;
            cmd.ExecuteNonQuery();
        }

        if (version < 7)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = SchemaV7;
                cmd.ExecuteNonQuery();
            }

            foreach (var pair in PlatformFormats.Defaults)
            {
                using var seed = connection.CreateCommand();
                seed.CommandText = """
                    UPDATE platforms SET formats=@formats
                    WHERE name=@name AND (formats IS NULL OR formats='');
                    """;
                seed.Parameters.AddWithValue("@formats", string.Join(" ", pair.Value));
                seed.Parameters.AddWithValue("@name", pair.Key);
                seed.ExecuteNonQuery();
            }
        }

        if (version < 8)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = SchemaV8;
            cmd.ExecuteNonQuery();
        }

        if (version < 9)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = SchemaV9;
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    UPDATE platforms SET args_template=(
                        SELECT e.args_template FROM emulators e WHERE e.id=platforms.emulator_id
                    ) WHERE args_template='' AND emulator_id IS NOT NULL;

                    UPDATE emulators SET website_url='https://github.com/'||github_repo||'/releases'
                    WHERE website_url='' AND github_repo<>'';

                    INSERT OR IGNORE INTO lookup_items(kind, value) SELECT 'platform', name FROM platforms;
                    INSERT OR IGNORE INTO lookup_items(kind, value) SELECT 'genre', genre FROM games WHERE genre<>'';
                    INSERT OR IGNORE INTO lookup_items(kind, value) SELECT 'publisher', publisher FROM games WHERE publisher<>'';
                    INSERT OR IGNORE INTO lookup_items(kind, value) SELECT 'developer', developer FROM games WHERE developer<>'';
                    """;
                cmd.ExecuteNonQuery();
            }

            SeedTags(connection);
        }

        if (version < 10)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = SchemaV10;
            cmd.ExecuteNonQuery();
        }

        if (version < 11)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = SchemaV11;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>把库里已有的标签拆成独立词条写入字典。</summary>
    private static void SeedTags(SqliteConnection connection)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT tags FROM games WHERE tags<>'';";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                foreach (var tag in reader.GetString(0).Split(
                             new[] { ',', '，', ';', '；' },
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    tags.Add(tag);
                }
            }
        }

        foreach (var tag in tags)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT OR IGNORE INTO lookup_items(kind, value) VALUES ('tag', @value);";
            insert.Parameters.AddWithValue("@value", tag);
            insert.ExecuteNonQuery();
        }
    }

    private const string SchemaV11 = """
        ALTER TABLE games ADD COLUMN special_note TEXT NOT NULL DEFAULT '';
        PRAGMA user_version = 11;
        """;

    private const string SchemaV10 = """
        ALTER TABLE games ADD COLUMN rating INTEGER NOT NULL DEFAULT 0;
        UPDATE games SET status = '在玩' WHERE play_count > 0 AND status = '未玩';
        PRAGMA user_version = 10;
        """;

    private const string SchemaV9 = """
        ALTER TABLE platforms ADD COLUMN args_template TEXT DEFAULT '';
        ALTER TABLE emulators ADD COLUMN website_url TEXT DEFAULT '';
        PRAGMA user_version = 9;
        """;

    private const string SchemaV8 = """
        ALTER TABLE platforms ADD COLUMN core_path TEXT DEFAULT '';
        PRAGMA user_version = 8;
        """;

    private const string SchemaV7 = """
        ALTER TABLE platforms ADD COLUMN formats TEXT DEFAULT '';
        PRAGMA user_version = 7;
        """;

    private const string SchemaV6 = """
        ALTER TABLE games ADD COLUMN rom_hash TEXT DEFAULT '';
        PRAGMA user_version = 6;
        """;

    private const string SchemaV5 = """
        CREATE TABLE IF NOT EXISTS platforms (
            name TEXT PRIMARY KEY,
            emulator_id INTEGER,
            folder TEXT NOT NULL DEFAULT '',
            updated_at TEXT NOT NULL DEFAULT ''
        );
        """;

    private const string SchemaV4 = """
        ALTER TABLE games ADD COLUMN disc_paths TEXT DEFAULT '';
        PRAGMA user_version = 4;
        """;

    private const string SchemaV3 = """
        CREATE TABLE IF NOT EXISTS lookup_items (
            kind TEXT NOT NULL,
            value TEXT NOT NULL,
            PRIMARY KEY (kind, value)
        );

        INSERT OR IGNORE INTO lookup_items(kind, value) VALUES
            ('region', '日版'), ('region', '美版'), ('region', '欧版'), ('region', '国行'),
            ('region', '港版'), ('region', '台版'), ('region', '韩版'), ('region', '汉化'),
            ('region', '世界版'),
            ('language', '简体中文'), ('language', '繁体中文'), ('language', '英语'),
            ('language', '日语'), ('language', '韩语'), ('language', '法语'),
            ('language', '德语'), ('language', '西班牙语'), ('language', '意大利语'),
            ('language', '俄语'), ('language', '葡萄牙语'), ('language', '多语言');

        PRAGMA user_version = 3;
        """;

    private const string SchemaV2 = """
        ALTER TABLE games ADD COLUMN language TEXT DEFAULT '';
        PRAGMA user_version = 2;
        """;

    private const string SchemaV1 = """
        CREATE TABLE IF NOT EXISTS games (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            title TEXT NOT NULL,
            platform TEXT NOT NULL DEFAULT '',
            genre TEXT DEFAULT '',
            region TEXT DEFAULT '',
            release_year INTEGER,
            publisher TEXT DEFAULT '',
            developer TEXT DEFAULT '',
            favorite INTEGER NOT NULL DEFAULT 0,
            status TEXT NOT NULL DEFAULT '未玩',
            rom_path TEXT NOT NULL UNIQUE,
            file_size INTEGER NOT NULL DEFAULT 0,
            cover_path TEXT DEFAULT '',
            notes TEXT DEFAULT '',
            tags TEXT DEFAULT '',
            emulator_id INTEGER,
            play_count INTEGER NOT NULL DEFAULT 0,
            play_seconds INTEGER NOT NULL DEFAULT 0,
            last_played_at TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS emulators (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE,
            exe_path TEXT NOT NULL DEFAULT '',
            args_template TEXT NOT NULL DEFAULT '"{rom}"',
            core_path TEXT DEFAULT '',
            supported_ext TEXT DEFAULT '',
            version TEXT DEFAULT '',
            install_dir TEXT DEFAULT '',
            config_path TEXT DEFAULT '',
            github_repo TEXT DEFAULT '',
            notes TEXT DEFAULT '',
            enabled INTEGER NOT NULL DEFAULT 1,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS emulator_platforms (
            emulator_id INTEGER NOT NULL,
            platform TEXT NOT NULL,
            is_default INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (emulator_id, platform)
        );

        CREATE TABLE IF NOT EXISTS settings (
            key TEXT PRIMARY KEY,
            value TEXT
        );
        """;
}

using System.IO;
using Microsoft.Data.Sqlite;

namespace MicaRomManager.Data;

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
    }

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

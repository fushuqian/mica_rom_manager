using Microsoft.Data.Sqlite;
using MicaRomManager.Models;

namespace MicaRomManager.Data;

public sealed class EmulatorRepository
{
    private const string Columns =
        "id,name,exe_path,args_template,core_path,supported_ext,version,install_dir,config_path,github_repo,notes,enabled";

    private readonly Database _db;

    public EmulatorRepository(Database db) => _db = db;

    public List<Emulator> ListAll()
    {
        var list = new List<Emulator>();
        using var connection = _db.Open();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"SELECT {Columns} FROM emulators ORDER BY name COLLATE NOCASE;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(Map(reader));
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT emulator_id, platform, is_default FROM emulator_platforms;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var emulator = list.FirstOrDefault(e => e.Id == reader.GetInt64(0));
                if (emulator == null) continue;
                var platform = reader.GetString(1);
                emulator.Platforms.Add(platform);
                if (reader.GetInt64(2) != 0) emulator.DefaultPlatforms.Add(platform);
            }
        }

        return list;
    }

    public Emulator? GetById(long id) => ListAll().FirstOrDefault(e => e.Id == id);

    public Emulator? GetDefaultForPlatform(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return null;
        foreach (var emulator in ListAll())
        {
            if (emulator.Enabled && emulator.DefaultPlatforms.Contains(platform)) return emulator;
        }

        return null;
    }

    public List<Emulator> ListForPlatform(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return new List<Emulator>();
        return ListAll()
            .Where(e => e.Enabled && e.Platforms.Any(p => string.Equals(p, platform, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public void Save(Emulator emulator)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        if (emulator.Id == 0)
        {
            cmd.CommandText = """
                INSERT INTO emulators
                    (name,exe_path,args_template,core_path,supported_ext,version,install_dir,config_path,github_repo,notes,enabled,created_at,updated_at)
                VALUES
                    (@name,@exe_path,@args_template,@core_path,@supported_ext,@version,@install_dir,@config_path,@github_repo,@notes,@enabled,@created_at,@updated_at);
                SELECT last_insert_rowid();
                """;
            AddParameters(cmd, emulator);
            emulator.Id = Convert.ToInt64(cmd.ExecuteScalar());
        }
        else
        {
            cmd.CommandText = """
                UPDATE emulators SET
                    name=@name, exe_path=@exe_path, args_template=@args_template, core_path=@core_path,
                    supported_ext=@supported_ext, version=@version, install_dir=@install_dir,
                    config_path=@config_path, github_repo=@github_repo, notes=@notes, enabled=@enabled,
                    updated_at=@updated_at
                WHERE id=@id;
                """;
            AddParameters(cmd, emulator);
            cmd.Parameters.AddWithValue("@id", emulator.Id);
            cmd.ExecuteNonQuery();
        }

        SavePlatforms(connection, emulator);
    }

    public void SavePlatforms(long emulatorId, IEnumerable<string> platforms, IEnumerable<string> defaults)
    {
        using var connection = _db.Open();
        SavePlatforms(connection, new Emulator
        {
            Id = emulatorId,
            Platforms = platforms.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            DefaultPlatforms = new HashSet<string>(defaults, StringComparer.OrdinalIgnoreCase),
        }, emulatorId);
    }

    private static void SavePlatforms(SqliteConnection connection, Emulator emulator, long? explicitId = null)
    {
        var id = explicitId ?? emulator.Id;
        using var transaction = connection.BeginTransaction();
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM emulator_platforms WHERE emulator_id=@id;";
            delete.Parameters.AddWithValue("@id", id);
            delete.ExecuteNonQuery();
        }

        foreach (var platform in emulator.Platforms.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO emulator_platforms (emulator_id, platform, is_default) VALUES (@id,@platform,@default);";
            insert.Parameters.AddWithValue("@id", id);
            insert.Parameters.AddWithValue("@platform", platform);
            insert.Parameters.AddWithValue("@default", emulator.DefaultPlatforms.Contains(platform) ? 1 : 0);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>把该模拟器设为其所有绑定平台的默认模拟器。</summary>
    public void SetAsDefaultForBoundPlatforms(long emulatorId)
    {
        using var connection = _db.Open();
        using var transaction = connection.BeginTransaction();
        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = """
                UPDATE emulator_platforms SET is_default=0
                WHERE platform IN (SELECT platform FROM emulator_platforms WHERE emulator_id=@id);
                """;
            clear.Parameters.AddWithValue("@id", emulatorId);
            clear.ExecuteNonQuery();
        }

        using (var set = connection.CreateCommand())
        {
            set.Transaction = transaction;
            set.CommandText = "UPDATE emulator_platforms SET is_default=1 WHERE emulator_id=@id;";
            set.Parameters.AddWithValue("@id", emulatorId);
            set.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void Delete(long emulatorId)
    {
        using var connection = _db.Open();
        using var transaction = connection.BeginTransaction();
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "DELETE FROM emulator_platforms WHERE emulator_id=@id;";
            cmd.Parameters.AddWithValue("@id", emulatorId);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "DELETE FROM emulators WHERE id=@id;";
            cmd.Parameters.AddWithValue("@id", emulatorId);
            cmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void AddParameters(SqliteCommand cmd, Emulator e)
    {
        cmd.Parameters.AddWithValue("@name", e.Name);
        cmd.Parameters.AddWithValue("@exe_path", e.ExePath);
        cmd.Parameters.AddWithValue("@args_template", e.ArgsTemplate);
        cmd.Parameters.AddWithValue("@core_path", e.CorePath);
        cmd.Parameters.AddWithValue("@supported_ext", e.SupportedExt);
        cmd.Parameters.AddWithValue("@version", e.Version);
        cmd.Parameters.AddWithValue("@install_dir", e.InstallDir);
        cmd.Parameters.AddWithValue("@config_path", e.ConfigPath);
        cmd.Parameters.AddWithValue("@github_repo", e.GithubRepo);
        cmd.Parameters.AddWithValue("@notes", e.Notes);
        cmd.Parameters.AddWithValue("@enabled", e.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@created_at", DateTime.Now.ToString("O"));
        cmd.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("O"));
    }

    private static Emulator Map(SqliteDataReader r)
    {
        return new Emulator
        {
            Id = r.GetInt64(0),
            Name = r.GetString(1),
            ExePath = r.GetString(2),
            ArgsTemplate = r.GetString(3),
            CorePath = r.GetString(4),
            SupportedExt = r.GetString(5),
            Version = r.GetString(6),
            InstallDir = r.GetString(7),
            ConfigPath = r.GetString(8),
            GithubRepo = r.GetString(9),
            Notes = r.GetString(10),
            Enabled = r.GetInt64(11) != 0,
        };
    }
}

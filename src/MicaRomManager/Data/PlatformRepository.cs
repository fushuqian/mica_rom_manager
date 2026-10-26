using Microsoft.Data.Sqlite;
using MicaRomManager.Helpers;
using MicaRomManager.Models;

namespace MicaRomManager.Data;

public sealed class PlatformRepository
{
    private readonly Database _db;

    public PlatformRepository(Database db) => _db = db;

    public List<PlatformSetting> ListAll()
    {
        var list = new List<PlatformSetting>();
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name, emulator_id, folder FROM platforms ORDER BY name COLLATE NOCASE;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            list.Add(new PlatformSetting
            {
                Name = name,
                EmulatorId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                Folder = reader.IsDBNull(2) ? "" : reader.GetString(2),
                IsCustom = !PlatformCatalog.Common.Contains(name, StringComparer.OrdinalIgnoreCase),
            });
        }

        return list;
    }

    public PlatformSetting? Get(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return null;

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name, emulator_id, folder FROM platforms WHERE name = @name;";
        cmd.Parameters.AddWithValue("@name", platform);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new PlatformSetting
        {
            Name = reader.GetString(0),
            EmulatorId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
            Folder = reader.IsDBNull(2) ? "" : reader.GetString(2),
            IsCustom = !PlatformCatalog.Common.Contains(reader.GetString(0), StringComparer.OrdinalIgnoreCase),
        };
    }

    public void Save(PlatformSetting setting)
    {
        if (string.IsNullOrWhiteSpace(setting.Name)) return;

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO platforms (name, emulator_id, folder, updated_at)
            VALUES (@name, @emulator_id, @folder, @updated_at)
            ON CONFLICT(name) DO UPDATE SET
                emulator_id=@emulator_id,
                folder=@folder,
                updated_at=@updated_at;
            """;
        cmd.Parameters.AddWithValue("@name", setting.Name.Trim());
        cmd.Parameters.AddWithValue("@emulator_id", (object?)setting.EmulatorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@folder", setting.Folder.Trim());
        cmd.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return;

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM platforms WHERE name=@name;";
        cmd.Parameters.AddWithValue("@name", platform);
        cmd.ExecuteNonQuery();
    }

    /// <summary>导入遇到平台未配置时补一行默认记录。</summary>
    public void EnsureExists(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return;

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO platforms (name, folder, updated_at)
            VALUES (@name, @name, @updated_at);
            """;
        cmd.Parameters.AddWithValue("@name", platform);
        cmd.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("O"));
        cmd.ExecuteNonQuery();
    }
}

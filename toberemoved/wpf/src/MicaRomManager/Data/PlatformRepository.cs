using Microsoft.Data.Sqlite;
using MicaRomManager.Helpers;
using MicaRomManager.Models;
using MicaRomManager.Services;

namespace MicaRomManager.Data;

public sealed class PlatformRepository
{
    private readonly Database _db;

    public PlatformRepository(Database db) => _db = db;

    public List<PlatformSetting> ListAll()
    {
        var names = new List<string>();
        using (var connection = _db.Open())
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM lookup_items WHERE kind='platform' ORDER BY value;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) names.Add(reader.GetString(0));
        }

        foreach (var name in names) EnsureExists(name);

        var map = new Dictionary<string, PlatformSetting>(StringComparer.OrdinalIgnoreCase);
        using (var connection = _db.Open())
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT name, emulator_id, folder, formats, core_path, args_template
                FROM platforms;
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                map[name] = new PlatformSetting
                {
                    Name = name,
                    EmulatorId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                    Folder = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Formats = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    CorePath = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    ArgsTemplate = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    IsCustom = !PlatformCatalog.Common.Contains(name, StringComparer.OrdinalIgnoreCase),
                };
            }
        }

        var list = names.Where(map.ContainsKey).Select(name => map[name]).ToList();

        // 字典尚未初始化时回退到平台配置表
        if (list.Count == 0) list.AddRange(map.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase));
        return list;
    }

    public PlatformSetting? Get(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return null;

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT name, emulator_id, folder, formats, core_path, args_template
            FROM platforms WHERE name = @name;
            """;
        cmd.Parameters.AddWithValue("@name", platform);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new PlatformSetting
        {
            Name = reader.GetString(0),
            EmulatorId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
            Folder = reader.IsDBNull(2) ? "" : reader.GetString(2),
            Formats = reader.IsDBNull(3) ? "" : reader.GetString(3),
            CorePath = reader.IsDBNull(4) ? "" : reader.GetString(4),
            ArgsTemplate = reader.IsDBNull(5) ? "" : reader.GetString(5),
            IsCustom = !PlatformCatalog.Common.Contains(reader.GetString(0), StringComparer.OrdinalIgnoreCase),
        };
    }

    public void Save(PlatformSetting setting)
    {
        if (string.IsNullOrWhiteSpace(setting.Name)) return;

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO platforms (name, emulator_id, folder, formats, core_path, args_template, updated_at)
            VALUES (@name, @emulator_id, @folder, @formats, @core_path, @args_template, @updated_at)
            ON CONFLICT(name) DO UPDATE SET
                emulator_id=@emulator_id,
                folder=@folder,
                formats=@formats,
                core_path=@core_path,
                args_template=@args_template,
                updated_at=@updated_at;
            """;
        cmd.Parameters.AddWithValue("@name", setting.Name.Trim());
        cmd.Parameters.AddWithValue("@emulator_id", (object?)setting.EmulatorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@folder", setting.Folder.Trim());
        cmd.Parameters.AddWithValue("@formats", PlatformFormats.Normalize(setting.Formats));
        cmd.Parameters.AddWithValue("@core_path", setting.CorePath.Trim());
        cmd.Parameters.AddWithValue("@args_template", setting.ArgsTemplate.Trim());
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
            INSERT OR IGNORE INTO platforms (name, folder, formats, args_template, updated_at)
            VALUES (@name, @name, @formats, '', @updated_at);
            """;
        cmd.Parameters.AddWithValue("@name", platform);
        cmd.Parameters.AddWithValue("@formats", PlatformFormats.GetDefault(platform));
        cmd.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("O"));
        cmd.ExecuteNonQuery();
    }
}

using Microsoft.Data.Sqlite;

namespace MicaRomManager.Data;

public sealed class SettingsRepository
{
    private readonly Database _db;

    public SettingsRepository(Database db) => _db = db;

    public string? Get(string key)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key=@key;";
        cmd.Parameters.AddWithValue("@key", key);
        return cmd.ExecuteScalar() as string;
    }

    public void Set(string key, string? value)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO settings (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value=@value;
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", (object?)value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public bool GetBool(string key, bool fallback = false) =>
        bool.TryParse(Get(key), out var value) ? value : fallback;
}

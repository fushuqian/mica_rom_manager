using Microsoft.Data.Sqlite;

namespace MicaRomManager.Data;

/// <summary>区域 / 语言等可维护的下拉词典。</summary>
public sealed class LookupRepository
{
    public const string Region = "region";
    public const string Language = "language";

    private readonly Database _db;

    public LookupRepository(Database db) => _db = db;

    public IReadOnlyList<string> List(string kind)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM lookup_items WHERE kind = $kind ORDER BY value;";
        cmd.Parameters.AddWithValue("$kind", kind);

        var result = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    public void Add(string kind, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return;

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO lookup_items(kind, value) VALUES ($kind, $value);";
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$value", trimmed);
        cmd.ExecuteNonQuery();
    }

    public void Remove(string kind, string value)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM lookup_items WHERE kind = $kind AND value = $value;";
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }
}

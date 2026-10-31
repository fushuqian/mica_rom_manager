using Microsoft.Data.Sqlite;
using MicaRomManager.Services;

namespace MicaRomManager.Data;

public sealed record LookupKind(string Key, string Title);

public sealed record LookupActionResult(bool Success, string Message, int Affected = 0);

/// <summary>可维护的下拉词典：区域、语言、平台、类型、发行商、开发商、标签。</summary>
public sealed class LookupRepository
{
    public const string Region = "region";
    public const string Language = "language";
    public const string Platform = "platform";
    public const string Genre = "genre";
    public const string Publisher = "publisher";
    public const string Developer = "developer";
    public const string Tag = "tag";

    public static IReadOnlyList<LookupKind> Kinds { get; } = new[]
    {
        new LookupKind(Region, "区域"),
        new LookupKind(Language, "语言"),
        new LookupKind(Genre, "类型"),
        new LookupKind(Publisher, "发行商"),
        new LookupKind(Developer, "开发商"),
        new LookupKind(Tag, "标签"),
    };

    private readonly Database _db;

    public LookupRepository(Database db) => _db = db;

    public IReadOnlyList<string> List(string kind)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM lookup_items WHERE kind = @kind ORDER BY value;";
        cmd.Parameters.AddWithValue("@kind", kind);

        var result = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    public LookupActionResult Add(string kind, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return new LookupActionResult(false, "值不能为空。");

        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO lookup_items(kind, value) VALUES (@kind, @value);";
        cmd.Parameters.AddWithValue("@kind", kind);
        cmd.Parameters.AddWithValue("@value", trimmed);
        if (cmd.ExecuteNonQuery() == 0) return new LookupActionResult(false, "该值已存在。");

        if (kind == Platform)
        {
            using var ensure = connection.CreateCommand();
            ensure.CommandText = """
                INSERT OR IGNORE INTO platforms (name, folder, formats, updated_at)
                VALUES (@name, @name, @formats, @updated_at);
                """;
            ensure.Parameters.AddWithValue("@name", trimmed);
            ensure.Parameters.AddWithValue("@formats", PlatformFormats.GetDefault(trimmed));
            ensure.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("O"));
            ensure.ExecuteNonQuery();
        }

        return new LookupActionResult(true, $"已添加「{trimmed}」。");
    }

    public LookupActionResult Rename(string kind, string oldValue, string newValue)
    {
        var newName = newValue.Trim();
        if (newName.Length == 0) return new LookupActionResult(false, "新值不能为空。");
        if (string.Equals(oldValue, newName, StringComparison.Ordinal)) return new LookupActionResult(true, "值未变化。");
        if (Exists(kind, newName)) return new LookupActionResult(false, "该值已存在。");

        using var connection = _db.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            var affected = kind switch
            {
                Tag => RenameTags(connection, transaction, oldValue, newName),
                Platform => RenamePlatform(connection, transaction, oldValue, newName),
                _ => RenameColumn(connection, transaction, kind, oldValue, newName),
            };

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "UPDATE lookup_items SET value=@new WHERE kind=@kind AND value=@old;";
                cmd.Parameters.AddWithValue("@new", newName);
                cmd.Parameters.AddWithValue("@kind", kind);
                cmd.Parameters.AddWithValue("@old", oldValue);
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
            return new LookupActionResult(true, $"已把「{oldValue}」改为「{newName}」，更新 {affected} 条记录。", affected);
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            return new LookupActionResult(false, $"改名失败：{ex.Message}");
        }
    }

    /// <summary>被游戏记录引用的值不能删除。</summary>
    public LookupActionResult Delete(string kind, string value)
    {
        var usage = CountUsage(kind, value);
        if (usage > 0)
            return new LookupActionResult(false, $"还有 {usage} 条记录在使用「{value}」，不能删除。");

        using var connection = _db.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM lookup_items WHERE kind=@kind AND value=@value;";
                cmd.Parameters.AddWithValue("@kind", kind);
                cmd.Parameters.AddWithValue("@value", value);
                cmd.ExecuteNonQuery();
            }

            if (kind == Platform)
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM platforms WHERE name=@name;";
                    cmd.Parameters.AddWithValue("@name", value);
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM emulator_platforms WHERE platform=@name;";
                    cmd.Parameters.AddWithValue("@name", value);
                    cmd.ExecuteNonQuery();
                }
            }

            transaction.Commit();
            return new LookupActionResult(true, $"已删除「{value}」。");
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            return new LookupActionResult(false, $"删除失败：{ex.Message}");
        }
    }

    public int CountUsage(string kind, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;

        using var connection = _db.Open();
        if (kind == Tag) return CountTagUsage(connection, value);

        var column = kind switch
        {
            Region => "region",
            Language => "language",
            Genre => "genre",
            Publisher => "publisher",
            Developer => "developer",
            Platform => "platform",
            _ => null,
        };
        if (column == null) return 0;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM games WHERE {column}=@value;";
        cmd.Parameters.AddWithValue("@value", value);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private bool Exists(string kind, string value)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM lookup_items WHERE kind=@kind AND value=@value LIMIT 1;";
        cmd.Parameters.AddWithValue("@kind", kind);
        cmd.Parameters.AddWithValue("@value", value);
        return cmd.ExecuteScalar() != null;
    }

    private static int RenameColumn(SqliteConnection connection, SqliteTransaction transaction, string kind, string oldValue, string newName)
    {
        var column = kind switch
        {
            Region => "region",
            Language => "language",
            Genre => "genre",
            Publisher => "publisher",
            Developer => "developer",
            _ => null,
        };
        if (column == null) return 0;

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"UPDATE games SET {column}=@new WHERE {column}=@old;";
        cmd.Parameters.AddWithValue("@new", newName);
        cmd.Parameters.AddWithValue("@old", oldValue);
        return cmd.ExecuteNonQuery();
    }

    private static int RenamePlatform(SqliteConnection connection, SqliteTransaction transaction, string oldValue, string newName)
    {
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT 1 FROM platforms WHERE name=@name LIMIT 1;";
            check.Parameters.AddWithValue("@name", newName);
            if (check.ExecuteScalar() != null)
                throw new InvalidOperationException($"平台「{newName}」已存在。");
        }

        using (var platform = connection.CreateCommand())
        {
            platform.Transaction = transaction;
            platform.CommandText = "UPDATE platforms SET name=@new WHERE name=@old;";
            platform.Parameters.AddWithValue("@new", newName);
            platform.Parameters.AddWithValue("@old", oldValue);
            platform.ExecuteNonQuery();
        }

        using (var bound = connection.CreateCommand())
        {
            bound.Transaction = transaction;
            bound.CommandText = "UPDATE emulator_platforms SET platform=@new WHERE platform=@old;";
            bound.Parameters.AddWithValue("@new", newName);
            bound.Parameters.AddWithValue("@old", oldValue);
            bound.ExecuteNonQuery();
        }

        using var games = connection.CreateCommand();
        games.Transaction = transaction;
        games.CommandText = "UPDATE games SET platform=@new WHERE platform=@old;";
        games.Parameters.AddWithValue("@new", newName);
        games.Parameters.AddWithValue("@old", oldValue);
        return games.ExecuteNonQuery();
    }

    private static int RenameTags(SqliteConnection connection, SqliteTransaction transaction, string oldValue, string newName)
    {
        var rows = new List<(long Id, string Tags)>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT id, tags FROM games WHERE tags<>'';";
            using var reader = read.ExecuteReader();
            while (reader.Read()) rows.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        var changed = 0;
        foreach (var (id, raw) in rows)
        {
            var tags = SplitTags(raw).ToList();
            if (!tags.Contains(oldValue, StringComparer.OrdinalIgnoreCase)) continue;

            var updated = string.Join(",", tags.Select(tag =>
                string.Equals(tag, oldValue, StringComparison.OrdinalIgnoreCase) ? newName : tag)
                .Distinct(StringComparer.OrdinalIgnoreCase));

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE games SET tags=@tags WHERE id=@id;";
            update.Parameters.AddWithValue("@tags", updated);
            update.Parameters.AddWithValue("@id", id);
            update.ExecuteNonQuery();
            changed++;
        }

        return changed;
    }

    private static int CountTagUsage(SqliteConnection connection, string value)
    {
        var count = 0;
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT tags FROM games WHERE tags<>'';";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (SplitTags(reader.GetString(0)).Contains(value, StringComparer.OrdinalIgnoreCase)) count++;
        }
        return count;
    }

    private static IEnumerable<string> SplitTags(string tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? Enumerable.Empty<string>()
            : tags.Split(new[] { ',', '，', ';', '；' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

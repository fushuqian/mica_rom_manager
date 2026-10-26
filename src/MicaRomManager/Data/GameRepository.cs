using Microsoft.Data.Sqlite;
using MicaRomManager.Models;

namespace MicaRomManager.Data;

public sealed class GameRepository
{
    private const string Columns =
        "id,title,platform,genre,region,language,release_year,publisher,developer,favorite,status," +
        "rom_path,file_size,cover_path,notes,tags,emulator_id,play_count,play_seconds,last_played_at,created_at,updated_at,disc_paths,rom_hash";

    private readonly Database _db;

    public GameRepository(Database db) => _db = db;

    public List<Game> ListAll()
    {
        var result = new List<Game>();
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM games ORDER BY title COLLATE NOCASE;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result.Add(Map(reader));
        return result;
    }

    public long Insert(Game game)
    {
        game.CreatedAt = DateTime.Now;
        game.UpdatedAt = DateTime.Now;
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO games
                (title,platform,genre,region,language,release_year,publisher,developer,favorite,status,
                 rom_path,file_size,cover_path,notes,tags,emulator_id,play_count,play_seconds,last_played_at,created_at,updated_at,disc_paths,rom_hash)
            VALUES
                (@title,@platform,@genre,@region,@language,@release_year,@publisher,@developer,@favorite,@status,
                 @rom_path,@file_size,@cover_path,@notes,@tags,@emulator_id,@play_count,@play_seconds,@last_played_at,@created_at,@updated_at,@disc_paths,@rom_hash);
            SELECT last_insert_rowid();
            """;
        AddParameters(cmd, game);
        game.Id = Convert.ToInt64(cmd.ExecuteScalar());
        return game.Id;
    }

    public void Update(Game game)
    {
        game.UpdatedAt = DateTime.Now;
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE games SET
                title=@title, platform=@platform, genre=@genre, region=@region, language=@language,
                release_year=@release_year, publisher=@publisher, developer=@developer,
                favorite=@favorite, status=@status, rom_path=@rom_path, file_size=@file_size,
                cover_path=@cover_path, notes=@notes, tags=@tags, emulator_id=@emulator_id,
                play_count=@play_count, play_seconds=@play_seconds, last_played_at=@last_played_at,
                disc_paths=@disc_paths,
                rom_hash=@rom_hash,
                updated_at=@updated_at
            WHERE id=@id;
            """;
        AddParameters(cmd, game);
        cmd.Parameters.AddWithValue("@id", game.Id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>库内已登记的非空指纹集合（导入/扫描去重用）。</summary>
    public HashSet<string> ListHashes()
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT rom_hash FROM games WHERE rom_hash <> '';";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            if (!reader.IsDBNull(0)) hashes.Add(reader.GetString(0));
        return hashes;
    }

    /// <summary>补算存量条目的指纹。</summary>
    public void SetHash(long id, string hash)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE games SET rom_hash=@hash, updated_at=@updated_at WHERE id=@id;";
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("O"));
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM games WHERE id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>游戏退出后累加游玩次数与时长。</summary>
    public void AddPlayStats(long id, long seconds)
    {
        if (seconds < 0) seconds = 0;
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE games SET
                play_count = play_count + 1,
                play_seconds = play_seconds + @seconds,
                last_played_at = @now,
                updated_at = @now
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@seconds", seconds);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("O"));
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private static void AddParameters(SqliteCommand cmd, Game g)
    {
        cmd.Parameters.AddWithValue("@title", g.Title);
        cmd.Parameters.AddWithValue("@platform", g.Platform);
        cmd.Parameters.AddWithValue("@genre", g.Genre);
        cmd.Parameters.AddWithValue("@region", g.Region);
        cmd.Parameters.AddWithValue("@language", g.Language);
        cmd.Parameters.AddWithValue("@release_year", (object?)g.ReleaseYear ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@publisher", g.Publisher);
        cmd.Parameters.AddWithValue("@developer", g.Developer);
        cmd.Parameters.AddWithValue("@favorite", g.Favorite ? 1 : 0);
        cmd.Parameters.AddWithValue("@status", g.Status);
        cmd.Parameters.AddWithValue("@rom_path", g.RomPath);
        cmd.Parameters.AddWithValue("@file_size", g.FileSize);
        cmd.Parameters.AddWithValue("@cover_path", g.CoverPath);
        cmd.Parameters.AddWithValue("@notes", g.Notes);
        cmd.Parameters.AddWithValue("@tags", g.Tags);
        cmd.Parameters.AddWithValue("@emulator_id", (object?)g.EmulatorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@play_count", g.PlayCount);
        cmd.Parameters.AddWithValue("@play_seconds", g.PlaySeconds);
        cmd.Parameters.AddWithValue("@last_played_at", (object?)g.LastPlayedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@disc_paths", g.DiscPaths);
        cmd.Parameters.AddWithValue("@rom_hash", g.RomHash);
        cmd.Parameters.AddWithValue("@created_at", g.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updated_at", g.UpdatedAt.ToString("O"));
    }

    private static Game Map(SqliteDataReader r)
    {
        return new Game
        {
            Id = r.GetInt64(0),
            Title = r.GetString(1),
            Platform = r.GetString(2),
            Genre = r.GetString(3),
            Region = r.GetString(4),
            Language = r.GetString(5),
            ReleaseYear = r.IsDBNull(6) ? null : r.GetInt32(6),
            Publisher = r.GetString(7),
            Developer = r.GetString(8),
            Favorite = r.GetInt64(9) != 0,
            Status = r.GetString(10),
            RomPath = r.GetString(11),
            FileSize = r.GetInt64(12),
            CoverPath = r.GetString(13),
            Notes = r.GetString(14),
            Tags = r.GetString(15),
            EmulatorId = r.IsDBNull(16) ? null : r.GetInt64(16),
            PlayCount = r.GetInt32(17),
            PlaySeconds = r.GetInt64(18),
            LastPlayedAt = r.IsDBNull(19) ? null : r.GetString(19),
            CreatedAt = ParseDate(r.GetString(20)),
            UpdatedAt = ParseDate(r.GetString(21)),
            DiscPaths = r.GetString(22),
            RomHash = r.GetString(23),
        };
    }

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, out var dt) ? dt : DateTime.Now;
}

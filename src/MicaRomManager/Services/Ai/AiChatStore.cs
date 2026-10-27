using System.IO;
using System.Text.Json;

namespace MicaRomManager.Services.Ai;

public sealed record AiChatRecord(string Role, string Text, DateTime Time);

/// <summary>聊天记录本地持久化；只保存 user/assistant 文本，不保存工具原始数据。</summary>
public sealed class AiChatStore
{
    private const int MaxRecords = 100;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;

    public AiChatStore(string path) => _path = path;

    public List<AiChatRecord> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new List<AiChatRecord>();
            var records = JsonSerializer.Deserialize<List<AiChatRecord>>(File.ReadAllText(_path), Options)
                          ?? new List<AiChatRecord>();
            return records
                .Where(record => record.Role is "user" or "assistant")
                .Where(record => !string.IsNullOrWhiteSpace(record.Text))
                .TakeLast(MaxRecords)
                .ToList();
        }
        catch
        {
            return new List<AiChatRecord>();
        }
    }

    public void Save(IEnumerable<AiChatRecord> records)
    {
        try
        {
            var list = records
                .Where(record => record.Role is "user" or "assistant")
                .Where(record => !string.IsNullOrWhiteSpace(record.Text))
                .TakeLast(MaxRecords)
                .ToList();

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(list, Options));
            File.Move(temp, _path, true);
        }
        catch
        {
            // 聊天记录写失败不影响对话
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch
        {
            // 忽略删除失败
        }
    }
}

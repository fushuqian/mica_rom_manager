using System.IO;
using System.Text.Json;
using MicaRomManager.Models;

namespace MicaRomManager.Services;

public static class EmulatorTemplateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static List<EmulatorTemplate> Load(PathService paths)
    {
        try
        {
            var userFile = Path.Combine(paths.DataDir, "EmulatorTemplates.json");
            if (!File.Exists(userFile) && File.Exists(PathService.TemplatesPath))
            {
                File.Copy(PathService.TemplatesPath, userFile, false);
            }

            var file = File.Exists(userFile) ? userFile : PathService.TemplatesPath;
            if (!File.Exists(file)) return new List<EmulatorTemplate>();

            return JsonSerializer.Deserialize<List<EmulatorTemplate>>(File.ReadAllText(file), Options)
                   ?? new List<EmulatorTemplate>();
        }
        catch
        {
            return new List<EmulatorTemplate>();
        }
    }
}

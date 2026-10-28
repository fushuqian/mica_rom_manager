using System.IO;

namespace MicaRomManager.Services;

public sealed class PathService
{
    public string DataDir { get; }
    public string DbPath => Path.Combine(DataDir, "mica.db");
    public string CoversDir => Path.Combine(DataDir, "covers");
    public string EmulatorsDir => Path.Combine(DataDir, "emulators");
    public string ExportsDir => Path.Combine(DataDir, "exports");
    public string LogsDir => Path.Combine(DataDir, "logs");
    public string DownloadsDir => Path.Combine(DataDir, "downloads");
    public string BackupsDir => Path.Combine(EmulatorsDir, ".backup");

    public static string TemplatesPath => Path.Combine(AppContext.BaseDirectory, "Resources", "EmulatorTemplates.json");

    public PathService()
    {
        var portable = Path.Combine(AppContext.BaseDirectory, "data");
        DataDir = IsWritable(portable)
            ? portable
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicaRomManager");

        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(CoversDir);
        Directory.CreateDirectory(EmulatorsDir);
        Directory.CreateDirectory(ExportsDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(DownloadsDir);
        Directory.CreateDirectory(BackupsDir);
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

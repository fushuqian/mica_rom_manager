using System.IO;

namespace MicaRomManager.Services;

public sealed class CoverStore
{
    private readonly PathService _paths;

    public CoverStore(PathService paths) => _paths = paths;

    public string Import(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension)) extension = ".png";
        var fileName = Guid.NewGuid().ToString("N") + extension;
        var target = Path.Combine(_paths.CoversDir, fileName);
        File.Copy(sourcePath, target, true);
        return target;
    }
}

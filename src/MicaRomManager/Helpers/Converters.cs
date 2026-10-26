using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace MicaRomManager.Helpers;

public sealed class CoverThumbnailConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var path = value as string;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.DecodePixelWidth = 96;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public static class PlatformCatalog
{
    public static IReadOnlyList<string> Common { get; } = new[]
    {
        "NES", "SNES", "N64", "Game Boy", "GBC", "GBA", "NDS", "3DS", "Switch",
        "GameCube", "Wii", "Mega Drive", "Master System", "Game Gear", "Saturn", "Dreamcast",
        "PlayStation", "PS2", "PS3", "PSP", "PC Engine", "Virtual Boy", "街机", "其他",
    };
}

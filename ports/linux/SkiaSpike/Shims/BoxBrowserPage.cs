using SkiaSharp;
namespace PKForge.App.Views;
// Spike stand-in for the 5970-line page: only the pixel-font loader PickerMenu calls, copied verbatim.
public static class BoxBrowserPage
{
    private static SKTypeface? _pixelTypeface;
    internal static SKTypeface PixelTypeface()
    {
        if (_pixelTypeface is not null) return _pixelTypeface;
        try
        {
            var cache = System.IO.Path.Combine(FileSystem.CacheDirectory, "NDS12.ttf");
            if (!File.Exists(cache))
            {
                using var asset = FileSystem.OpenAppPackageFileAsync("NDS12.ttf").GetAwaiter().GetResult();
                using var output = File.Create(cache);
                asset.CopyTo(output);
            }
            _pixelTypeface = SKTypeface.FromFile(cache);
            Console.WriteLine("FONT loaded from app package: " + _pixelTypeface?.FamilyName);
        }
        catch (Exception e) { Console.WriteLine("FONT asset load failed: " + e.GetType().Name + " " + e.Message); }
        _pixelTypeface ??= SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
        return _pixelTypeface;
    }
}

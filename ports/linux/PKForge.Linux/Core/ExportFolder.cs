namespace PKForge.Linux.Core;

/// <summary>The Linux stand-in for a share sheet: a copy of the file in a folder the user can reach.</summary>
public static class ExportFolder
{
    /// <summary>Copies <paramref name="sourcePath"/> into <paramref name="exportRoot"/> (created if
    /// missing) and returns the new path. A taken name gets "-yyyyMMdd-HHmmss" before the extension.</summary>
    public static string Copy(string sourcePath, string exportRoot, DateTimeOffset now)
    {
        Directory.CreateDirectory(exportRoot);
        var name = Path.GetFileName(sourcePath);
        var target = Path.Combine(exportRoot, name);
        if (File.Exists(target))
        {
            var stamp = now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            target = Path.Combine(exportRoot,
                $"{Path.GetFileNameWithoutExtension(name)}-{stamp}{Path.GetExtension(name)}");
        }
        File.Copy(sourcePath, target, overwrite: false);
        return target;
    }
}

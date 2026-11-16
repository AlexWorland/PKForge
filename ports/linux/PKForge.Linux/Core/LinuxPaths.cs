namespace PKForge.Linux.Core;

public static class LinuxPaths
{
    private static readonly string[] FixedRoots =
        ["/storage/roms", "/roms", "/roms2", "/userdata/roms", "/mnt/sdcard", "/media"];

    /// <summary>Folders worth offering first when picking a save: user-configured roots, then
    /// the usual handheld-Linux ROM mounts, then home. Only existing ones, each once.</summary>
    public static IReadOnlyList<string> SuggestedRoots(Func<string, bool> directoryExists, string home, IEnumerable<string> extra)
    {
        var result = new List<string>();
        foreach (var candidate in extra.Concat(FixedRoots).Append(home))
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (result.Contains(candidate, StringComparer.Ordinal)) continue;
            if (directoryExists(candidate)) result.Add(candidate);
        }
        return result;
    }
}

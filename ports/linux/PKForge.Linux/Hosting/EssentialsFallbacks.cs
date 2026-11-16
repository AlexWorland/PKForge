using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace PKForge.Linux;

// OpenMaui does not supply every MAUI Essentials service; the ones it leaves on the
// reference-assembly implementation throw NotImplementedInReferenceAssemblyException.
// Program.Main installs these before the app composes. See DECISIONS.md ESSENTIALS_GAPS.
internal sealed class LinuxAppInfo : IAppInfo
{
    public string PackageName => "org.pkforge.app";
    public string Name => "PKForge";
    public string VersionString => "1.0.0";
    public Version Version => new(1, 0, 0);
    public string BuildString => "1";
    public void ShowSettingsUI() { }
    public AppTheme RequestedTheme => AppTheme.Dark;
    public AppPackagingModel PackagingModel => AppPackagingModel.Unpackaged;
    public LayoutDirection RequestedLayoutDirection => LayoutDirection.LeftToRight;
}

internal static class EssentialsFallbacks
{
    // Essentials' SetCurrent hooks are internal in the shipped assembly, so go through reflection.
    public static void Install()
    {
        SetCurrent(typeof(AppInfo), typeof(IAppInfo), new LinuxAppInfo());
        var fileSystem = new LinuxFileSystem();
        SetCurrent(typeof(FileSystem), typeof(IFileSystem), fileSystem);
    }

    private static void SetCurrent(Type essential, Type contract, object implementation)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        // Current-style Essentials use SetCurrent; Preferences, Share and the like use SetDefault.
        var setter = essential.GetMethod("SetCurrent", flags, null, [contract], null)
            ?? essential.GetMethod("SetDefault", flags, null, [contract], null)
            ?? throw new InvalidOperationException($"{essential.Name}.SetCurrent/SetDefault not found");
        setter.Invoke(null, [implementation]);
    }
}

internal sealed class LinuxFileSystem : IFileSystem
{
    public string CacheDirectory { get; } = Ensure(Path.Combine(Root("XDG_CACHE_HOME", ".cache"), "PKForge"));
    public string AppDataDirectory { get; } = Ensure(Path.Combine(Root("XDG_DATA_HOME", ".local/share"), "PKForge"));

    // The app's MauiAssets are copied next to the executable under their logical names.
    public Task<Stream> OpenAppPackageFileAsync(string filename) =>
        Task.FromResult<Stream>(File.OpenRead(Path.Combine(AppContext.BaseDirectory, filename)));

    public Task<bool> AppPackageFileExistsAsync(string filename) =>
        Task.FromResult(File.Exists(Path.Combine(AppContext.BaseDirectory, filename)));

    private static string Root(string variable, string fallback)
    {
        var set = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrEmpty(set)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallback)
            : set;
    }

    private static string Ensure(string path) { Directory.CreateDirectory(path); return path; }
}

using System.Reflection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace PKForge.Linux;

// OpenMaui does not supply every MAUI Essentials service; the ones it leaves on the
// reference-assembly implementation throw NotImplementedInReferenceAssemblyException.
// Program.Main installs these before the app composes (AppInfo and FileSystem are read
// by the composition root). See DECISIONS.md ESSENTIALS_GAPS.
internal sealed class LinuxAppInfo : IAppInfo
{
    // The numbers come from the csproj's ApplicationDisplayVersion / ApplicationVersion, which it
    // stamps into the assembly as AssemblyMetadata, so they cannot drift from the app's version.
    private static readonly string Display = Meta("ApplicationDisplayVersion");
    private static readonly string Build = Meta("ApplicationVersion");

    public string PackageName => "org.pkforge.app";
    public string Name => "PKForge";
    public string VersionString => Display;
    public Version Version => System.Version.Parse(Display);
    public string BuildString => Build;
    public void ShowSettingsUI() { }
    public AppTheme RequestedTheme => AppTheme.Dark;
    public AppPackagingModel PackagingModel => AppPackagingModel.Unpackaged;
    public LayoutDirection RequestedLayoutDirection => LayoutDirection.LeftToRight;

    private static string Meta(string key) =>
        typeof(LinuxAppInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value
        ?? throw new InvalidOperationException($"The csproj does not stamp {key} into the assembly.");
}

internal static class EssentialsFallbacks
{
    public static void Install()
    {
        SetStatic(typeof(AppInfo), "SetCurrent", typeof(IAppInfo), new LinuxAppInfo());
        SetStatic(typeof(FileSystem), "SetCurrent", typeof(IFileSystem), new LinuxFileSystem());
    }

    // Essentials' setters are not public in the shipped assembly (SetCurrent for AppInfo and
    // FileSystem, SetDefault for Share, Clipboard and the rest), so both files go through here.
    public static void SetStatic(Type essential, string method, Type contract, object implementation)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var setter = essential.GetMethod(method, flags, null, [contract], null)
            ?? throw new InvalidOperationException($"{essential.Name}.{method}({contract.Name}) not found");
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

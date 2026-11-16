using System.Xml.Linq;
using Xunit;

namespace PKForge.Linux.Tests;

public class AssetParityTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PKForge.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("PKForge.sln not found above " + AppContext.BaseDirectory);
    }

    private static string Norm(string path, string appPrefix)
    {
        path = path.Replace('\\', '/');
        const string root = "$(PKForgeRoot)/";
        if (path.StartsWith(root, StringComparison.Ordinal)) return path[root.Length..];
        // Android project paths are relative to src/PKForge.App; resolve them to repo-relative.
        var full = Path.GetFullPath(Path.Combine(Root, appPrefix, path));
        return Path.GetRelativePath(Root, full).Replace('\\', '/');
    }

    private static string NormList(string? list, string appPrefix) =>
        string.Join(';', (list ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => Norm(p, appPrefix)).OrderBy(p => p, StringComparer.Ordinal));

    private static SortedSet<string> Collect(string file, string appPrefix, bool skipLinuxOnly)
    {
        var doc = XDocument.Load(file);
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var group in doc.Descendants("ItemGroup"))
        {
            if (skipLinuxOnly && (string?)group.Attribute("Label") == "LinuxOnly") continue;
            foreach (var item in group.Elements().Where(e => e.Name.LocalName is "MauiAsset" or "MauiFont"))
            {
                var include = NormList((string?)item.Attribute("Include"), appPrefix);
                var exclude = NormList((string?)item.Attribute("Exclude"), appPrefix);
                var logical = (string?)item.Attribute("LogicalName") ?? "";
                set.Add($"{item.Name.LocalName}|{include}|{exclude}|{logical}");
            }
        }
        return set;
    }

    private static string Prop(string file, string name) =>
        XDocument.Load(file).Descendants(name).Select(e => e.Value.Trim()).First();

    private static readonly string AndroidCsproj = Path.Combine(Root, "src/PKForge.App/PKForge.App.csproj");
    private static readonly string LinuxDir = Path.Combine(Root, "ports/linux/PKForge.Linux");

    [Fact]
    public void LinuxAssetsMirrorTheAndroidProject()
    {
        var android = Collect(AndroidCsproj, "src/PKForge.App", skipLinuxOnly: false);
        var linux = Collect(Path.Combine(LinuxDir, "Assets.props"), "ports/linux/PKForge.Linux", skipLinuxOnly: true);
        Assert.NotEmpty(android);
        Assert.Equal(android, linux);
    }

    [Fact]
    public void VersionMatchesTheAndroidProject()
    {
        var linuxCsproj = Path.Combine(LinuxDir, "PKForge.Linux.csproj");
        Assert.Equal(Prop(AndroidCsproj, "ApplicationVersion"), Prop(linuxCsproj, "ApplicationVersion"));
        Assert.Equal(Prop(AndroidCsproj, "ApplicationDisplayVersion"), Prop(linuxCsproj, "ApplicationDisplayVersion"));
    }
}

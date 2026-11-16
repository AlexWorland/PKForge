using PKForge.Linux.Core;
using Xunit;

namespace PKForge.Linux.Tests;

public sealed class FileBrowserModelTests
{
    private static readonly Dictionary<string, (string Name, bool IsDirectory)[]> Tree = new()
    {
        ["/roms"] =
        [
            ("zeta.srm", false), ("Alpha", true), ("beta.sav", false), (".hidden", true), (".secret.sav", false), ("gba", true),
        ],
        ["/roms/gba"] = [("emerald.sav", false)],
        ["/media"] = [],
    };

    private static FileBrowserModel Model() =>
        new(path => Tree.TryGetValue(path, out var rows) ? rows : [], ["/roms", "/media"]);

    [Fact]
    public void RowsListDirectoriesFirstThenFilesAndHideDotNames()
    {
        var model = Model();
        model.Enter(0); // /roms
        Assert.Equal("/roms", model.Current);
        Assert.Equal(["..", "Alpha", "gba", "beta.sav", "zeta.srm"], model.Rows.Select(r => r.Name));
        Assert.Equal(["Alpha", "gba"], model.Rows.Where(r => r.IsDirectory && r.Name != "..").Select(r => r.Name));
        Assert.Equal("/roms/gba", model.Rows.Single(r => r.Name == "gba").FullPath);
    }

    [Fact]
    public void EnterThenUpReturnsToTheParent()
    {
        var model = Model();
        model.Enter(0);
        model.Enter(model.Rows.ToList().FindIndex(r => r.Name == "gba"));
        Assert.Equal("/roms/gba", model.Current);
        model.Up();
        Assert.Equal("/roms", model.Current);
        // The ".." row goes up as well.
        model.Enter(0);
        Assert.Null(model.Current);
    }

    [Fact]
    public void UpAtARootShowsTheRootsListWithNoParentRow()
    {
        var model = Model();
        Assert.Null(model.Current);
        Assert.Equal(["/roms", "/media"], model.Rows.Select(r => r.FullPath));
        Assert.DoesNotContain(model.Rows, r => r.Name == "..");
        model.Enter(1);
        Assert.Equal("/media", model.Current);
        model.Up();
        Assert.Null(model.Current);
        model.Up(); // already at the list: stays
        Assert.Null(model.Current);
    }
}

using PKForge.Linux.Core;
using Xunit;

namespace PKForge.Linux.Tests;

public class LinuxPathsTests
{
    [Fact]
    public void SuggestedRootsKeepsOrderAndDropsMissingAndDuplicates()
    {
        var existing = new HashSet<string> { "/roms", "/home/u" };
        var result = LinuxPaths.SuggestedRoots(existing.Contains, "/home/u", ["/roms"]);
        Assert.Equal(["/roms", "/home/u"], result);
    }

    [Fact]
    public void ExtraRootsComeFirstThenFixedThenHome()
    {
        var existing = new HashSet<string> { "/x", "/media", "/storage/roms", "/h" };
        var result = LinuxPaths.SuggestedRoots(existing.Contains, "/h", ["/x"]);
        Assert.Equal(["/x", "/storage/roms", "/media", "/h"], result);
    }
}

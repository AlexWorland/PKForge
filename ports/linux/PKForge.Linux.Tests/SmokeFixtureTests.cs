using PKForge.Engine;
using Xunit;

namespace PKForge.Linux.Tests;

// Not a test of behaviour: packaging/smoke.sh needs a valid save on disk, and only the engine can
// make one. Set PKFORGE_FIXTURE_DIR to have this write crystal.sav (a blank Gen 2 Crystal save) there.
public sealed class SmokeFixtureTests
{
    [Fact]
    public void WritesTheSmokeSaveWhenAskedTo()
    {
        var dir = Environment.GetEnvironmentVariable("PKFORGE_FIXTURE_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        using var session = new SaveEngine().OpenBlankSession(2);
        var path = Path.Combine(dir, "crystal.sav");
        File.WriteAllBytes(path, session.Serialize().ToArray());
        Assert.True(new FileInfo(path).Length > 0);
    }
}

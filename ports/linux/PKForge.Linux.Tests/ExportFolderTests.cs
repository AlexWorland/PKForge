using PKForge.Linux.Core;
using Xunit;

namespace PKForge.Linux.Tests;

public sealed class ExportFolderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pkforge-" + Guid.NewGuid().ToString("N"));

    public ExportFolderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Source(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "data");
        return path;
    }

    [Fact]
    public void CopyKeepsTheNameWhenFree()
    {
        var export = Path.Combine(_dir, "exports");
        var copy = ExportFolder.Copy(Source("team.txt"), export, DateTimeOffset.UnixEpoch);
        Assert.Equal(Path.Combine(export, "team.txt"), copy);
        Assert.Equal("data", File.ReadAllText(copy));
    }

    [Fact]
    public void CopyAddsATimestampWhenTheNameIsTaken()
    {
        var export = Path.Combine(_dir, "exports");
        Directory.CreateDirectory(export);
        File.WriteAllText(Path.Combine(export, "team.txt"), "old");
        var now = new DateTimeOffset(2026, 11, 15, 10, 20, 30, TimeSpan.Zero);
        var copy = ExportFolder.Copy(Source("team.txt"), export, now);
        Assert.Equal(Path.Combine(export, "team-20261115-102030.txt"), copy);
        Assert.Equal("old", File.ReadAllText(Path.Combine(export, "team.txt")));
        Assert.Equal("data", File.ReadAllText(copy));
    }
}

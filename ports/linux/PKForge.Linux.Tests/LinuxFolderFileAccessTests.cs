using PKForge.Domain;
using PKForge.Linux.Core;
using Xunit;

namespace PKForge.Linux.Tests;

public sealed class LinuxFolderFileAccessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pkforge-" + Guid.NewGuid().ToString("N"));

    public LinuxFolderFileAccessTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task ListFilesReturnsOnlyDirectChildrenWithFullPathIds()
    {
        File.WriteAllText(Path.Combine(_dir, "one.sav"), "1");
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        File.WriteAllText(Path.Combine(_dir, "sub", "deep.sav"), "2");
        var files = await new LinuxFolderFileAccess().ListFilesAsync(_dir);
        var only = Assert.Single(files);
        Assert.Equal(new PickedDocument(Path.Combine(_dir, "one.sav"), "one.sav"), only);
    }

    [Fact]
    public async Task TreeWriteOutsideTheRootIsRefused()
    {
        IFolderTreeAccess tree = new LinuxFolderFileAccess();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await tree.WriteAsync(_dir, "../escape.bin", new byte[] { 1 }));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_dir)!, "escape.bin")));
    }

    [Fact]
    public async Task TreeRoundTripUsesForwardSlashRelativePaths()
    {
        IFolderTreeAccess tree = new LinuxFolderFileAccess();
        await tree.WriteAsync(_dir, "Bank/01/a.pk1", new byte[] { 1, 2, 3 });
        var entry = Assert.Single(await tree.ListTreeAsync(_dir));
        Assert.Equal("Bank/01/a.pk1", entry.Path);
        Assert.Equal(3, entry.Size);
        Assert.Equal(new byte[] { 1, 2, 3 }, await tree.ReadAsync(_dir, "Bank/01/a.pk1"));
        Assert.True(await tree.DeleteAsync(_dir, "Bank/01/a.pk1"));
        Assert.False(await tree.DeleteAsync(_dir, "Bank/01/a.pk1"));
    }

    [Fact]
    public async Task WriteBesideNeverOverwritesAndReturnsTheFreeName()
    {
        var access = new LinuxFolderFileAccess();
        var existing = Path.Combine(_dir, "g.sav");
        File.WriteAllBytes(existing, [9]);
        var name = await access.WriteBesideAsync(existing, "g.sav", new byte[] { 1 });
        Assert.Equal("g (copy).sav", name);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(existing));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(_dir, name)));
    }

    [Fact]
    public async Task WriteFileCreatesOrOverwritesByName()
    {
        var access = new LinuxFolderFileAccess();
        await access.WriteFileAsync(_dir, "x.bin", new byte[] { 1 });
        await access.WriteFileAsync(_dir, "x.bin", new byte[] { 2 });
        Assert.Equal(new byte[] { 2 }, (await access.ReadFileAsync(Path.Combine(_dir, "x.bin"))).ToArray());
    }
}

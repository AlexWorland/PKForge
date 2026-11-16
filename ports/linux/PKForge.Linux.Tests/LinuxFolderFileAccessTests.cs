using PKForge.Domain;
using PKForge.Linux.Core;
using System.Runtime.Versioning;
using Xunit;

namespace PKForge.Linux.Tests;

[SupportedOSPlatform("linux")]
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

    private string MakeOutside(out string secret)
    {
        var outside = Path.Combine(Path.GetTempPath(), "pkforge-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        secret = Path.Combine(outside, "secret.bin");
        File.WriteAllBytes(secret, [7]);
        return outside;
    }

    [Fact]
    public async Task SymlinkedDirectoryPointingOutsideIsRefusedForReadWriteAndDelete()
    {
        var outside = MakeOutside(out var secret);
        try
        {
            Directory.CreateDirectory(Path.Combine(_dir, "Bank"));
            Directory.CreateSymbolicLink(Path.Combine(_dir, "Bank", "link"), outside);
            IFolderTreeAccess tree = new LinuxFolderFileAccess();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await tree.ReadAsync(_dir, "Bank/link/secret.bin"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await tree.WriteAsync(_dir, "Bank/link/new.bin", new byte[] { 1 }));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await tree.DeleteAsync(_dir, "Bank/link/secret.bin"));
            Assert.True(File.Exists(secret));
            Assert.False(File.Exists(Path.Combine(outside, "new.bin")));
        }
        finally { Directory.Delete(outside, recursive: true); }
    }

    [Fact]
    public async Task SymlinkedFilePointingOutsideIsRefused()
    {
        var outside = MakeOutside(out var secret);
        try
        {
            File.CreateSymbolicLink(Path.Combine(_dir, "f.bin"), secret);
            IFolderTreeAccess tree = new LinuxFolderFileAccess();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await tree.ReadAsync(_dir, "f.bin"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await tree.WriteAsync(_dir, "f.bin", new byte[] { 1 }));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await tree.DeleteAsync(_dir, "f.bin"));
            Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(secret));
        }
        finally { Directory.Delete(outside, recursive: true); }
    }

    [Fact]
    public async Task SymlinkInsideTheRootStaysAllowed()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "real"));
        File.WriteAllBytes(Path.Combine(_dir, "real", "a.bin"), [5]);
        Directory.CreateSymbolicLink(Path.Combine(_dir, "alias"), Path.Combine(_dir, "real"));
        IFolderTreeAccess tree = new LinuxFolderFileAccess();
        Assert.Equal(new byte[] { 5 }, await tree.ReadAsync(_dir, "alias/a.bin"));
    }

    [Fact]
    public async Task ListTreeDoesNotFollowLinksOutsideTheRoot()
    {
        var outside = MakeOutside(out var secret);
        try
        {
            File.WriteAllBytes(Path.Combine(_dir, "ok.bin"), [1]);
            Directory.CreateSymbolicLink(Path.Combine(_dir, "dirlink"), outside);
            File.CreateSymbolicLink(Path.Combine(_dir, "filelink.bin"), secret);
            var entries = await ((IFolderTreeAccess)new LinuxFolderFileAccess()).ListTreeAsync(_dir);
            Assert.Equal(new[] { "ok.bin" }, entries.Select(e => e.Path).ToArray());
        }
        finally { Directory.Delete(outside, recursive: true); }
    }

    [Fact]
    public async Task AbsolutePathArgumentIsRefused()
    {
        IFolderTreeAccess tree = new LinuxFolderFileAccess();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await tree.ReadAsync(_dir, "/etc/hostname"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await tree.WriteAsync(_dir, "/tmp/pkforge-abs.bin", new byte[] { 1 }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await tree.DeleteAsync(_dir, "/etc/hostname"));
    }

    [Fact]
    public async Task WriteBesideSkipsTakenNamesAndLeavesThemUntouched()
    {
        var access = new LinuxFolderFileAccess();
        var existing = Path.Combine(_dir, "g.sav");
        File.WriteAllBytes(existing, [9]);
        File.WriteAllBytes(Path.Combine(_dir, "g (copy).sav"), [8]);
        var name = await access.WriteBesideAsync(existing, "g.sav", new byte[] { 1 });
        Assert.NotEqual("g.sav", name);
        Assert.NotEqual("g (copy).sav", name);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(existing));
        Assert.Equal(new byte[] { 8 }, File.ReadAllBytes(Path.Combine(_dir, "g (copy).sav")));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(_dir, name)));
    }

    [Fact]
    public async Task WriteBesideFailureDuringWriteLeavesNoPartialFileAndPropagates()
    {
        var access = new LinuxFolderFileAccess();
        var existing = Path.Combine(_dir, "g.sav");
        File.WriteAllBytes(existing, [9]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await access.WriteBesideAsync(existing, "g.sav", new byte[1 << 20], cts.Token));
        Assert.Equal(new[] { existing }, Directory.GetFileSystemEntries(_dir));
    }
}

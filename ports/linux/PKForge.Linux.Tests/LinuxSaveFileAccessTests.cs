using System.Security.Cryptography;
using PKForge.Linux.Core;
using Xunit;

namespace PKForge.Linux.Tests;

public sealed class LinuxSaveFileAccessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pkforge-" + Guid.NewGuid().ToString("N"));

    public LinuxSaveFileAccessTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Unlock();
        Directory.Delete(_dir, recursive: true);
    }

    private void Unlock() => File.SetUnixFileMode(_dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

    [Fact]
    public async Task WriteThenReadReturnsTheSameBytes()
    {
        var access = new LinuxSaveFileAccess();
        var path = Path.Combine(_dir, "a.sav");
        var bytes = RandomNumberGenerator.GetBytes(64 * 1024);
        await access.WriteAtomicallyAsync(path, bytes);
        Assert.Equal(bytes, (await access.ReadAsync(path)).ToArray());
    }

    [Fact]
    public async Task WriteLeavesNoTemporaryAndKeepsOldBytesOnFailure()
    {
        // Root ignores directory permissions, so there is nothing to provoke a failure with.
        if (Environment.UserName == "root" || geteuid() == 0) return;
        var access = new LinuxSaveFileAccess();
        var path = Path.Combine(_dir, "a.sav");
        var a = RandomNumberGenerator.GetBytes(1024);
        var b = RandomNumberGenerator.GetBytes(1024);
        await access.WriteAtomicallyAsync(path, a);

        File.SetUnixFileMode(_dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(async () => await access.WriteAtomicallyAsync(path, b));
            Assert.Equal(a, File.ReadAllBytes(path));
        }
        finally { Unlock(); }
        Assert.Single(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public async Task ContentUriDocumentIdIsReportedMissingNotThrown()
    {
        var access = new LinuxSaveFileAccess();
        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await access.ReadAsync("content://com.android.externalstorage.documents/x"));
    }

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern uint geteuid();
}

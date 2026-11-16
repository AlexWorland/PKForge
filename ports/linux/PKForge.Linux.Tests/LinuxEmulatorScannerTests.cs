using PKForge.Domain;
using PKForge.Engine;
using PKForge.Linux.Core;
using System.Runtime.Versioning;
using Xunit;

namespace PKForge.Linux.Tests;

[SupportedOSPlatform("linux")]
public sealed class LinuxEmulatorScannerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pkforge-" + Guid.NewGuid().ToString("N"));

    public LinuxEmulatorScannerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        // A test may leave a folder at mode 000; make it deletable again.
        foreach (var sub in Directory.EnumerateDirectories(_dir, "*", SearchOption.AllDirectories))
            File.SetUnixFileMode(sub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.Delete(_dir, recursive: true);
    }

    // A blank Gen 3 save cannot be serialized (SAV3.WriteSectors throws), so the valid save is a
    // blank Gen 2 one, the era BlankGameBoySaveRoundTripTests round-trips through the engine.
    private static byte[] BlankGen2Save()
    {
        using var session = new SaveEngine().OpenBlankSession(2);
        return session.Serialize().ToArray();
    }

    private string WriteSaveAndLookalike()
    {
        var save = Path.Combine(_dir, "crystal.sav");
        File.WriteAllBytes(save, BlankGen2Save());
        File.WriteAllBytes(Path.Combine(_dir, "notasave.srm"), new byte[32 * 1024]);
        return save;
    }

    [Fact]
    public async Task FindsAValidSaveAndRejectsALookalike()
    {
        var savePath = WriteSaveAndLookalike();
        var result = await new LinuxEmulatorScanner(new SaveEngine()).ScanAsync(_dir, EmulatorKind.RetroArch);
        var save = Assert.Single(result.Saves);
        Assert.Equal(savePath, save.DocumentId);
        Assert.Contains("notasave.srm", result.RejectedCandidates);
        Assert.Equal(2, result.FilesSeen);
    }

    [Fact]
    public async Task IncrementalScanEndsWithACompleteUpdate()
    {
        WriteSaveAndLookalike();
        var updates = new List<EmulatorScanUpdate>();
        await foreach (var update in new LinuxEmulatorScanner(new SaveEngine()).ScanIncrementalAsync(_dir, EmulatorKind.RetroArch))
            updates.Add(update);
        Assert.True(updates[^1].IsComplete);
        Assert.Equal(1, updates[^1].SavesFound);
        Assert.Single(updates, u => u.Save is not null);
    }

    [Fact]
    public async Task UnreadableSubfolderIsSkippedAndReported()
    {
        if (Environment.UserName == "root" || geteuid() == 0) return; // root ignores file modes
        WriteSaveAndLookalike();
        var locked = Path.Combine(_dir, "locked");
        Directory.CreateDirectory(locked);
        File.SetUnixFileMode(locked, UnixFileMode.None);
        var result = await new LinuxEmulatorScanner(new SaveEngine()).ScanAsync(_dir, EmulatorKind.RetroArch);
        Assert.Single(result.Saves);
        Assert.Contains(result.Diagnostics!, line => line.Contains(locked));
    }

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern uint geteuid();
}

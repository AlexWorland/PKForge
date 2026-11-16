using PKForge.Domain;

namespace PKForge.App;

// Throwing stand-ins so the shared composition root resolves on Linux. The real Linux
// implementations replace them one by one in later tasks; each is deleted when its task lands.
internal sealed class PlaceholderDocumentPicker : IDocumentPicker
{
    public ValueTask<PickedDocument?> PickSaveAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("IDocumentPicker is not implemented on Linux yet.");

    public ValueTask<IReadOnlyList<PickedDocument>> PickManyAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("IDocumentPicker is not implemented on Linux yet.");
}

internal sealed class PlaceholderFolderPicker : IFolderPicker
{
    public ValueTask<PickedFolder?> PickFolderAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("IFolderPicker is not implemented on Linux yet.");
}

internal sealed class PlaceholderEmulatorDetectionService : IEmulatorDetectionService
{
    public ValueTask<EmulatorScanResult> ScanAsync(string treeId, EmulatorKind kind, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("IEmulatorDetectionService is not implemented on Linux yet.");
}

internal sealed class PlaceholderSecondaryDisplayHost : ISecondaryDisplayHost
{
    public bool IsAvailable => false;

    public ValueTask ShowAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask DismissAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

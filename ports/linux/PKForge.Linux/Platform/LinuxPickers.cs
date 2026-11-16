using PKForge.App.Views;
using PKForge.Domain;

namespace PKForge.App;

// The Linux head has no system dialogs: both pickers open the in-app FileBrowserPage on the
// current navigation stack. A DocumentId is the file's absolute path and a TreeId the folder's.
internal sealed class LinuxDocumentPicker : IDocumentPicker
{
    public async ValueTask<PickedDocument?> PickSaveAsync(CancellationToken cancellationToken = default)
    {
        var path = await FileBrowserPage.PickFileAsync(Nav(), "Open a save file");
        return path is null ? null : new PickedDocument(path, Path.GetFileName(path));
    }

    public async ValueTask<IReadOnlyList<PickedDocument>> PickManyAsync(CancellationToken cancellationToken = default)
    {
        var paths = await FileBrowserPage.PickFilesAsync(Nav(), "Choose files");
        return [.. paths.Select(p => new PickedDocument(p, Path.GetFileName(p)))];
    }

    private static INavigation Nav() =>
        FileBrowserPage.CurrentNavigation ?? throw new InvalidOperationException("No page is showing to open the file browser on.");
}

internal sealed class LinuxFolderPicker : IFolderPicker
{
    public async ValueTask<PickedFolder?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        var nav = FileBrowserPage.CurrentNavigation
            ?? throw new InvalidOperationException("No page is showing to open the file browser on.");
        var path = await FileBrowserPage.PickFolderAsync(nav, "Choose an emulator folder");
        if (path is null) return null;
        var name = Path.GetFileName(path.TrimEnd('/'));
        return new PickedFolder(path, string.IsNullOrEmpty(name) ? path : name);
    }
}

using PKForge.Domain;

namespace PKForge.App;

// Throwing stand-ins so the shared composition root resolves on Linux. The real Linux
// implementations replace them one by one in later tasks; each is deleted when its task lands.
internal sealed class PlaceholderSecondaryDisplayHost : ISecondaryDisplayHost
{
    public bool IsAvailable => false;

    public ValueTask ShowAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask DismissAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

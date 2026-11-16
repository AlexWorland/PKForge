using PKForge.Domain;
using PKForge.Infrastructure;

namespace PKForge.Linux.Core;

/// <summary>ISaveFileAccess over plain paths: a DocumentId is an absolute file path.</summary>
public sealed class LinuxSaveFileAccess : ISaveFileAccess
{
    public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(string documentId, CancellationToken cancellationToken = default)
    {
        // A content:// id left over from an Android profile is not rooted, so it lands here too.
        if (!Path.IsPathRooted(documentId) || !File.Exists(documentId))
            throw new FileNotFoundException($"No file at '{documentId}'.", documentId);
        return await File.ReadAllBytesAsync(documentId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteAtomicallyAsync(string documentId, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathRooted(documentId))
            throw new FileNotFoundException($"'{documentId}' is not a file path.", documentId);
        await AtomicFile.WriteAsync(documentId, bytes, cancellationToken).ConfigureAwait(false);
    }
}

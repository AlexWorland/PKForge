using PKForge.Domain;
using PKForge.Infrastructure;

namespace PKForge.Linux.Core;

/// <summary>Folder access over plain paths: a TreeId is an absolute directory, a DocumentId an absolute file.</summary>
public sealed class LinuxFolderFileAccess : IFolderFileAccess, IFolderTreeAccess
{
    public ValueTask<IReadOnlyList<PickedDocument>> ListFilesAsync(string treeId, CancellationToken cancellationToken = default)
    {
        var root = RootOf(treeId);
        IReadOnlyList<PickedDocument> files = Directory.EnumerateFiles(root)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new PickedDocument(p, Path.GetFileName(p)))
            .ToList();
        return ValueTask.FromResult(files);
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReadFileAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathRooted(documentId) || !File.Exists(documentId))
            throw new FileNotFoundException($"No file at '{documentId}'.", documentId);
        return await File.ReadAllBytesAsync(documentId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteFileAsync(string treeId, string fileName, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        var target = Resolve(RootOf(treeId), fileName);
        await AtomicFile.WriteAsync(target, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a new file next to <paramref name="documentId"/>, never overwriting: a taken name gets a "(copy)" suffix.</summary>
    public async ValueTask<string> WriteBesideAsync(string documentId, string fileName, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathRooted(documentId))
            throw new NotSupportedException("This file's folder cannot be written to.");
        var directory = Path.GetDirectoryName(documentId);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            throw new NotSupportedException("This file's folder cannot be written to.");

        var name = fileName;
        for (var attempt = 0; attempt < MaxNameAttempts; attempt++)
        {
            name = SaveInsights.FreeFileName(name, Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName)!);
            var target = Resolve(directory, name);
            FileStream stream;
            try
            {
                // CreateNew keeps "never overwrites" true even if the name is taken between listing and creating.
                stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            }
            catch (IOException) when (File.Exists(target) || Directory.Exists(target))
            {
                continue; // Lost the race for this name; pick the next free one.
            }

            // From here the file exists: any failure is a real write error, so remove the partial file and surface it.
            try
            {
                await using (stream.ConfigureAwait(false))
                {
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }
                return name;
            }
            catch
            {
                try { File.Delete(target); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }
        throw new IOException($"Could not find a free name for '{fileName}' after {MaxNameAttempts} attempts.");
    }

    private const int MaxNameAttempts = 64;

    public ValueTask<IReadOnlyList<FolderEntry>> ListTreeAsync(string treeId, CancellationToken cancellationToken = default)
    {
        var root = RootOf(treeId);
        IReadOnlyList<FolderEntry> entries = Directory.EnumerateFiles(root, "*", SkipLinks)
            .Select(p => new FileInfo(p))
            .Select(f => new FolderEntry(
                Path.GetRelativePath(root, f.FullName).Replace('\\', '/'),
                f.Length,
                new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero)))
            .OrderBy(e => e.Path, StringComparer.Ordinal)
            .ToList();
        return ValueTask.FromResult(entries);
    }

    async ValueTask<byte[]> IFolderTreeAccess.ReadAsync(string treeId, string path, CancellationToken cancellationToken)
    {
        var target = Resolve(RootOf(treeId), path);
        if (!File.Exists(target)) throw new FileNotFoundException($"No file at '{path}'.", target);
        return await File.ReadAllBytesAsync(target, cancellationToken).ConfigureAwait(false);
    }

    async ValueTask IFolderTreeAccess.WriteAsync(string treeId, string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        var target = Resolve(RootOf(treeId), path);
        await AtomicFile.WriteAsync(target, bytes, cancellationToken).ConfigureAwait(false);
    }

    ValueTask<bool> IFolderTreeAccess.DeleteAsync(string treeId, string path, CancellationToken cancellationToken)
    {
        var target = Resolve(RootOf(treeId), path);
        if (!File.Exists(target)) return ValueTask.FromResult(false);
        File.Delete(target);
        return ValueTask.FromResult(true);
    }

    /// <summary>Recursive enumeration that skips symbolic links (ReparsePoint on Unix), so nothing behind a link is listed.</summary>
    private static readonly EnumerationOptions SkipLinks = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    private static string RootOf(string treeId)
    {
        if (!Path.IsPathRooted(treeId) || !Directory.Exists(treeId))
            throw new DirectoryNotFoundException($"No folder at '{treeId}'.");
        return Path.GetFullPath(treeId);
    }

    /// <summary>Joins a '/'-separated relative path onto the root; anything that lands outside the root is refused.</summary>
    private static string Resolve(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative))
            throw new UnauthorizedAccessException($"'{relative}' is not a path inside the folder.");
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal))
            throw new UnauthorizedAccessException($"'{relative}' is outside the folder.");

        // GetFullPath only normalizes text: a symlink inside the tree can still lead outside it.
        var realRoot = RealPath(root);
        var realPrefix = realRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!(RealPath(full) + Path.DirectorySeparatorChar).StartsWith(realPrefix, StringComparison.Ordinal))
            throw new UnauthorizedAccessException($"'{relative}' leads outside the folder through a link.");
        return full;
    }

    /// <summary>Resolves every symbolic link along <paramref name="path"/>; components that do not exist yet are kept as written.</summary>
    private static string RealPath(string path)
    {
        var current = Path.GetPathRoot(path)!;
        var pending = new Stack<string>(path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Reverse());
        var hops = 0;
        while (pending.Count > 0)
        {
            var next = Path.Combine(current, pending.Pop());
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (info.LinkTarget is null) { current = next; continue; }
            if (++hops > 40) throw new UnauthorizedAccessException($"Too many nested links at '{next}'.");
            var target = Path.GetFullPath(info.LinkTarget, Path.GetDirectoryName(next)!);
            // Re-walk the target so links inside it are resolved too.
            current = Path.GetPathRoot(target)!;
            foreach (var part in target[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Reverse())
                pending.Push(part);
        }
        return current;
    }
}

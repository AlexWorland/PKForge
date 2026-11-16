using System.Runtime.CompilerServices;
using System.Threading.Channels;
using PKForge.Domain;
using PKForge.Infrastructure;

namespace PKForge.Linux.Core;

/// <summary>
/// Walks an emulator folder looking for parseable Pokémon saves: the Android scanner's pipeline
/// (src/PKForge.App/Platforms/Android/AndroidEmulatorScanner.cs) over System.IO. A DocumentId is
/// the file's absolute path and a TreeId is the folder's absolute path. File-name filters are only
/// a pre-filter; every candidate's bytes must pass engine validation before being reported.
/// </summary>
public sealed class LinuxEmulatorScanner(ISaveEngine engine) : IIncrementalEmulatorDetectionService
{
    private const int EdenMaxDepth = 8;
    private const int FlatMaxDepth = 8;
    private const long MaxSaveBytes = 32 * 1024 * 1024;

    private sealed class ScanState
    {
        public int FilesSeen;
        public EmulatorKind Kind;
        public IReadOnlyList<string>? Siblings;
        public string Root = "";
        public readonly List<string> Rejected = [];
        public readonly List<string> Diagnostics = [];

        public void Trace(string message)
        {
            const int maxLines = 2000;
            if (Diagnostics.Count < maxLines)
                Diagnostics.Add(message);
            else if (Diagnostics.Count == maxLines)
                Diagnostics.Add("DIAGNOSTICS TRUNCATED after 2000 lines");
        }
    }

    private sealed record Child(string Path, string Name, bool IsDirectory, DateTimeOffset? LastModified);

    public ValueTask<EmulatorScanResult> ScanAsync(string treeId, EmulatorKind kind, CancellationToken cancellationToken = default) =>
        new(Task.Run(() => ScanCore(treeId, kind, cancellationToken, null), cancellationToken));

    public async IAsyncEnumerable<EmulatorScanUpdate> ScanIncrementalAsync(
        string treeId, EmulatorKind kind,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<EmulatorScanUpdate>(
            new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });
        var producer = Task.Run(() =>
        {
            try
            {
                var result = ScanCore(treeId, kind, cancellationToken,
                    save => channel.Writer.TryWrite(new EmulatorScanUpdate(save, false)));
                channel.Writer.TryWrite(new EmulatorScanUpdate(
                    null, true, result.FilesSeen, result.Saves.Count,
                    result.RejectedCandidates, result.Diagnostics));
                channel.Writer.TryComplete();
            }
            catch (Exception error)
            {
                channel.Writer.TryComplete(error);
            }
        });

        await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken))
            yield return update;
        await producer;
    }

    private EmulatorScanResult ScanCore(string treeId, EmulatorKind kind, CancellationToken cancellationToken,
        Action<DetectedSave>? onSave)
    {
        if (kind == EmulatorKind.LinkedFile)
            return ScanLinkedFile(treeId, onSave);
        var root = Path.GetFullPath(treeId);
        var state = new ScanState { Kind = kind, Root = root };
        state.Trace($"Scanner={nameof(LinuxEmulatorScanner)} kind={kind}");
        state.Trace($"TreePath={root}");
        List<DetectedSave> found;
        try
        {
            found = kind switch
            {
                EmulatorKind.RetroArch or EmulatorKind.MelonDS or EmulatorKind.Linkboy or
                EmulatorKind.DraStic or EmulatorKind.PizzaBoyGba or EmulatorKind.PizzaBoyGbc or EmulatorKind.Dolphin or
                EmulatorKind.MyBoy or EmulatorKind.MyOldBoy or EmulatorKind.PokeDaisy =>
                    ScanFlatFolder(root, kind, cancellationToken, state, onSave),
                EmulatorKind.Eden => ScanEden(root, cancellationToken, state, onSave),
                EmulatorKind.Azahar or EmulatorKind.CitraMmj => ScanThreeDs(root, kind, cancellationToken, state, onSave),
                _ => [],
            };
        }
        catch (Exception error)
        {
            if (error is OperationCanceledException)
                throw;
            state.Trace($"SCAN EXCEPTION {error}");
            found = [];
        }
        state.Trace($"Scanner complete files={state.FilesSeen} saves={found.Count} rejected={state.Rejected.Count}");
        return new EmulatorScanResult(EmulatorSaveHeuristics.Normalize(found), state.FilesSeen,
            [.. state.Rejected], [.. state.Diagnostics]);
    }

    /// <summary>
    /// A single linked save file: described like any scanned save. A file that is gone or no
    /// longer readable is reported as rejected, so it comes back when it is readable again.
    /// </summary>
    private EmulatorScanResult ScanLinkedFile(string path, Action<DetectedSave>? onSave)
    {
        var state = new ScanState { Kind = EmulatorKind.LinkedFile };
        state.Trace($"Scanner={nameof(LinuxEmulatorScanner)} kind={EmulatorKind.LinkedFile}");
        state.Trace($"DocumentPath={path}");
        var found = new List<DetectedSave>();
        var name = "Pokémon save";
        try
        {
            var info = new FileInfo(path);
            if (info.Name.Length > 0) name = info.Name;
            DateTimeOffset? modified = info.Exists ? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero) : null;
            state.FilesSeen = 1;
            var bytes = ReadBounded(path) ?? throw new IOException($"larger than {MaxSaveBytes} bytes");
            var description = engine.TryDescribe(bytes, name);
            if (description is null)
            {
                state.Trace($"PARSER REJECTED: {engine.DescribeRejection(bytes) ?? "ISaveEngine.TryDescribe returned null"}");
                state.Rejected.Add(name);
            }
            else
            {
                var guess = SaveIdentityRules.Guess(description.GameName, description.Generation, name, null, name);
                state.Trace($"PARSER ACCEPTED game={description.GameName} generation={description.Generation} label={guess.Label}");
                var save = new DetectedSave(path, name, guess.Label, EmulatorKind.LinkedFile,
                    RequiresExtraCare: false, modified, description.Generation, description.TrainerName,
                    description.PlayTime, guess, Language: description.Language, TrainerId: description.TrainerId);
                found.Add(save);
                onSave?.Invoke(save);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            state.Trace($"LINKED FILE UNREADABLE {error.Message}");
            state.Rejected.Add($"{name} (linked file not readable: {error.Message})");
        }
        return new EmulatorScanResult(found, state.FilesSeen, [.. state.Rejected], [.. state.Diagnostics]);
    }

    /// <summary>Emulator folders whose contents can never be saves, pruned so a whole-RetroArch grant stays fast.</summary>
    private static readonly HashSet<string> PrunedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "assets", "autoconfig", "cheats", "config", "cores", "database", "downloads", "filters",
        "info", "logs", "overlays", "playlists", "remaps", "screenshots", "shaders", "states",
        "system", "thumbnails", "cache", "temp", "roms", "shader_cache",
    };

    // RetroArch keeps ROMs apart from its saves folder, so skipping "roms" keeps a whole-folder
    // grant fast. Standalone emulators (melonDS, DraStic, Pizza Boy...) save beside the ROM.
    private static bool IsPruned(string folder, EmulatorKind kind) =>
        PrunedDirectories.Contains(folder)
        && (kind == EmulatorKind.RetroArch || !string.Equals(folder, "roms", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Battery saves and GCI files can be nested: RetroArch uses per-core folders, DraStic uses
    /// backup/, and Dolphin uses GC/&lt;region&gt;/Card A or Card B. Prefer the emulator's save
    /// subtrees when its whole data folder was granted; direct save-folder grants work too.
    /// </summary>
    private List<DetectedSave> ScanFlatFolder(string root, EmulatorKind kind,
        CancellationToken cancellationToken, ScanState state, Action<DetectedSave>? onSave)
    {
        var results = new List<DetectedSave>();
        void OnFile(Child child)
        {
            state.FilesSeen++;
            if (kind == EmulatorKind.Dolphin && EmulatorSaveHeuristics.IsDolphinMemoryCard(child.Name))
            {
                state.Rejected.Add($"{child.Name}: export Colosseum/XD as GCI with Dolphin's Memory Card Manager, or use GCI Folder for the card slot.");
                state.Trace($"RAW MEMORY CARD {child.Name}: individual GCI export required");
                return;
            }
            if (!EmulatorSaveHeuristics.IsCandidateFileName(child.Name, kind)) return;
            if (TryDetect(child, kind, gameLabel: child.Name, state) is { } save)
            {
                results.Add(save);
                onSave?.Invoke(save);
            }
            else
                state.Rejected.Add(child.Name);
        }

        var rootChildren = ListChildren(root, state);
        string[] preferredFolders = kind switch
        {
            EmulatorKind.Dolphin => ["GC"],
            EmulatorKind.DraStic => ["backup"],
            EmulatorKind.PizzaBoyGba or EmulatorKind.PizzaBoyGbc or EmulatorKind.MyBoy or EmulatorKind.MyOldBoy => ["save", "saves"],
            _ => ["saves"],
        };
        var savesDirs = rootChildren.Where(x => x.IsDirectory && preferredFolders.Contains(x.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (savesDirs.Length > 0)
        {
            state.Siblings = rootChildren.Where(x => !x.IsDirectory).Select(x => x.Name).ToArray();
            foreach (var file in rootChildren.Where(x => !x.IsDirectory))
                OnFile(file);
            foreach (var savesDir in savesDirs)
                FindFilesRecursive(savesDir.Path, FlatMaxDepth, cancellationToken, OnFile, state);
        }
        else
        {
            FindFilesRecursive(root, FlatMaxDepth, cancellationToken, OnFile, state);
        }
        return results;
    }

    /// <summary>
    /// Eden (Switch): saves are files named "main" (or "*.bin" for BDSP) under nand/user/save/…
    /// The user may have granted the files root, nand/, or user/, so each prefix is tried.
    /// </summary>
    private List<DetectedSave> ScanEden(string root, CancellationToken cancellationToken,
        ScanState state, Action<DetectedSave>? onSave)
    {
        string[][] prefixes = [["nand", "user", "save"], ["user", "save"], ["save"]];
        string? saveDir = null;
        foreach (var prefix in prefixes)
        {
            state.Trace($"Trying Eden prefix {string.Join('/', prefix)}");
            saveDir = NavigatePath(root, prefix, state);
            state.Trace(saveDir is null ? "Prefix not found" : $"Save root found: {saveDir}");
            if (saveDir is not null)
                break;
        }
        if (saveDir is null)
        {
            state.Trace("EDEN FAILURE: none of nand/user/save, user/save, or save exists under the granted root");
            return [];
        }

        var results = new List<DetectedSave>();
        FindFilesRecursive(saveDir, EdenMaxDepth, cancellationToken, child =>
        {
            state.FilesSeen++;
            if (!EmulatorSaveHeuristics.IsEdenSaveFileName(child.Name))
            {
                state.Trace($"SKIP file name={child.Name} path={child.Path}");
                return;
            }
            state.Trace($"CANDIDATE name={child.Name} modified={child.LastModified?.ToString("O") ?? "unknown"} path={child.Path}");
            var label = EmulatorSaveHeuristics.GuessSwitchGameLabel(child.Path);
            if (TryDetect(child, EmulatorKind.Eden, label, state) is { } save)
            {
                results.Add(save);
                onSave?.Invoke(save);
            }
            else
                state.Rejected.Add(child.Name);
        }, state, diagnostic: true);
        return results;
    }

    /// <summary>
    /// Citra-family 3DS (Azahar, Citra MMJ): sdmc/Nintendo 3DS/&lt;ID0&gt;/&lt;ID1&gt;/title/00040000/&lt;game&gt;/data/00000001/main,
    /// with the root resolved from whichever level the user granted (see <see cref="ThreeDsSaveLayout"/>).
    /// </summary>
    private List<DetectedSave> ScanThreeDs(string root, EmulatorKind kind,
        CancellationToken cancellationToken, ScanState state, Action<DetectedSave>? onSave)
    {
        var results = new List<DetectedSave>();
        IEnumerable<TreeEntry<string>> List(string directory) =>
            ListChildren(directory, state).Select(x => new TreeEntry<string>(x.Name, x.IsDirectory, x.Path));
        var rootName = ThreeDsSaveLayout.LastSegment(root);
        foreach (var hit in ThreeDsSaveLayout.FindMainSaves(root, rootName, List, state.Trace, cancellationToken))
        {
            state.FilesSeen++;
            var info = new FileInfo(hit.File);
            var main = new Child(hit.File, info.Name, false, ModifiedOf(info));
            if (TryDetect(main, kind, gameLabel: $"3DS save ({hit.TitleId})", state) is { } save)
            {
                results.Add(save);
                onSave?.Invoke(save);
            }
            else
                state.Rejected.Add($"3DS save {hit.TitleId}");
        }
        return results;
    }

    /// <summary>Reads a candidate's bytes and reports it only if the engine can parse them.</summary>
    private DetectedSave? TryDetect(Child child, EmulatorKind kind, string gameLabel, ScanState state)
    {
        try
        {
            state.Trace($"DocumentPath={child.Path}");
            // A backup or a sync tool's old version is still a save the player may want: it
            // is found like the others and only starts off the shelf, in Hidden saves.
            var isCopy = SaveNoise.IsCopyPath(PathBelowRoot(state.Root, child.Path),
                kind == EmulatorKind.DraStic ? "backup" : null);
            if (isCopy) state.Trace("COPY (backup or hidden folder): starts in Hidden saves");

            // Saves are small; a matching extension on a ROM/archive must not stall the scan.
            var bytes = ReadBounded(child.Path);
            if (bytes is null)
            {
                state.Trace($"REJECT larger than {MaxSaveBytes} bytes");
                return null;
            }
            if (bytes.Length == 0)
            {
                state.Trace("REJECT zero-byte file");
                return null;
            }

            var headerLength = Math.Min(16, bytes.Length);
            var bdspSize = bytes.Length is 956456 or 973856 or 978316 or 979108;
            state.Trace($"BYTES length={bytes.Length} header16={Convert.ToHexString(bytes.AsSpan(0, headerLength))} knownBdspSize={bdspSize}");

            // Describe consumes a copy: the engine decrypts Switch saves in place during parsing.
            var description = engine.TryDescribe(bytes, child.Name);
            if (description is null)
            {
                state.Trace($"PARSER REJECTED: {engine.DescribeRejection(bytes) ?? "ISaveEngine.TryDescribe returned null"}");
                return null;
            }
            state.Trace($"PARSER ACCEPTED game={description.GameName} generation={description.Generation} trainer={description.TrainerName} playTime={description.PlayTime}");

            // The label comes from the bytes alone; the save/ROM name is only a Rename prefill.
            var romName = EmulatorSaveHeuristics.FindSiblingRom(child.Name, state.Siblings);
            var guess = SaveIdentityRules.Guess(description.GameName, description.Generation, child.Name, romName, gameLabel);
            state.Trace($"IDENTITY label={guess.Label} family={guess.Family} format={guess.Format} language={description.Language ?? "-"} rom={romName ?? "-"}");
            return new DetectedSave(
                child.Path,
                child.Name,
                guess.Label,
                kind,
                EmulatorSaveHeuristics.RequiresExtraCare(kind),
                child.LastModified,
                description.Generation,
                description.TrainerName,
                description.PlayTime,
                guess,
                romName,
                EmulatorSaveHeuristics.FolderHint(child.Path),
                Language: description.Language,
                IsCopy: isCopy,
                TrainerId: description.TrainerId);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            state.Trace($"CANDIDATE EXCEPTION {error}");
            return null; // unreadable candidates are simply not saves
        }
    }

    /// <summary>The whole file, or null when it is larger than any save can be.</summary>
    private static byte[]? ReadBounded(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaxSaveBytes) return null;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.Length > MaxSaveBytes ? null : buffer.ToArray();
    }

    /// <summary>The part of a path below the linked folder ("mGBA/Emerald.srm").</summary>
    private static string PathBelowRoot(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static DateTimeOffset? ModifiedOf(FileSystemInfo info) =>
        info.Exists ? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero) : null;

    private void FindFilesRecursive(string parent, int maxDepth, CancellationToken cancellationToken,
        Action<Child> onFile, ScanState state, bool diagnostic = false)
    {
        if (maxDepth <= 0)
        {
            if (diagnostic) state.Trace($"DEPTH LIMIT reached at {parent}");
            return;
        }
        var children = ListChildren(parent, state);
        var siblings = children.Where(x => !x.IsDirectory).Select(x => x.Name).ToArray();
        if (diagnostic) state.Trace($"WALK depthRemaining={maxDepth} parent={parent} children={children.Count}");
        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (child.IsDirectory)
            {
                if (diagnostic) state.Trace($"DIR name={child.Name} path={child.Path}");
                if (IsPruned(child.Name, state.Kind))
                {
                    if (diagnostic) state.Trace($"PRUNED directory {child.Name}");
                    continue;
                }
                FindFilesRecursive(child.Path, maxDepth - 1, cancellationToken, onFile, state, diagnostic);
            }
            else
            {
                state.Siblings = siblings;
                onFile(child);
            }
        }
    }

    private static string? NavigatePath(string from, string[] segments, ScanState state)
    {
        var current = from;
        foreach (var segment in segments)
        {
            var children = ListChildren(current, state);
            state.Trace($"At {current}: {children.Count} children [{string.Join(", ", children.Select(x => x.IsDirectory ? x.Name + "/" : x.Name))}]");
            var next = children.FirstOrDefault(x => x.IsDirectory && x.Name == segment);
            if (next is null)
            {
                state.Trace($"Missing directory segment '{segment}'");
                return null;
            }
            state.Trace($"Matched '{segment}' -> {next.Path}");
            current = next.Path;
        }
        return current;
    }

    /// <summary>
    /// The entries directly inside a folder. A folder that cannot be read yields nothing and is
    /// named in the diagnostics; symbolic links to directories are listed as neither files nor
    /// folders, so a link cycle cannot trap the walk.
    /// </summary>
    private static List<Child> ListChildren(string directory, ScanState state)
    {
        var results = new List<Child>();
        try
        {
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo dir)
                {
                    if (dir.LinkTarget is not null)
                    {
                        state.Trace($"SKIP symbolic link to directory {dir.FullName}");
                        continue;
                    }
                    results.Add(new Child(dir.FullName, dir.Name, true, null));
                }
                else
                    results.Add(new Child(entry.FullName, entry.Name, false, ModifiedOf(entry)));
            }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            state.Trace($"UNREADABLE directory {directory}: {error.Message}");
        }
        return results;
    }
}

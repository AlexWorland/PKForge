namespace PKForge.Linux.Core;

public sealed record BrowserRow(string Name, string FullPath, bool IsDirectory);

/// <summary>
/// The state behind the in-app file browser: where it is and which rows it shows. The listing
/// comes in as a function so the model is tested without a disk. <see cref="Current"/> is null
/// while the list of roots is showing; going up from a root returns there.
/// </summary>
public sealed class FileBrowserModel
{
    private readonly Func<string, IEnumerable<(string Name, bool IsDirectory)>> _list;
    private readonly IReadOnlyList<string> _roots;

    public FileBrowserModel(Func<string, IEnumerable<(string Name, bool IsDirectory)>> list, IReadOnlyList<string> roots)
    {
        _list = list;
        _roots = roots;
        Rows = BuildRows();
    }

    public string? Current { get; private set; }

    public IReadOnlyList<BrowserRow> Rows { get; private set; }

    /// <summary>Opens a directory row or the ".." row; a file row does nothing (the page decides).</summary>
    public void Enter(int row)
    {
        if (row < 0 || row >= Rows.Count) return;
        var picked = Rows[row];
        if (picked.Name == ".." && Current is not null) { Up(); return; }
        if (!picked.IsDirectory) return;
        Current = picked.FullPath;
        Rows = BuildRows();
    }

    public void Up()
    {
        if (Current is null) return;
        var parent = _roots.Contains(Current, StringComparer.Ordinal) ? null : Path.GetDirectoryName(Current);
        Current = string.IsNullOrEmpty(parent) ? null : parent;
        Rows = BuildRows();
    }

    private List<BrowserRow> BuildRows()
    {
        if (Current is null)
            return _roots.Select(root => new BrowserRow(root, root, true)).ToList();

        var entries = _list(Current)
            .Where(e => !e.Name.StartsWith('.'))
            .OrderBy(e => e.IsDirectory ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => new BrowserRow(e.Name, Path.Combine(Current, e.Name), e.IsDirectory));
        var parent = _roots.Contains(Current, StringComparer.Ordinal) ? Current : Path.GetDirectoryName(Current) ?? Current;
        return [new BrowserRow("..", parent, true), .. entries];
    }
}

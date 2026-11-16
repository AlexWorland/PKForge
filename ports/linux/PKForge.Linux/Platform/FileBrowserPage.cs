using PKForge.App.Services;
using PKForge.App.Theme;
using PKForge.Chrome;
using PKForge.Linux.Core;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PKForge.App.Views;

/// <summary>
/// The in-app replacement for the system file dialogs, which a handheld without a desktop does
/// not have. It reuses the app's picker painter, so it looks like every other list: d-pad moves,
/// A opens a folder or picks a file, B goes up (and cancels at the list of roots), Start picks the
/// highlighted folder in folder mode or finishes a multi-file selection. Touch: tap a row to aim
/// it, tap it again to open it, drag to scroll, tap the keys at the foot.
/// </summary>
public sealed class FileBrowserPage : ContentPage, IPadHandler
{
    private enum Mode { File, Files, Folder }

    private readonly Mode _mode;
    private readonly string _title;
    private readonly FileBrowserModel _model;
    private readonly TaskCompletionSource<IReadOnlyList<string>> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SKCanvasView _canvas = new() { EnableTouchEvents = true };
    private readonly HashSet<string> _marked = new(StringComparer.Ordinal);
    private int _index;
    private float _offset;
    private bool _keepPending = true;
    private bool _armed;
    private bool _closed;
    private IReadOnlyList<string>? _final;
    private PickerHits? _hits;
    private SKPoint? _pressedAt;
    private float _pressedOffset;
    private bool _dragged;

    private FileBrowserPage(Mode mode, string title)
    {
        _mode = mode;
        _title = title;
        _model = new FileBrowserModel(List, LinuxPickerRoots());
        Title = title;
        BackgroundColor = UiTokens.Housing;
        NavigationPage.SetHasNavigationBar(this, false);
        _canvas.PaintSurface += PaintGuard.Wrap("FileBrowserPage.OnPaint", OnPaint);
        _canvas.Touch += OnTouch;
        Content = new Grid { Children = { DsChrome.GridBackground(), _canvas } };
    }

    public static async Task<string?> PickFileAsync(INavigation nav, string title) =>
        (await ShowAsync(nav, new FileBrowserPage(Mode.File, title))).FirstOrDefault();

    public static Task<IReadOnlyList<string>> PickFilesAsync(INavigation nav, string title) =>
        ShowAsync(nav, new FileBrowserPage(Mode.Files, title));

    public static async Task<string?> PickFolderAsync(INavigation nav, string title) =>
        (await ShowAsync(nav, new FileBrowserPage(Mode.Folder, title))).FirstOrDefault();

    /// <summary>The page that is on screen now: pickers are called from view models that hold no page.</summary>
    public static INavigation? CurrentNavigation =>
        Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;

    private static async Task<IReadOnlyList<string>> ShowAsync(INavigation nav, FileBrowserPage page)
    {
        await MainThread.InvokeOnMainThreadAsync(() => nav.PushAsync(page, animated: false));
        return await page._result.Task;
    }

    private static IReadOnlyList<string> LinuxPickerRoots()
    {
        var extra = (Environment.GetEnvironmentVariable("PKFORGE_SAVE_ROOTS") ?? "")
            .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return LinuxPaths.SuggestedRoots(Directory.Exists,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), extra);
    }

    private static IEnumerable<(string Name, bool IsDirectory)> List(string path)
    {
        try
        {
            return new DirectoryInfo(path).EnumerateFileSystemInfos()
                .Select(e => (e.Name, e.Attributes.HasFlag(FileAttributes.Directory))).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("filebrowser", $"Cannot list {path}: {ex.Message}");
            return [];
        }
    }

    // ---------- Lifecycle ----------

    protected override void OnAppearing()
    {
        base.OnAppearing();
        IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Push(this);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Remove(this);
        // Left by any route other than a pick or B: that is a cancel.
        _result.TrySetResult(_final ?? []);
    }

    private void Finish(IReadOnlyList<string> picked)
    {
        if (_closed) return;
        _closed = true;
        _final = picked;
        // The page leaves first and the caller hears the answer after: the caller usually opens
        // its next screen at once, and a late pop would remove that one instead.
        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                if (ReferenceEquals(Navigation.NavigationStack.LastOrDefault(), this)) await Navigation.PopAsync(animated: false);
                else Navigation.RemovePage(this);
            }
            catch (Exception ex) { AppLog.Warn("filebrowser", $"Closing the browser failed: {ex.Message}"); }
            finally { _result.TrySetResult(picked); }
        });
    }

    // ---------- Painting ----------

    private BrowserRow? Current => _index >= 0 && _index < _model.Rows.Count ? _model.Rows[_index] : null;

    private PickerScene Scene()
    {
        var keys = new List<(string, string)> { ("A", _mode == Mode.Files ? "Open/Mark" : "Open") };
        if (_mode == Mode.Folder) keys.Add(("Start", "Pick folder"));
        if (_mode == Mode.Files) keys.Add(("Start", "Done"));
        keys.Add(("L/R", "Page"));
        keys.Add(("B", _model.Current is null ? "Cancel" : "Up"));
        return new PickerScene(_title, _model.Current ?? "Locations", _model.Rows.Count, LineOf, _index, _offset, [],
            StatusText(), keys, false);
    }

    private PickerLine LineOf(int index)
    {
        var row = _model.Rows[index];
        var label = row.IsDirectory ? row.Name + "/" : row.Name;
        return new PickerLine(label, Tag: _marked.Contains(row.FullPath) ? "MARKED" : null);
    }

    private string StatusText()
    {
        if (Current is not { } row) return "(nothing here)";
        if (row.IsDirectory) return _mode == Mode.Folder && row.Name != ".." ? $"Start picks: {row.Name}" : $"A opens: {row.Name}";
        return _mode == Mode.Files ? $"A marks: {row.Name}" : $"A picks: {row.Name}";
    }

    private void OnPaint(object? sender, SKPaintSurfaceEventArgs args)
    {
        args.Surface.Canvas.Clear(SKColors.Transparent);
        var face = BoxBrowserPage.PixelTypeface();
        _hits = PickerPaint.Paint(args.Surface.Canvas, args.Info, Scene(), face);
        var before = _offset;
        if (_keepPending) KeepInView();
        _keepPending = false;
        _offset = Math.Clamp(_offset, 0f, _hits.MaxOffset);
        if (MathF.Abs(before - _offset) > 0.01f)
        {
            args.Surface.Canvas.Clear(SKColors.Transparent);
            _hits = PickerPaint.Paint(args.Surface.Canvas, args.Info, Scene(), face);
        }
    }

    private void KeepInView()
    {
        if (_hits is not { } hits) return;
        var top = _index * hits.RowHeight;
        if (top < _offset) _offset = top;
        else if (top + hits.RowHeight > _offset + hits.ListHeight) _offset = top + hits.RowHeight - hits.ListHeight;
    }

    private void Invalidate() => _canvas.InvalidateSurface();

    // ---------- Touch ----------

    private void OnTouch(object? sender, SKTouchEventArgs args)
    {
        args.Handled = true;
        if (_hits is not { } hits) return;
        var at = args.Location;
        switch (args.ActionType)
        {
            case SKTouchAction.Pressed:
                _pressedAt = at;
                _pressedOffset = _offset;
                _dragged = false;
                return;
            case SKTouchAction.Moved when _pressedAt is { } start && hits.List.Contains(start):
            {
                var dy = (start.Y - at.Y) / hits.Unit;
                if (!_dragged && MathF.Abs(dy) < hits.RowHeight / 4f) return;
                _dragged = true;
                _offset = Math.Clamp(_pressedOffset + dy, 0f, hits.MaxOffset);
                Invalidate();
                return;
            }
            case SKTouchAction.Released:
                var tapped = !_dragged;
                _pressedAt = null;
                if (tapped) OnTap(hits, at);
                return;
            case SKTouchAction.Cancelled:
                _pressedAt = null;
                return;
        }
    }

    private void OnTap(PickerHits hits, SKPoint at)
    {
        foreach (var (key, rect) in hits.Keys)
        {
            if (!rect.Contains(at)) continue;
            switch (key)
            {
                case "A": Activate(); break;
                case "Start": PickHere(); break;
                case "L/R": Move(PageRows); break;
                default: GoBack(); break;
            }
            return;
        }
        foreach (var (index, rect) in hits.Rows)
        {
            if (!rect.Contains(at) || !hits.List.Contains(at)) continue;
            if (index == _index && _armed) { Activate(); return; }
            _index = index;
            _armed = true;
            _keepPending = true;
            Invalidate();
            return;
        }
    }

    // ---------- Pad ----------

    public bool OnPadButton(PadButton button)
    {
        switch (button)
        {
            case PadButton.Up: Move(-1); break;
            case PadButton.Down: Move(1); break;
            case PadButton.L: case PadButton.Left: Move(-PageRows); break;
            case PadButton.R: case PadButton.Right: Move(PageRows); break;
            case PadButton.A: Activate(); break;
            case PadButton.B: GoBack(); break;
            case PadButton.Start: PickHere(); break;
        }
        return true; // the browser owns the pad while open
    }

    private int PageRows => _hits is { } hits ? Math.Max(1, (int)(hits.ListHeight / hits.RowHeight) - 1) : 8;

    private void Move(int delta)
    {
        if (_model.Rows.Count == 0) return;
        _index = Math.Clamp(_index + delta, 0, _model.Rows.Count - 1);
        _armed = false;
        _keepPending = true;
        Invalidate();
    }

    private void Activate()
    {
        if (Current is not { } row) return;
        if (row.IsDirectory)
        {
            Enter(_index);
            return;
        }
        if (_mode == Mode.Files)
        {
            if (!_marked.Remove(row.FullPath)) _marked.Add(row.FullPath);
            Invalidate();
        }
        else if (_mode == Mode.File) Finish([row.FullPath]);
    }

    private void Enter(int row)
    {
        _model.Enter(row);
        Reset(0);
    }

    private void GoBack()
    {
        if (_model.Current is not { } here) { Finish([]); return; }
        _model.Up();
        // The cursor lands on the folder just left, so Down/A can step back in.
        Reset(Math.Max(0, _model.Rows.ToList().FindIndex(r => r.FullPath == here)));
    }

    private void Reset(int index)
    {
        _index = index;
        _offset = 0;
        _armed = false;
        _keepPending = true;
        Invalidate();
    }

    private void PickHere()
    {
        if (_mode == Mode.Folder)
        {
            // ".." stands for the folder being viewed; a root row or a folder row is itself.
            if (Current is { } row && row.IsDirectory && row.Name != "..") Finish([row.FullPath]);
            else if (_model.Current is { } here) Finish([here]);
        }
        else if (_mode == Mode.Files)
        {
            if (_marked.Count > 0) Finish([.. _marked.Order(StringComparer.Ordinal)]);
            else if (Current is { IsDirectory: false } row) Finish([row.FullPath]);
        }
    }
}

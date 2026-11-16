using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using PKForge.App;
using PKForge.Domain;
using PKForge.App.Services;
using PKForge.App.Views;
using PKForge.Linux.Core;

namespace PKForge.Linux;

// A handheld without a desktop has no share sheet, file dialog, browser or vibration motor.
// These stand-ins keep the shared screens working: Share copies the file into an export
// folder and says where, FilePicker opens the in-app browser, Clipboard stays in memory,
// Launcher declines (callers already handle false), haptics do nothing.
internal static class LinuxEssentials
{
    /// <summary>Where Share puts what the app shares.</summary>
    public static string ExportRoot => Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "exports");

    public static void Install()
    {
        Set(typeof(Share), typeof(IShare), new LinuxShare());
        Set(typeof(FilePicker), typeof(IFilePicker), new LinuxFilePicker());
        Set(typeof(Clipboard), typeof(IClipboard), new LinuxClipboard());
        Set(typeof(Launcher), typeof(ILauncher), new LinuxLauncher());
        Set(typeof(HapticFeedback), typeof(IHapticFeedback), new LinuxHaptics());
    }

    /// <summary>One line per service: which implementation is live. Used to prove the install stuck.</summary>
    public static void LogActive(string when)
    {
        void Line(string name, object impl) => AppLog.Info("essentials", $"({when}) {name}.Default = {impl.GetType().FullName}");
        Line("Share", Share.Default);
        Line("FilePicker", FilePicker.Default);
        Line("Clipboard", Clipboard.Default);
        Line("Launcher", Launcher.Default);
        Line("HapticFeedback", HapticFeedback.Default);
    }

    private static void Set(Type essential, Type contract, object implementation) =>
        EssentialsFallbacks.SetStatic(essential, "SetDefault", contract, implementation);

    internal static void Notice(string text, NoticeTone tone)
    {
        AppLog.Info("essentials", text);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                if (page is NavigationPage nav) page = nav.CurrentPage;
                if (page is not ContentPage { Content: Grid host }) return;
                var toast = host.Children.OfType<StatusToast>().FirstOrDefault();
                if (toast is null) { toast = new StatusToast(); host.Add(toast); }
                toast.Show(text, tone);
            }
            catch (Exception ex) { AppLog.Warn("essentials", $"Toast failed: {ex.Message}"); }
        });
    }
}

internal sealed class LinuxShare : IShare
{
    public Task RequestAsync(ShareTextRequest request)
    {
        var text = string.IsNullOrEmpty(request.Uri) ? request.Text : $"{request.Text}\n{request.Uri}".Trim();
        var path = Path.Combine(LinuxEssentials.ExportRoot, "shared-text.txt");
        Directory.CreateDirectory(LinuxEssentials.ExportRoot);
        // The text goes through the same naming rule as files, from a scratch file.
        var scratch = Path.Combine(Path.GetTempPath(), Path.GetFileName(path));
        File.WriteAllText(scratch, text ?? "");
        try { Report([ExportFolder.Copy(scratch, LinuxEssentials.ExportRoot, DateTimeOffset.Now)]); }
        finally { File.Delete(scratch); }
        return Task.CompletedTask;
    }

    public Task RequestAsync(ShareFileRequest request)
    {
        Report([Export(request.File!)]);
        return Task.CompletedTask;
    }

    public Task RequestAsync(ShareMultipleFilesRequest request)
    {
        Report([.. request.Files!.Select(Export)]);
        return Task.CompletedTask;
    }

    private static string Export(ShareFile file) =>
        ExportFolder.Copy(file.FullPath, LinuxEssentials.ExportRoot, DateTimeOffset.Now);

    private static void Report(IReadOnlyList<string> copies) =>
        LinuxEssentials.Notice(copies.Count == 1 ? $"Saved to {copies[0]}" : $"Saved {copies.Count} files to {LinuxEssentials.ExportRoot}",
            NoticeTone.Success);
}

internal sealed class LinuxFilePicker : IFilePicker
{
    public async Task<FileResult?> PickAsync(PickOptions? options = null)
    {
        var nav = FileBrowserPage.CurrentNavigation;
        if (nav is null) return null;
        var path = await FileBrowserPage.PickFileAsync(nav, options?.PickerTitle ?? "Choose a file");
        return path is null ? null : new FileResult(path);
    }

    public async Task<IEnumerable<FileResult>?> PickMultipleAsync(PickOptions? options = null)
    {
        var nav = FileBrowserPage.CurrentNavigation;
        if (nav is null) return [];
        var paths = await FileBrowserPage.PickFilesAsync(nav, options?.PickerTitle ?? "Choose files");
        return paths.Select(p => new FileResult(p)).ToList();
    }
}

internal sealed class LinuxClipboard : IClipboard
{
    private string? _text;

    public bool HasText => !string.IsNullOrEmpty(_text);

    public event EventHandler<EventArgs>? ClipboardContentChanged;

    public Task<string?> GetTextAsync() => Task.FromResult(_text);

    public Task SetTextAsync(string? text)
    {
        _text = text;
        ClipboardContentChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}

internal sealed class LinuxLauncher : ILauncher
{
    public Task<bool> CanOpenAsync(Uri uri) => Task.FromResult(false);
    public Task<bool> OpenAsync(Uri uri) => Task.FromResult(false);
    public Task<bool> OpenAsync(OpenFileRequest request) => Task.FromResult(false);
    public Task<bool> TryOpenAsync(Uri uri) => Task.FromResult(false);
}

internal sealed class LinuxHaptics : IHapticFeedback
{
    public bool IsSupported => false;
    public void Perform(HapticFeedbackType type) { }
}

using PKForge.Domain;

namespace PKForge.App.Services;

// Stand-in for src/PKForge.App/Services/AppUpdateService.cs. Linux has no APK installer, so no
// update is ever offered and nothing is downloaded.
public sealed class AppUpdateService
{
    public string SkippedVersion => "";

    public static bool CanInstallUpdates() => false;

    public Task<AppUpdateCheck> CheckAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AppUpdateCheck(false, null, "Updates are not offered on Linux yet."));

    public bool ShouldPromptAutomatically(AvailableAppUpdate update) => false;

    public void DontRemindMe(AvailableAppUpdate update) { }

    public Task<AppUpdateInstallResult> DownloadAndInstallAsync(
        AvailableAppUpdate update,
        Action<long, long> onProgress,
        CancellationToken cancellationToken) =>
        Task.FromResult(AppUpdateInstallResult.ReleasePageOpened);

    public static void OpenInstallPermissionSettings() { }
}

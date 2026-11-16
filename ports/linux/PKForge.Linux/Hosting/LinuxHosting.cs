using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Hosting;
using PKForge.Domain;
using Microsoft.Maui.Platform.Linux.Hosting;

namespace PKForge.App;

// src/PKForge.App/MauiProgram.cs calls `builder.UseMauiApp<App>().UseSkiaSharp()` from inside
// `namespace PKForge.App;` and imports SkiaSharp.Views.Maui.Controls.Hosting at the top of the
// file. C# resolves an extension method by walking outward from the innermost namespace, and
// the enclosing namespace's own types are searched before the using directives of the outer
// compilation unit. So the PKForge.App.LinuxHosting.UseSkiaSharp declared here wins over the
// SkiaSharp one, MauiProgram.cs stays unedited, and the Linux head hooks in OpenMaui and its
// platform services at the exact point where the app already configures SkiaSharp.
public static class LinuxHosting
{
    public static MauiAppBuilder UseSkiaSharp(this MauiAppBuilder builder)
    {
        SkiaSharp.Views.Maui.Controls.Hosting.AppHostBuilderExtensions.UseSkiaSharp(builder);
        builder.UseLinux();
        RegisterPlatformServices(builder.Services);
        StartPadScript();
        return builder;
    }

    // Later tasks register the Linux implementations of the app's platform seams here.
    internal static void RegisterPlatformServices(IServiceCollection services)
    {
        services.AddSingleton<Platforms.Android.MusicPlayer>();
        services.AddSingleton<PKForge.Domain.IMusicPlayer>(sp => sp.GetRequiredService<Platforms.Android.MusicPlayer>());
        // Temporary placeholders (Placeholders.cs); tasks 6 to 12 replace them one by one.
        services.AddSingleton<ISaveFileAccess, PKForge.Linux.Core.LinuxSaveFileAccess>();
        services.AddSingleton<IDocumentPicker, PlaceholderDocumentPicker>();
        services.AddSingleton<IFolderPicker, PlaceholderFolderPicker>();
        // Shared code reaches IFolderTreeAccess by casting IFolderFileAccess, so one class serves both.
        services.AddSingleton<IFolderFileAccess, PKForge.Linux.Core.LinuxFolderFileAccess>();
        services.AddSingleton<IEmulatorDetectionService, PlaceholderEmulatorDetectionService>();
        services.AddSingleton<ISecondaryDisplayHost, PlaceholderSecondaryDisplayHost>();
    }

    // Test hook: PKFORGE_PAD_SCRIPT="Down,Down,A" sends each PadButton through GamepadRouter.Dispatch
    // on the main thread, 500 ms apart, starting 3 seconds after launch.
    private static void StartPadScript()
    {
        var script = Environment.GetEnvironmentVariable("PKFORGE_PAD_SCRIPT");
        if (string.IsNullOrWhiteSpace(script)) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            Services.AppLog.Info("padscript", $"Running {script}");
            foreach (var name in script.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Enum.TryParse<Services.PadButton>(name, ignoreCase: true, out var button))
                {
                    Services.AppLog.Warn("padscript", $"Unknown button '{name}'");
                    continue;
                }
                try
                {
                    var router = IPlatformApplication.Current?.Services.GetService<Services.GamepadRouter>();
                    var handled = await MainThread.InvokeOnMainThreadAsync(() => router?.Dispatch(button) ?? false);
                    Services.AppLog.Info("padscript", $"{button} handled={handled}");
                }
                catch (Exception ex) { Services.AppLog.Error("padscript", $"{button} failed", ex); }
                await Task.Delay(500);
            }
        });
    }
}

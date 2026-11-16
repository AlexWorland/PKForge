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
        PKForge.Linux.LinuxEssentials.Install();
        PKForge.Linux.OskHook.Install();
        StartEssentialsProbe();
        RegisterPlatformServices(builder.Services);
        StartPadScript();
        StartGamepad();
        return builder;
    }

    // Later tasks register the Linux implementations of the app's platform seams here.
    internal static void RegisterPlatformServices(IServiceCollection services)
    {
        services.AddSingleton<Platforms.Android.MusicPlayer>();
        services.AddSingleton<PKForge.Domain.IMusicPlayer>(sp => sp.GetRequiredService<Platforms.Android.MusicPlayer>());
        // Temporary placeholders (Placeholders.cs); tasks 6 to 12 replace them one by one.
        services.AddSingleton<ISaveFileAccess, PKForge.Linux.Core.LinuxSaveFileAccess>();
        services.AddSingleton<IDocumentPicker, LinuxDocumentPicker>();
        services.AddSingleton<IFolderPicker, LinuxFolderPicker>();
        // Shared code reaches IFolderTreeAccess by casting IFolderFileAccess, so one class serves both.
        services.AddSingleton<IFolderFileAccess, PKForge.Linux.Core.LinuxFolderFileAccess>();
        services.AddSingleton<IEmulatorDetectionService, PKForge.Linux.Core.LinuxEmulatorScanner>();
        services.AddSingleton<ISecondaryDisplayHost, PlaceholderSecondaryDisplayHost>();
    }

    // OpenMaui installs some of its own Essentials late, inside LinuxApplication.Run. Once the first
    // window exists, log which implementation each service resolves to, so a lost install shows.
    private static void StartEssentialsProbe() => _ = Task.Run(async () =>
    {
        for (var i = 0; i < 120; i++)
        {
            await Task.Delay(250);
            if (Application.Current?.Windows.Count > 0) break;
        }
        await MainThread.InvokeOnMainThreadAsync(() => PKForge.Linux.LinuxEssentials.LogActive("first window"));
        await Task.Delay(5000);
        await MainThread.InvokeOnMainThreadAsync(() => PKForge.Linux.LinuxEssentials.LogActive("5 s later"));
    });

    private static PKForge.Linux.Core.SdlGamepadPump? _pump;

    // The handheld's built-in pad is a joystick device the window system does not deliver, so SDL2
    // reads it and feeds the same GamepadRouter the Android key events reach. Started once the first
    // window exists, because the router and the dispatcher are only there by then.
    private static void StartGamepad() => _ = Task.Run(async () =>
    {
        for (var i = 0; i < 120; i++)
        {
            await Task.Delay(250);
            if (Application.Current?.Windows.Count > 0) break;
        }
        try
        {
            var layout = PKForge.Linux.Core.PadMapping.LayoutFrom(Environment.GetEnvironmentVariable("PKFORGE_PAD_LAYOUT"));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Services.GamepadRouter? Router() => IPlatformApplication.Current?.Services.GetService<Services.GamepadRouter>();
            // All of these run on the main thread (the pump posts, the timer is a dispatcher timer).
            var repeater = new PKForge.Linux.Core.PadRepeater(
                button => Router()?.Dispatch(button),
                () => Router()?.TopPagesWithShoulders == true);
            var pump = new PKForge.Linux.Core.SdlGamepadPump(
                MainThread.BeginInvokeOnMainThread,
                (sdl, down) =>
                {
                    if (PKForge.Linux.Core.PadMapping.Resolve(sdl, layout) is not { } button) return;
                    if (down) { repeater.Press(button, clock.ElapsedMilliseconds); return; }
                    repeater.Release(button);
                    Router()?.DispatchRelease(button);
                },
                (lx, ly, rx, ry) => Router()?.DispatchStick(lx, ly, rx, ry),
                log: message => Services.AppLog.Info("pad", message));
            if (!pump.Start()) return;   // the pump has logged why
            _pump = pump;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => pump.Stop();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var timer = Application.Current!.Dispatcher.CreateTimer();
                timer.Interval = TimeSpan.FromMilliseconds(16);
                timer.Tick += (_, _) => repeater.Tick(clock.ElapsedMilliseconds);
                timer.Start();
            });
            Services.AppLog.Info("pad", $"Gamepad pump started, layout {layout}");
        }
        catch (Exception ex) { Services.AppLog.Error("pad", "Gamepad pump failed to start", ex); }
    });

    // Test hook: PKFORGE_PAD_SCRIPT="Down,Wait1000,A" sends each PadButton through GamepadRouter.Dispatch
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
                // "Wait2000" pauses that many milliseconds: a dialogue types out at its own pace.
                if (name.StartsWith("Wait", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(4), out var pause))
                {
                    await Task.Delay(pause);
                    continue;
                }
                if (!Enum.TryParse<Services.PadButton>(name, ignoreCase: true, out var button))
                {
                    Services.AppLog.Warn("padscript", $"Unknown button '{name}'");
                    continue;
                }
                try
                {
                    var router = IPlatformApplication.Current?.Services.GetService<Services.GamepadRouter>();
                    var handled = await MainThread.InvokeOnMainThreadAsync(() => router?.Dispatch(button) ?? false);
                    var top = await MainThread.InvokeOnMainThreadAsync(() =>
                        (Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation.NavigationStack.LastOrDefault())?.GetType().Name);
                    Services.AppLog.Info("padscript", $"{button} handled={handled} top={top}");
                }
                catch (Exception ex) { Services.AppLog.Error("padscript", $"{button} failed", ex); }
                await Task.Delay(500);
            }
        });
    }
}

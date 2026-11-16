using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
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
        return builder;
    }

    // Later tasks register the Linux implementations of the app's platform seams here.
    internal static void RegisterPlatformServices(IServiceCollection services)
    {
        services.AddSingleton<Platforms.Android.MusicPlayer>();
        services.AddSingleton<PKForge.Domain.IMusicPlayer>(sp => sp.GetRequiredService<Platforms.Android.MusicPlayer>());
    }
}

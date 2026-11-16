using Microsoft.Maui.Platform.Linux;

namespace PKForge.Linux;

public static class Program
{
    public static void Main(string[] args)
    {
        var app = PKForge.App.MauiProgram.CreateMauiApp();
        LinuxApplication.Run(app, args);
    }
}

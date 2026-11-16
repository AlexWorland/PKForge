using PKForge.Domain;

namespace PKForge.App;

// Stand-ins for src/PKForge.App/Platforms/Android/HomeWidgets.cs (the home-screen widget store
// and renderer). Linux has no home screen widgets: every query answers "none".
public sealed record LastSave(string DocumentId, string Name);

public static class HomeWidgetStore
{
    public static HomeWidgetSettings Settings => new();

    public static void Save(HomeWidgetSettings settings) { }

    public static Dictionary<string, EggWidgetSnapshot> Eggs => [];

    public static LastSave? Last => null;
}

public static class HomeWidgets
{
    public static bool HasAny(HomeWidgetKind kind) => false;

    public static Task CaptureOpenSaveAsync() => Task.CompletedTask;

    public static Task SetPartnerAsync(PartnerChoice? partner) => Task.CompletedTask;

    public static PartnerChoice? PartnerFrom(byte[] data, string? format, string origin, Guid? bankId, string? documentId) => null;
}

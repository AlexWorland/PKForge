namespace PKForge.App;

// Stand-in for src/PKForge.App/Platforms/Android/PokeparkWidget.cs: no widgets on Linux.
public static class PokeparkWidgetPublisher
{
    public static bool HasActiveWidgets() => false;

    public static bool HasActive(Type provider) => false;

    public static Task RefreshAsync(TimeSpan spriteWait, bool always = false) => Task.CompletedTask;
}

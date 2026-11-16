using PKForge.Domain;

namespace PKForge.App.Views;

public enum HomeWidgetTab { Pokepark, Walker, Partner, Egg }

// Stand-in for src/PKForge.App/Views/WidgetMenu.cs: home-screen widgets do not exist on Linux.
public sealed class WidgetMenu
{
    private WidgetMenu() { }

    public static Task ShowAsync(Grid host, HomeWidgetTab start) => Task.CompletedTask;

    public static Task OfferOnceAsync(Grid host, HomeWidgetKind kind, string line) => Task.CompletedTask;
}

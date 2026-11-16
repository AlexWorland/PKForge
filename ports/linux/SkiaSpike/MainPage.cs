using Microsoft.Maui.Controls;
using PKForge.App.Views;

namespace SkiaSpike;

public class MainPage : ContentPage
{
    public MainPage()
    {
        var host = new Grid { BackgroundColor = Color.FromArgb("#14121D") };
        Content = host;
        Loaded += async (_, _) =>
        {
            var names = new[] { "Leftovers", "Choice Band", "Choice Scarf", "Life Orb", "Focus Sash", "Assault Vest", "Rocky Helmet",
                "Sitrus Berry", "Lum Berry", "Expert Belt", "Light Ball", "Eviolite", "Black Sludge", "Shell Bell", "Muscle Band",
                "Wise Glasses", "Scope Lens", "Razor Claw", "King's Rock", "Quick Claw", "Bright Powder", "Soul Dew", "Metronome" };
            var items = names.Select((n, i) => new PickItem(i + 1, n, null, i % 3 == 0 ? "Restores a little HP each turn." : null)
                { Tag = i % 5 == 0 ? "HELD" : null }).ToList();
            Console.WriteLine("SHOW picker");
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                var r = IPlatformApplication.Current!.Services.GetService<PKForge.App.Services.GamepadRouter>()!;
                foreach (var b in new[] { PKForge.App.Services.PadButton.Down, PKForge.App.Services.PadButton.Down, PKForge.App.Services.PadButton.A })
                { var handled = await MainThread.InvokeOnMainThreadAsync(() => r.Dispatch(b)); Console.WriteLine($"PAD {b} handled={handled}"); await Task.Delay(500); }
            });
            var picked = await PickerMenu.ShowAsync(host, "Held item", items, currentId: 4);
            Console.WriteLine("PICKED " + (picked?.Name ?? "(cancelled)"));
        };
    }
}

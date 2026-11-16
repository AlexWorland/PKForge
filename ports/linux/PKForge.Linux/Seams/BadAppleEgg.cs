using PKForge.App.Services;

namespace PKForge.App.Views;

// Stand-in for src/PKForge.App/Views/BadAppleEgg.cs, which plays its song through Android's
// MediaPlayer. The easter egg does nothing on Linux.
public sealed class BadAppleEgg : IPadHandler
{
    private BadAppleEgg() { }

    public static Task PlayAsync(Grid host) => Task.CompletedTask;

    public bool OnPadButton(PadButton button) => true;
}

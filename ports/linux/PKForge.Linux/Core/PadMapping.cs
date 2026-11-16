using PKForge.App.Services;

namespace PKForge.Linux.Core;

/// <summary>The values of SDL_GameControllerButton.</summary>
public enum SdlButton
{
    A = 0, B = 1, X = 2, Y = 3, Back = 4, Guide = 5, Start = 6, LeftStick = 7, RightStick = 8,
    LeftShoulder = 9, RightShoulder = 10, DpadUp = 11, DpadDown = 12, DpadLeft = 13, DpadRight = 14,
}

public enum PadLayout { Nintendo, Xbox }

public static class PadMapping
{
    /// <summary>Maps an SDL button to the app's PadButton. Null for buttons the app has no use for.</summary>
    public static PadButton? Resolve(SdlButton button, PadLayout layout)
    {
        // SDL names buttons by position (A is the bottom one). On a Nintendo-labelled pad the bottom
        // button is printed "B" and the right one "A", so the app's A (confirm) is SDL's B.
        var nintendo = layout == PadLayout.Nintendo;
        return button switch
        {
            SdlButton.A => nintendo ? PadButton.B : PadButton.A,
            SdlButton.B => nintendo ? PadButton.A : PadButton.B,
            SdlButton.X => nintendo ? PadButton.Y : PadButton.X,
            SdlButton.Y => nintendo ? PadButton.X : PadButton.Y,
            SdlButton.Back => PadButton.Select,
            SdlButton.Start => PadButton.Start,
            SdlButton.LeftShoulder => PadButton.L,
            SdlButton.RightShoulder => PadButton.R,
            SdlButton.DpadUp => PadButton.Up,
            SdlButton.DpadDown => PadButton.Down,
            SdlButton.DpadLeft => PadButton.Left,
            SdlButton.DpadRight => PadButton.Right,
            _ => null,
        };
    }

    /// <summary>Reads PKFORGE_PAD_LAYOUT: "xbox" selects Xbox, anything else Nintendo.</summary>
    public static PadLayout LayoutFrom(string? environmentValue) =>
        string.Equals(environmentValue?.Trim(), "xbox", StringComparison.OrdinalIgnoreCase) ? PadLayout.Xbox : PadLayout.Nintendo;
}

using Xunit;
using PKForge.App.Services;
using PKForge.Linux.Core;

namespace PKForge.Linux.Tests;

public class PadMappingTests
{
    [Theory]
    [InlineData(SdlButton.B, PadLayout.Nintendo, PadButton.A)]
    [InlineData(SdlButton.A, PadLayout.Nintendo, PadButton.B)]
    [InlineData(SdlButton.Y, PadLayout.Nintendo, PadButton.X)]
    [InlineData(SdlButton.X, PadLayout.Nintendo, PadButton.Y)]
    [InlineData(SdlButton.A, PadLayout.Xbox, PadButton.A)]
    [InlineData(SdlButton.LeftShoulder, PadLayout.Nintendo, PadButton.L)]
    [InlineData(SdlButton.RightShoulder, PadLayout.Xbox, PadButton.R)]
    [InlineData(SdlButton.Back, PadLayout.Nintendo, PadButton.Select)]
    [InlineData(SdlButton.Start, PadLayout.Nintendo, PadButton.Start)]
    [InlineData(SdlButton.DpadLeft, PadLayout.Xbox, PadButton.Left)]
    public void ResolvesButtons(SdlButton sdl, PadLayout layout, PadButton expected) =>
        Assert.Equal(expected, PadMapping.Resolve(sdl, layout));

    [Fact]
    public void GuideAndStickClicksAreIgnored()
    {
        foreach (var layout in Enum.GetValues<PadLayout>())
            foreach (var b in new[] { SdlButton.Guide, SdlButton.LeftStick, SdlButton.RightStick })
                Assert.Null(PadMapping.Resolve(b, layout));
    }

    [Fact]
    public void LayoutDefaultsToNintendo()
    {
        Assert.Equal(PadLayout.Nintendo, PadMapping.LayoutFrom(null));
        Assert.Equal(PadLayout.Nintendo, PadMapping.LayoutFrom(""));
        Assert.Equal(PadLayout.Nintendo, PadMapping.LayoutFrom("weird"));
        Assert.Equal(PadLayout.Xbox, PadMapping.LayoutFrom("xbox"));
    }
}

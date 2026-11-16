using Xunit;
using PKForge.App.Services;
using PKForge.Linux.Core;

namespace PKForge.Linux.Tests;

public class PadRepeaterTests
{
    [Fact]
    public void DirectionFiresOnceThenRepeatsAfter320Then80()
    {
        var fired = new List<PadButton>();
        var r = new PadRepeater(fired.Add, () => true);
        r.Press(PadButton.Down, 0);
        Assert.Single(fired);
        r.Tick(319); Assert.Single(fired);
        r.Tick(320); Assert.Equal(2, fired.Count);
        r.Tick(399); Assert.Equal(2, fired.Count);
        r.Tick(400); Assert.Equal(3, fired.Count);
    }

    [Fact]
    public void ShoulderRepeatsAfter380Then140OnlyWhenAllowed()
    {
        var fired = new List<PadButton>();
        var r = new PadRepeater(fired.Add, () => true);
        r.Press(PadButton.L, 0);
        r.Tick(379); Assert.Single(fired);
        r.Tick(380); Assert.Equal(2, fired.Count);
        r.Tick(519); Assert.Equal(2, fired.Count);
        r.Tick(520); Assert.Equal(3, fired.Count);

        var none = new List<PadButton>();
        var blocked = new PadRepeater(none.Add, () => false);
        blocked.Press(PadButton.L, 0);
        blocked.Tick(1000);
        Assert.Single(none);
    }

    [Fact]
    public void ShoulderRepeatStopsWhenTheScreenStopsPaging()
    {
        var allowed = true;
        var fired = new List<PadButton>();
        var r = new PadRepeater(fired.Add, () => allowed);
        r.Press(PadButton.R, 0);
        r.Tick(380); Assert.Equal(2, fired.Count);
        allowed = false;
        r.Tick(520); r.Tick(5000);
        Assert.Equal(2, fired.Count);
    }

    [Fact]
    public void ReleaseStopsTheRepeat()
    {
        var fired = new List<PadButton>();
        var r = new PadRepeater(fired.Add, () => true);
        r.Press(PadButton.Right, 0);
        r.Release(PadButton.Right);
        r.Tick(5000);
        Assert.Single(fired);
    }

    [Fact]
    public void ReleasingAnotherButtonKeepsTheRepeat()
    {
        var fired = new List<PadButton>();
        var r = new PadRepeater(fired.Add, () => true);
        r.Press(PadButton.Right, 0);
        r.Release(PadButton.A);
        r.Tick(320);
        Assert.Equal(2, fired.Count);
    }

    [Fact]
    public void CancelStopsTheRepeatOfAVanishedController()
    {
        var fired = new List<PadButton>();
        var r = new PadRepeater(fired.Add, () => true);
        r.Press(PadButton.Up, 0);
        r.Cancel();
        r.Tick(5000);
        Assert.Single(fired);
    }

    [Fact]
    public void FaceButtonsNeverRepeat()
    {
        var fired = new List<PadButton>();
        var r = new PadRepeater(fired.Add, () => true);
        r.Press(PadButton.A, 0);
        r.Tick(5000);
        Assert.Single(fired);
    }
}

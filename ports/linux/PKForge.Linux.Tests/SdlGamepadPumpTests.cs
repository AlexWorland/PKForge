using Xunit;
using PKForge.Linux.Core;

namespace PKForge.Linux.Tests;

public class SdlGamepadPumpTests
{
    [Fact]
    public void PumpStartsWithoutSdlAndReportsUnavailable()
    {
        var logged = new List<string>();
        var pump = new SdlGamepadPump(a => a(), (_, _) => { }, (_, _, _, _) => { }, "libdoes-not-exist.so", logged.Add);
        Assert.False(pump.Start());
        Assert.NotNull(pump.UnavailableReason);
        Assert.Single(logged);
        pump.Stop();   // harmless after a failed start
    }
}

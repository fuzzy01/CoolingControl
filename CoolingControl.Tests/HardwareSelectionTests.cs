using CoolingControl;
using CoolingControl.Platform;
using Xunit;

namespace CoolingControl.Tests;

public class HardwareSelectionTests
{
    private static readonly HardwareChannel[] Catalog =
    [
        new("LHM", "/temp/0", "CPU Package", "Board", "Temperature", false),
        new("LHM", "/control/0", "CPU Fan", "Board", "Control", true),
        new("LHM", "/fan/0", "CPU Fan RPM", "Board", "Fan", false),
        new("DS18B20", "COM5/t1", "t1", "COM5", "Temperature", false)
    ];

    [Fact]
    public void EnsureSensorChannel_AcceptsTemperatureAndRejectsControlOrUnknown()
    {
        HardwareSelection.EnsureSensorChannel(Catalog, "LHM", "/temp/0");
        Assert.Throws<ArgumentException>(() => HardwareSelection.EnsureSensorChannel(Catalog, "LHM", "/control/0"));
        Assert.Throws<ArgumentException>(() => HardwareSelection.EnsureSensorChannel(Catalog, "LHM", "/missing"));
    }

    [Fact]
    public void EnsureControlChannel_AcceptsControlAndRejectsSensor()
    {
        HardwareSelection.EnsureControlChannel(Catalog, "LHM", "/control/0");
        Assert.Throws<ArgumentException>(() => HardwareSelection.EnsureControlChannel(Catalog, "LHM", "/temp/0"));
    }

    [Fact]
    public void EnsureFanChannel_RequiresSamePlatformFan()
    {
        HardwareSelection.EnsureFanChannel(Catalog, "LHM", "");
        HardwareSelection.EnsureFanChannel(Catalog, "LHM", "/fan/0");
        Assert.Throws<ArgumentException>(() => HardwareSelection.EnsureFanChannel(Catalog, "LHM", "/temp/0"));
        Assert.Throws<ArgumentException>(() => HardwareSelection.EnsureFanChannel(Catalog, "DS18B20", "/fan/0"));
    }
}

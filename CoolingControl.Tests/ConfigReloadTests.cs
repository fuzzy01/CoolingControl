using CoolingControl;
using Xunit;

namespace CoolingControl.Tests;

public class ConfigReloadTests
{
    [Fact]
    public void TryBegin_SecondRequestWhilePending_Is409()
    {
        var reload = new ConfigReload();

        Assert.True(reload.TryBegin(false, out var firstStatus, out _));
        Assert.Equal(202, firstStatus);

        var started = reload.TryBegin(false, out var status, out var message);

        Assert.False(started);
        Assert.Equal(409, status);
        Assert.Equal("Config reload is already running.", message);
        Assert.True(reload.Status().Running);
    }

    [Fact]
    public void TryBegin_WhileCalibrationBusy_Is409()
    {
        var reload = new ConfigReload();

        var started = reload.TryBegin(true, out var status, out var message);

        Assert.False(started);
        Assert.Equal(409, status);
        Assert.Equal("Calibration is already running.", message);
        Assert.False(reload.Status().Running);
    }
}

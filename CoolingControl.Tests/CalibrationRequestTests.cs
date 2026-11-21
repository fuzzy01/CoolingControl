using CoolingControl;
using Xunit;

namespace CoolingControl.Tests;

public class CalibrationRequestTests
{
    [Fact]
    public void TryBegin_UnknownAlias_Is400()
    {
        var request = new CalibrationRequest();

        var started = request.TryBegin([Fan("Fan", "/rpm/0")], "Missing", out var status, out var message);

        Assert.False(started);
        Assert.Equal(400, status);
        Assert.Contains("Unknown control", message);
    }

    [Fact]
    public void TryBegin_MissingRpmSensor_Is400()
    {
        var request = new CalibrationRequest();

        var started = request.TryBegin([Fan("Pump", "")], "Pump", out var status, out var message);

        Assert.False(started);
        Assert.Equal(400, status);
        Assert.Contains("no RPM sensor", message);
    }

    [Fact]
    public void TryBegin_All_WhenOneControlHasNoRpmSensor_Is400()
    {
        var request = new CalibrationRequest();
        var controls = new[] { Fan("Fan", "/rpm/0"), Fan("Pump", "") };

        var started = request.TryBegin(controls, "all", out var status, out var message);

        Assert.False(started);
        Assert.Equal(400, status);
        Assert.Contains("every control", message);
    }

    [Fact]
    public void TryBegin_SecondRequestWhilePending_Is409()
    {
        var request = new CalibrationRequest();
        var controls = new[] { Fan("Fan", "/rpm/0"), Fan("Rear", "/rpm/1") };

        Assert.True(request.TryBegin(controls, "Fan", out var firstStatus, out _));
        Assert.Equal(202, firstStatus);

        var started = request.TryBegin(controls, "all", out var status, out var message);

        Assert.False(started);
        Assert.Equal(409, status);
        Assert.Contains("already running", message);
        Assert.True(request.Status().Running);
    }

    [Fact]
    public void TryBegin_SecondRequestWhileRunning_Is409()
    {
        var request = new CalibrationRequest();
        var controls = new[] { Fan("Fan", "/rpm/0") };
        Assert.True(request.TryBegin(controls, "all", out _, out _));
        Assert.True(request.TryTake(out var alias));

        var started = request.TryBegin(controls, "Fan", out var status, out _);

        Assert.False(started);
        Assert.Equal(409, status);
        Assert.Equal("all", alias);
        request.Finish(true, alias);
        Assert.False(request.Status().Running);
        Assert.Equal("Calibration saved.", request.Status().Message);
    }

    [Fact]
    public void TryBegin_WhileReloadBusy_Is409()
    {
        var request = new CalibrationRequest();

        var started = request.TryBegin([Fan("Fan", "/rpm/0")], "Fan", out var status, out var message, reloadBusy: true);

        Assert.False(started);
        Assert.Equal(409, status);
        Assert.Equal("Config reload is already running.", message);
        Assert.False(request.Status().Running);
    }

    private static ControlConfig Fan(string alias, string rpmSensor) => new()
    {
        Alias = alias,
        Identifier = "/" + alias,
        RPMSensor = rpmSensor
    };
}

using CoolingControl;
using Xunit;

namespace CoolingControl.Tests;

public class StatusSnapshotTests
{
    [Fact]
    public void SetAlerts_ReplacesAlertsWithoutChangingHistory()
    {
        var snapshot = new StatusSnapshot();
        snapshot.Update(
            new Dictionary<string, float?> { ["CPU Package"] = 70f },
            new Dictionary<string, float>(),
            new Dictionary<string, float?>(),
            DateTime.UtcNow,
            []);

        snapshot.SetAlerts([new Alert("errors", "Control loop errors 8 of 10 in the last 60 seconds")]);

        var alert = Assert.Single(snapshot.GetAlerts());
        Assert.Equal("errors", alert.Key);
        Assert.Equal("Control loop errors 8 of 10 in the last 60 seconds", alert.Message);

        var (sensors, _, _, _) = snapshot.GetSnapshot();
        Assert.Equal(70f, sensors["CPU Package"]);

        var (sensorHistory, _) = snapshot.GetHistory();
        Assert.Equal([70f], sensorHistory["CPU Package"]);
    }
}

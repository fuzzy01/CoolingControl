using CoolingControl.Tray;
using Xunit;

namespace CoolingControl.Tests;

public class TrayMenuTests : IDisposable
{
    private readonly string _tempDir;
    private static readonly DateTime HealthNow = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    public TrayMenuTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void FromJson_BuildsSensorControlAndCheckedProfile()
    {
        var model = TrayMenuBuilder.FromJson(
            """
            {
              "activeProfile": "balanced",
              "profiles": ["silent", "balanced", "performance"],
              "sensors": {
                "CPU Package": 46.2,
                "GPU Fan 1": 800,
                "CPU Power": 40,
                "GPU Load": 10
              },
              "controls": { "AIO Fan": 40 },
              "controlRpm": { "AIO Fan": 1234.4 }
            }
            """);

        Assert.Equal(
            ["CPU Package 46.2 °C", "GPU Fan 1 800.0 RPM", "CPU Power 40.0 W", "GPU Load 10.0 %"],
            model.SensorLines);
        Assert.Equal(["AIO Fan 40.0% · 1234 RPM"], model.ControlLines);
        Assert.Equal(["silent", "balanced", "performance"], model.Profiles.Select(p => p.Name));
        Assert.Equal([false, true, false], model.Profiles.Select(p => p.IsChecked));
        Assert.StartsWith("balanced\nCPU Package 46.2 °C", model.Tooltip);
    }

    [Fact]
    public void FromJson_MissingProfiles_HasNoProfileEntries()
    {
        var model = TrayMenuBuilder.FromJson(
            """
            {
              "sensors": { "CPU Package": 1 },
              "controls": { "Pump": 50 }
            }
            """);

        Assert.Empty(model.Profiles);
        Assert.Equal(["Pump 50.0%"], model.ControlLines);
    }

    [Fact]
    public void SameShape_IgnoresValuesAndCheckedProfile()
    {
        var first = TrayMenuBuilder.FromJson(
            """
            {
              "activeProfile": "silent",
              "profiles": ["silent", "balanced"],
              "sensors": { "CPU": 1 },
              "controls": { "Fan": 10 }
            }
            """);
        var second = TrayMenuBuilder.FromJson(
            """
            {
              "activeProfile": "balanced",
              "profiles": ["silent", "balanced"],
              "sensors": { "CPU": 2 },
              "controls": { "Fan": 20 }
            }
            """);

        Assert.True(TrayMenuBuilder.SameShape(first, second));
    }

    [Fact]
    public void SameShape_SensorCountOrProfileNames_RequiresRebuild()
    {
        var first = TrayMenuBuilder.FromJson(
            """
            {
              "profiles": ["silent"],
              "sensors": { "CPU": 1, "GPU": 2 },
              "controls": {}
            }
            """);
        var fewerSensors = TrayMenuBuilder.FromJson(
            """
            {
              "profiles": ["silent"],
              "sensors": { "CPU": 1 },
              "controls": {}
            }
            """);
        var renamedProfile = TrayMenuBuilder.FromJson(
            """
            {
              "profiles": ["quiet"],
              "sensors": { "CPU": 1, "GPU": 2 },
              "controls": {}
            }
            """);

        Assert.False(TrayMenuBuilder.SameShape(first, fewerSensors));
        Assert.False(TrayMenuBuilder.SameShape(first, renamedProfile));
    }

    [Fact]
    public void FromJson_HealthLines_FormatsAgeAndUptime()
    {
        var fresh = TrayMenuBuilder.FromJson(
            HealthJson("1h 2m 3s", HealthNow.AddSeconds(-2), 1000), HealthNow);
        var justNow = TrayMenuBuilder.FromJson(
            HealthJson("1m 0s", HealthNow.AddMilliseconds(-400), 1000), HealthNow);
        var minutes = TrayMenuBuilder.FromJson(
            HealthJson("1h 0m 0s", HealthNow.AddSeconds(-(3 * 60 + 12)), 80_000), HealthNow);

        Assert.Equal(["Uptime 1h 2m 3s", "Updated 2s ago"], fresh.HealthLines);
        Assert.Equal("Updated just now", justNow.HealthLines[1]);
        Assert.Equal("Updated 3m 12s ago", minutes.HealthLines[1]);
    }

    [Fact]
    public void FromJson_HealthLines_MarksStalledPastThreshold()
    {
        var stalled = TrayMenuBuilder.FromJson(
            HealthJson("1h 2m 3s", HealthNow.AddSeconds(-30), 1000), HealthNow);
        var withinLongInterval = TrayMenuBuilder.FromJson(
            HealthJson("1h 2m 3s", HealthNow.AddSeconds(-12), 5000), HealthNow);

        Assert.Equal("Updated 30s ago · stalled", stalled.HealthLines[1]);
        Assert.Equal("Updated 12s ago", withinLongInterval.HealthLines[1]);
    }

    [Fact]
    public void FromJson_HealthLines_MissingFields_UsesPlaceholders()
    {
        var model = TrayMenuBuilder.FromJson("""{ "sensors": { "CPU": 1 } }""", HealthNow);

        Assert.Equal(["Uptime —", "Updated —"], model.HealthLines);
    }

    [Fact]
    public void SameShape_IgnoresHealthText()
    {
        var first = TrayMenuBuilder.FromJson(
            HealthJson("1m 0s", HealthNow.AddSeconds(-2), 1000), HealthNow);
        var second = TrayMenuBuilder.FromJson(
            HealthJson("2h 0m 0s", HealthNow.AddSeconds(-30), 1000), HealthNow);

        Assert.True(TrayMenuBuilder.SameShape(first, second));
    }

    [Fact]
    public void SameShape_HealthLineCount_RequiresRebuild()
    {
        var down = TrayMenuBuilder.ServiceNotRunning();
        var up = TrayMenuBuilder.FromJson("""{ "sensors": { "CPU": 1 } }""", HealthNow);

        Assert.False(TrayMenuBuilder.SameShape(down, up));
    }

    private static string HealthJson(string uptime, DateTime lastUpdate, int intervalMs)
    {
        return $$"""
            {
              "uptime": "{{uptime}}",
              "lastUpdate": "{{lastUpdate.ToString("O")}}",
              "updateInterval": {{intervalMs}},
              "sensors": { "CPU": 1 },
              "controls": { "Fan": 10 }
            }
            """;
    }

    [Fact]
    public void BuildTooltip_StopsAt127Characters()
    {
        var alias = new string('A', 200);
        var model = TrayMenuBuilder.FromJson(
            $$"""
            { "sensors": { "{{alias}}": 1.0 } }
            """);

        Assert.Equal(TrayMenuBuilder.MaxTooltipLength, model.Tooltip.Length);
    }

    [Fact]
    public void Read_MissingFile_UsesDefaultPortAndEnabled()
    {
        var endpoint = StatusEndpointConfig.Read(Path.Combine(_tempDir, "missing.json"));

        Assert.Equal(StatusEndpointConfig.DefaultPort, endpoint.Port);
        Assert.True(endpoint.Enabled);
        Assert.Equal("http://localhost:19999/", endpoint.BaseUrl);
    }

    [Fact]
    public void Read_ExplicitPort_IsHonored()
    {
        var path = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(path, """{ "StatusServerPort": 20000 }""");

        var endpoint = StatusEndpointConfig.Read(path);

        Assert.Equal(20000, endpoint.Port);
        Assert.True(endpoint.Enabled);
    }

    [Fact]
    public void Read_StatusServerDisabled_IsHonored()
    {
        var path = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(path, """{ "StatusServerEnabled": false }""");

        var endpoint = StatusEndpointConfig.Read(path);

        Assert.False(endpoint.Enabled);
        Assert.Equal(StatusEndpointConfig.DefaultPort, endpoint.Port);
    }
}

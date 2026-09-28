using System.Text.Json;
using CoolingControl;
using Xunit;

namespace CoolingControl.Tests;

public class ConfigHelperTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _scriptPath;

    public ConfigHelperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _scriptPath = Path.Combine(_tempDir, "script.lua");
        File.WriteAllText(_scriptPath, "");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Theory]
    [InlineData(250f, 20f)]
    [InlineData(500f, 20f)]
    [InlineData(1500f, 60f)]
    [InlineData(2500f, 100f)]
    [InlineData(3000f, 100f)]
    public void ConvertRPMToPercent_ClampsAndInterpolates(float rpm, float expectedPercent)
    {
        var config = CreateConfig(
        [
            new() { Control = 20f, Rpm = 500f },
            new() { Control = 100f, Rpm = 2500f }
        ]);

        var result = config.ConvertRPMToPercent("Fan", rpm);

        Assert.Equal(expectedPercent, result!.Value);
    }

    [Fact]
    public void ConvertRPMToPercent_DuplicateRpm_UsesFirstCalibrationPoint()
    {
        var config = CreateConfig(
        [
            new() { Control = 20f, Rpm = 500f },
            new() { Control = 30f, Rpm = 500f },
            new() { Control = 100f, Rpm = 2500f }
        ]);

        var result = config.ConvertRPMToPercent("Fan", 500f);

        Assert.Equal(20f, result!.Value);
    }

    [Fact]
    public void ConvertRPMToPercent_MissingOrInsufficientCalibration_ReturnsNull()
    {
        var config = CreateConfig([]);

        Assert.Null(config.ConvertRPMToPercent("Fan", 1000f));
        Assert.Null(config.ConvertRPMToPercent("Unknown", 1000f));
    }

    [Theory]
    [InlineData(20f, 500f)]
    [InlineData(60f, 1500f)]
    [InlineData(100f, 2500f)]
    [InlineData(0f, 500f)]
    [InlineData(110f, 2500f)]
    public void ConvertPercentToRpm_ClampsAndInterpolates(float percent, float expectedRpm)
    {
        var config = CreateConfig(
        [
            new() { Control = 20f, Rpm = 500f },
            new() { Control = 100f, Rpm = 2500f }
        ]);

        var result = config.ConvertPercentToRpm("Fan", percent);

        Assert.Equal(expectedRpm, result!.Value);
    }

    [Fact]
    public void ConvertPercentToRpm_ZeroControlDelta_SkipsSegment()
    {
        var config = CreateConfig(
        [
            new() { Control = 20f, Rpm = 500f },
            new() { Control = 20f, Rpm = 700f },
            new() { Control = 100f, Rpm = 2500f }
        ]);

        var result = config.ConvertPercentToRpm("Fan", 60f);

        Assert.Equal(1600f, result!.Value);
    }

    [Fact]
    public void ConvertPercentToRpm_MissingOrInsufficientCalibration_ReturnsNull()
    {
        var config = CreateConfig([]);

        Assert.Null(config.ConvertPercentToRpm("Fan", 50f));
        Assert.Null(config.ConvertPercentToRpm("Unknown", 50f));
    }

    [Fact]
    public void SetActiveProfile_PersistsListedNameAndRejectsUnknown()
    {
        var configPath = Path.Combine(_tempDir, "profiles.json");
        var config = new Config
        {
            ScriptPath = _scriptPath,
            Profiles = ["silent", "balanced"],
            ActiveProfile = "balanced",
            Controls = [new() { Alias = "Fan", Identifier = "/fan/0" }]
        };
        File.WriteAllText(configPath, JsonSerializer.Serialize(config));
        var helper = new ConfigHelper(configPath);

        Assert.False(helper.SetActiveProfile("balanced"));
        Assert.True(helper.SetActiveProfile("silent"));
        Assert.Equal("silent", helper.GetActiveProfile());

        var saved = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath));
        Assert.Equal("silent", saved!.ActiveProfile);
        Assert.Throws<ArgumentException>(() => helper.SetActiveProfile("performance"));
        Assert.Equal("silent", helper.GetActiveProfile());
    }

    [Fact]
    public void AddSensor_PersistsAndLeavesRuntimeIdentifiersUnchanged()
    {
        var helper = CreateConfig([]);
        var before = helper.SensorIdentifiers.Count;

        helper.AddSensor(new SensorConfig { Platform = "LHM", Identifier = "/cpu/0", Alias = "CPU Package", AlertMax = 90f });

        Assert.Equal(before, helper.SensorIdentifiers.Count);
        Assert.DoesNotContain("/cpu/0", helper.SensorIdentifiers);
        var reloaded = new ConfigHelper(helper.ConfigFilePath);
        Assert.Contains(reloaded.Config.Sensors, sensor => sensor.Alias == "CPU Package" && sensor.AlertMax == 90f);
    }

    [Fact]
    public void AddSensor_DuplicateAliasOrIdentifier_Throws()
    {
        var helper = CreateConfig([]);
        helper.AddSensor(new SensorConfig { Platform = "LHM", Identifier = "/cpu/0", Alias = "CPU Package" });

        Assert.Throws<ArgumentException>(() =>
            helper.AddSensor(new SensorConfig { Platform = "LHM", Identifier = "/cpu/1", Alias = "CPU Package" }));
        Assert.Throws<ArgumentException>(() =>
            helper.AddSensor(new SensorConfig { Platform = "LHM", Identifier = "/cpu/0", Alias = "Other" }));
        Assert.Single(helper.Config.Sensors, sensor => sensor.Identifier == "/cpu/0");
    }

    [Fact]
    public void UpdateSensor_ChangesAlertMaxAndAlias()
    {
        var helper = CreateConfig([]);
        helper.AddSensor(new SensorConfig { Platform = "LHM", Identifier = "/cpu/0", Alias = "CPU Package" });
        helper.AddSensor(new SensorConfig { Platform = "LHM", Identifier = "/gpu/0", Alias = "GPU Core" });

        helper.UpdateSensor("CPU Package", new SensorConfig
        {
            Platform = "changed",
            Identifier = "changed",
            Alias = "CPU",
            AlertMax = 60f
        });

        var sensor = Assert.Single(helper.Config.Sensors, item => item.Identifier == "/cpu/0");
        Assert.Equal("CPU", sensor.Alias);
        Assert.Equal(60f, sensor.AlertMax);
        Assert.Equal("LHM", sensor.Platform);
        Assert.Contains(helper.Config.Sensors, item => item.Alias == "GPU Core");
    }

    [Fact]
    public void DeleteSensor_RemovesOnlyThatSensor()
    {
        var helper = CreateConfig([]);
        helper.AddSensor(new SensorConfig { Platform = "LHM", Identifier = "/cpu/0", Alias = "CPU Package" });
        helper.AddSensor(new SensorConfig { Platform = "LHM", Identifier = "/gpu/0", Alias = "GPU Core" });

        helper.DeleteSensor("CPU Package");

        Assert.Single(helper.Config.Sensors);
        Assert.Equal("GPU Core", helper.Config.Sensors[0].Alias);
    }

    [Fact]
    public void AddControl_StoresParametersAndRejectsReusedRpmSensor()
    {
        var helper = CreateConfig([]);

        helper.AddControl(new ControlConfig
        {
            Platform = "LHM",
            Identifier = "/fan/1",
            Alias = "Rear",
            StepUp = 4f,
            RPMSensor = "/rpm/1"
        });
        Assert.Throws<ArgumentException>(() => helper.AddControl(new ControlConfig
        {
            Platform = "LHM",
            Identifier = "/fan/2",
            Alias = "Front",
            RPMSensor = "/rpm/1"
        }));

        var saved = Assert.Single(helper.Config.Controls, control => control.Alias == "Rear");
        Assert.Empty(saved.RPMCalibration);
        Assert.Equal(4f, saved.StepUp);
    }

    [Fact]
    public void UpdateControl_ChangesStepUpAndKeepsCalibration()
    {
        var helper = CreateConfig(
        [
            new() { Control = 20f, Rpm = 500f },
            new() { Control = 100f, Rpm = 2500f }
        ]);

        helper.Config.Controls[0].ThermalMinControl = 40f;
        helper.UpdateControl("Fan", new ControlConfig
        {
            Platform = "LHM",
            Identifier = "/fan/0",
            Alias = "Case Fan",
            StepUp = 12f,
            RPMCalibration = []
        });

        var control = Assert.Single(helper.Config.Controls);
        Assert.Equal("Case Fan", control.Alias);
        Assert.Equal(12f, control.StepUp);
        Assert.Equal(2, control.RPMCalibration.Count);
        Assert.Equal(500f, control.RPMCalibration[0].Rpm);
        Assert.Equal(40f, control.ThermalMinControl);
    }

    [Fact]
    public void DeleteControl_RemovesOnlyThatControl()
    {
        var helper = CreateConfig([]);
        helper.AddControl(new ControlConfig { Platform = "LHM", Identifier = "/fan/1", Alias = "Rear" });

        helper.DeleteControl("Rear");

        Assert.Single(helper.Config.Controls);
        Assert.Equal("Fan", helper.Config.Controls[0].Alias);
    }

    [Fact]
    public void ReorderSensors_PersistsNewOrderAndLeavesIdentifiersUnchanged()
    {
        var helper = CreateConfig([]);
        helper.AddSensor(new SensorConfig { Platform = "LHM", Identifier = "/a", Alias = "A" });
        helper.AddSensor(new SensorConfig { Platform = "LHM", Identifier = "/b", Alias = "B" });
        var before = helper.SensorIdentifiers.ToHashSet();

        helper.ReorderSensors(["B", "A"]);

        Assert.Equal(before, helper.SensorIdentifiers.ToHashSet());
        var reloaded = new ConfigHelper(helper.ConfigFilePath);
        Assert.Equal(["B", "A"], reloaded.Config.Sensors.Select(sensor => sensor.Alias));
    }

    [Fact]
    public void ReorderControls_PersistsNewOrderAndKeepsCalibration()
    {
        var helper = CreateConfig(
        [
            new() { Control = 20f, Rpm = 500f },
            new() { Control = 100f, Rpm = 2500f }
        ]);
        helper.AddControl(new ControlConfig { Platform = "LHM", Identifier = "/fan/1", Alias = "Rear" });

        helper.ReorderControls(["Rear", "Fan"]);

        var reloaded = new ConfigHelper(helper.ConfigFilePath);
        Assert.Equal(["Rear", "Fan"], reloaded.Config.Controls.Select(control => control.Alias));
        var fan = reloaded.Config.Controls.Single(control => control.Alias == "Fan");
        Assert.Equal(2, fan.RPMCalibration.Count);
    }

    [Fact]
    public void ReorderSensors_InvalidList_LeavesOriginalOrder()
    {
        var helper = CreateConfig([]);
        helper.AddSensor(new SensorConfig { Platform = "LHM", Identifier = "/a", Alias = "A" });
        helper.AddSensor(new SensorConfig { Platform = "LHM", Identifier = "/b", Alias = "B" });

        Assert.Throws<ArgumentException>(() => helper.ReorderSensors(["A"]));
        Assert.Throws<ArgumentException>(() => helper.ReorderSensors(["A", "A"]));
        Assert.Throws<ArgumentException>(() => helper.ReorderSensors(["A", "C"]));
        Assert.Equal(["A", "B"], helper.Config.Sensors.Select(sensor => sensor.Alias));
    }

    private ConfigHelper CreateConfig(List<RPMCalibrationData> rpmCalibration)
    {
        var configPath = Path.Combine(_tempDir, $"{Path.GetRandomFileName()}.json");
        var config = new Config
        {
            ScriptPath = _scriptPath,
            Controls =
            [
                new()
                {
                    Alias = "Fan",
                    Identifier = "/fan/0",
                    RPMCalibration = rpmCalibration
                }
            ]
        };

        File.WriteAllText(configPath, JsonSerializer.Serialize(config));
        return new ConfigHelper(configPath);
    }
}

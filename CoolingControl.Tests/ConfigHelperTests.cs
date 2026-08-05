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

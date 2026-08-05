using System.Text.Json;
using CoolingControl;
using Xunit;

namespace CoolingControl.Tests;

public class ControlScriptTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _scriptPath;
    private readonly string _configPath;

    public ControlScriptTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _scriptPath = Path.Combine(_tempDir, "script.lua");
        _configPath = Path.Combine(_tempDir, "config.json");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void CalculateControls_UsesSensorsAndConfiguration_ReturnsPercentage()
    {
        using var script = CreateScript(
            """
            function calculate_controls(sensors)
                return {
                    {
                        alias = "Fan",
                        value = sensors["CPU Temp"] + control_config["Fan"].step_up
                            + (sensor_config["CPU Temp"].identifier == "/cpu/0/temp/0" and 1)
                    }
                }
            end
            """);

        var result = script.CalculateControls(new() { ["CPU Temp"] = 40f });

        Assert.Equal(49f, result["Fan"]);
    }

    [Fact]
    public void CalculateControls_RpmOutput_ConvertsUsingCalibration()
    {
        using var script = CreateScript(
            """
            function calculate_controls(sensors)
                return { { alias = "Fan", rpm = 1500 } }
            end
            """);

        var result = script.CalculateControls([]);

        Assert.Equal(60f, result["Fan"]);
    }

    [Fact]
    public void LifecycleCallbacks_UpdateLuaState()
    {
        using var script = CreateScript(
            """
            local state = 0

            function initialize()
                state = state + 1
            end

            function on_start()
                state = state + 1
            end

            function on_suspend()
                state = state + 1
            end

            function on_resume()
                state = state + 1
            end

            function on_stop()
                state = state + 1
            end

            function calculate_controls(sensors)
                return { { alias = "Fan", value = state } }
            end
            """);

        Assert.Equal(1f, script.CalculateControls([])["Fan"]);

        script.OnStart();
        script.OnSuspend();
        script.OnResume();
        script.OnStop();

        Assert.Equal(5f, script.CalculateControls([])["Fan"]);
    }

    [Fact]
    public void CalculateControls_InvalidEntries_AreIgnoredAndLastDuplicateWins()
    {
        using var script = CreateScript(
            """
            function calculate_controls(sensors)
                return {
                    { alias = "Fan", value = 25 },
                    { alias = "Unknown Fan", value = 50 },
                    { value = 50 },
                    { alias = "Pump" },
                    { alias = "Pump", value = "invalid" },
                    { alias = "Fan", value = 30 }
                }
            end
            """,
            controls:
            [
                new() { Alias = "Fan", Identifier = "/fan/0" },
                new() { Alias = "Pump", Identifier = "/fan/1" }
            ]);

        var result = script.CalculateControls([]);

        Assert.Single(result);
        Assert.Equal(30f, result["Fan"]);
    }

    [Fact]
    public void Constructor_InvalidLua_ThrowsWithScriptPath()
    {
        File.WriteAllText(_scriptPath, "function calculate_controls(");
        var config = CreateConfig();

        var exception = Assert.Throws<InvalidOperationException>(() => new ControlScript(config));

        Assert.Contains(_scriptPath, exception.Message);
        Assert.Contains("Failed to load Lua script", exception.Message);
    }

    [Fact]
    public void Constructor_MissingCalculateControls_Throws()
    {
        File.WriteAllText(_scriptPath, "function on_start() end");
        var config = CreateConfig();

        var exception = Assert.Throws<InvalidOperationException>(() => new ControlScript(config));

        Assert.Contains("must define a 'calculate_controls' function", exception.Message);
    }

    [Fact]
    public void CalculateControls_NonTableResult_Throws()
    {
        using var script = CreateScript(
            """
            function calculate_controls(sensors)
                return 1
            end
            """);

        var exception = Assert.Throws<InvalidOperationException>(() => script.CalculateControls([]));

        Assert.Contains("did not return a valid table", exception.Message);
    }

    private ControlScript CreateScript(string source, List<ControlConfig>? controls = null)
    {
        File.WriteAllText(_scriptPath, source);
        return new ControlScript(CreateConfig(controls));
    }

    private ConfigHelper CreateConfig(List<ControlConfig>? controls = null)
    {
        var config = new Config
        {
            ScriptPath = _scriptPath,
            Controls = controls ??
            [
                new()
                {
                    Alias = "Fan",
                    Identifier = "/fan/0",
                    StepUp = 8f,
                    RPMCalibration =
                    [
                        new() { Control = 20f, Rpm = 500f },
                        new() { Control = 100f, Rpm = 2500f }
                    ]
                }
            ],
            Sensors =
            [
                new() { Alias = "CPU Temp", Identifier = "/cpu/0/temp/0" }
            ]
        };

        File.WriteAllText(_configPath, JsonSerializer.Serialize(config));
        return new ConfigHelper(_configPath);
    }
}

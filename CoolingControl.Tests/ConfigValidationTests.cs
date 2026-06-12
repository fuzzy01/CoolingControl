using CoolingControl;
using Xunit;

namespace CoolingControl.Tests;

public class ConfigValidationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _scriptPath;

    public ConfigValidationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _scriptPath = Path.Combine(_tempDir, "script.lua");
        File.WriteAllText(_scriptPath, "");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private Config ValidConfig() => new()
    {
        ScriptPath = _scriptPath,
        Controls = [new() { Alias = "Fan", Identifier = "/fan/0" }],
        Sensors = []
    };

    [Fact]
    public void Validate_ValidConfig_Succeeds()
    {
        var ex = Record.Exception(() => ConfigHelper.Validate(ValidConfig()));
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_EmptyScriptPath_Throws()
    {
        var config = ValidConfig();
        config.ScriptPath = "";

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigHelper.Validate(config));
        Assert.Contains("ScriptPath must not be empty", ex.Message);
    }

    [Fact]
    public void Validate_MissingScriptFile_Throws()
    {
        var config = ValidConfig();
        config.ScriptPath = Path.Combine(_tempDir, "nonexistent.lua");

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigHelper.Validate(config));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Validate_InvalidUpdateInterval_Throws()
    {
        var config = ValidConfig();
        config.UpdateIntervalMs = 0;

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigHelper.Validate(config));
        Assert.Contains("UpdateIntervalMs", ex.Message);
    }

    [Fact]
    public void Validate_InvalidLogLevel_Throws()
    {
        var config = ValidConfig();
        config.LogLevel = "INVALID";

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigHelper.Validate(config));
        Assert.Contains("LogLevel", ex.Message);
    }

    [Fact]
    public void Validate_NoControls_Throws()
    {
        var config = ValidConfig();
        config.Controls.Clear();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigHelper.Validate(config));
        Assert.Contains("At least one control", ex.Message);
    }

    [Fact]
    public void Validate_DuplicateControlAlias_Throws()
    {
        var config = ValidConfig();
        config.Controls.Add(new() { Alias = "Fan", Identifier = "/fan/1" });

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigHelper.Validate(config));
        Assert.Contains("Duplicate alias", ex.Message);
    }

    [Fact]
    public void Validate_DuplicateControlIdentifier_Throws()
    {
        var config = ValidConfig();
        config.Controls.Add(new() { Alias = "Fan2", Identifier = "/fan/0" });

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigHelper.Validate(config));
        Assert.Contains("Duplicate identifier", ex.Message);
    }

    [Fact]
    public void Validate_DuplicateSensorAlias_Throws()
    {
        var config = ValidConfig();
        config.Sensors.Add(new() { Alias = "CPU Temp", Identifier = "/cpu/0/temp/0" });
        config.Sensors.Add(new() { Alias = "CPU Temp", Identifier = "/cpu/0/temp/1" });

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigHelper.Validate(config));
        Assert.Contains("Duplicate alias", ex.Message);
    }

    [Fact]
    public void Validate_MultipleErrors_AllReported()
    {
        var config = new Config
        {
            ScriptPath = "",
            LogLevel = "BAD"
        };
        config.Controls.Clear();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigHelper.Validate(config));
        Assert.Contains("ScriptPath must not be empty", ex.Message);
        Assert.Contains("At least one control", ex.Message);
        Assert.Contains("LogLevel", ex.Message);
    }
}

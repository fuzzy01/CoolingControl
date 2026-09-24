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

    [Theory]
    [InlineData("information")]
    [InlineData("DEBUG")]
    [InlineData("warning")]
    public void Validate_LogLevel_WrongCase_Throws(string logLevel)
    {
        var config = ValidConfig();
        config.LogLevel = logLevel;

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigHelper.Validate(config));
        Assert.Contains("LogLevel", ex.Message);
    }

    [Fact]
    public void Validate_SensorAlertMax_Succeeds()
    {
        var config = ValidConfig();
        config.Sensors.Add(new() { Alias = "CPU Package", Identifier = "/cpu/0", AlertMax = 90f });

        var ex = Record.Exception(() => ConfigHelper.Validate(config));
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_EmptyProfiles_Succeeds()
    {
        var config = ValidConfig();
        config.Profiles = [];
        config.ActiveProfile = "";

        var ex = Record.Exception(() => ConfigHelper.Validate(config));
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_KnownActiveProfile_Succeeds()
    {
        var config = ValidConfig();
        config.Profiles = ["silent", "balanced", "performance"];
        config.ActiveProfile = "balanced";

        var ex = Record.Exception(() => ConfigHelper.Validate(config));
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_DuplicateProfile_Throws()
    {
        var config = ValidConfig();
        config.Profiles = ["silent", "silent"];

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigHelper.Validate(config));
        Assert.Contains("Duplicate profile", ex.Message);
    }

    [Fact]
    public void Validate_UnknownActiveProfile_Throws()
    {
        var config = ValidConfig();
        config.Profiles = ["silent"];
        config.ActiveProfile = "performance";

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigHelper.Validate(config));
        Assert.Contains("ActiveProfile", ex.Message);
        Assert.Contains("not listed in Profiles", ex.Message);
    }

    [Fact]
    public void Validate_NoControls_Succeeds()
    {
        var config = ValidConfig();
        config.Controls.Clear();

        var ex = Record.Exception(() => ConfigHelper.Validate(config));
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_ControlAliasesDifferingOnlyByCase_Succeeds()
    {
        var config = ValidConfig();
        config.Controls.Add(new() { Alias = "fan", Identifier = "/fan/1" });

        var ex = Record.Exception(() => ConfigHelper.Validate(config));
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_SensorAliasesDifferingOnlyByCase_Succeeds()
    {
        var config = ValidConfig();
        config.Sensors.Add(new() { Alias = "CPU Temp", Identifier = "/cpu/0/temp/0" });
        config.Sensors.Add(new() { Alias = "cpu temp", Identifier = "/cpu/0/temp/1" });

        var ex = Record.Exception(() => ConfigHelper.Validate(config));
        Assert.Null(ex);
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
    public void Validate_DuplicateRPMSensor_Throws()
    {
        var config = ValidConfig();
        config.Controls[0].RPMSensor = "/fan/rpm/0";
        config.Controls.Add(new() { Alias = "Fan2", Identifier = "/fan/1", RPMSensor = "/fan/rpm/0" });

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigHelper.Validate(config));
        Assert.Contains("Duplicate RPMSensor", ex.Message);
    }

    [Fact]
    public void Validate_EmptyRPMSensorOnMultipleControls_Succeeds()
    {
        var config = ValidConfig();
        config.Controls.Add(new() { Alias = "Fan2", Identifier = "/fan/1" });

        var ex = Record.Exception(() => ConfigHelper.Validate(config));
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_RPMSensorsDifferingOnlyByCase_Succeeds()
    {
        var config = ValidConfig();
        config.Controls[0].RPMSensor = "/fan/rpm/0";
        config.Controls.Add(new() { Alias = "Fan2", Identifier = "/fan/1", RPMSensor = "/Fan/rpm/0" });

        var ex = Record.Exception(() => ConfigHelper.Validate(config));
        Assert.Null(ex);
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
        Assert.Contains("LogLevel", ex.Message);
    }

    [Fact]
    public void Validate_EmptyBindAddress_Throws()
    {
        var config = ValidConfig();
        config.StatusServerBindAddress = "";

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigHelper.Validate(config));
        Assert.Contains("StatusServerBindAddress must not be empty", ex.Message);
    }

    [Fact]
    public void Validate_BeatDetuneMinSeparationRpm_NonPositive_Throws()
    {
        var config = ValidConfig();
        config.BeatDetuneMinSeparationRpm = 0;

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigHelper.Validate(config));
        Assert.Contains("BeatDetuneMinSeparationRpm must be positive", ex.Message);
    }

    [Fact]
    public void Validate_BeatDetuneMaxNudgeRpm_Negative_Throws()
    {
        var config = ValidConfig();
        config.BeatDetuneMaxNudgeRpm = -1;

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigHelper.Validate(config));
        Assert.Contains("BeatDetuneMaxNudgeRpm must be zero or positive", ex.Message);
    }
}

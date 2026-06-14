using System.Text.Json;
using Serilog;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CoolingControl.Tests")]

namespace CoolingControl;

public class ConfigHelper
{
    private readonly Config _config;
    private readonly string _configFilePath;
    private readonly Dictionary<string, SensorConfig> _sensorConfigsByAlias;
    private readonly Dictionary<string, SensorConfig> _sensorConfigsByIdentifier;
    private readonly HashSet<string> _sensorIdentifiers;
    private readonly Dictionary<string, ControlConfig> _controlConfigsByAlias;
    private readonly Dictionary<string, ControlConfig> _controlConfigsByIdentifier;
    private readonly HashSet<string> _controlIdentifiers;

    public ConfigHelper(string configFilePath)
    {
        _configFilePath = configFilePath;
        _config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configFilePath))
                    ?? throw new InvalidOperationException("Deserialized configuration is null.");
        Validate(_config);
        Log.Information("Configuration loaded successfully from {ConfigFilePath}", configFilePath);

        _sensorConfigsByAlias = _config.Sensors.ToDictionary(f => f.Alias, f => f);
        _sensorConfigsByIdentifier = _config.Sensors.ToDictionary(f => f.Identifier, f => f);
        _sensorIdentifiers = _config.Sensors.Select(f => f.Identifier).ToHashSet();
        _controlConfigsByAlias = _config.Controls.ToDictionary(f => f.Alias, f => f);
        _controlConfigsByIdentifier = _config.Controls.ToDictionary(f => f.Identifier, f => f);
        _controlIdentifiers = _config.Controls.Select(f => f.Identifier).ToHashSet();
    }

    public void SaveConfig()
    {
        var jsonString = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configFilePath, jsonString);
        Log.Information("Configuration saved successfully to {ConfigFilePath}", _configFilePath);
    }

    internal static void Validate(Config config)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.ScriptPath))
            errors.Add("ScriptPath must not be empty.");
        else if (!File.Exists(config.ScriptPath))
            errors.Add($"ScriptPath '{config.ScriptPath}' does not exist.");

        if (config.UpdateIntervalMs <= 0)
            errors.Add($"UpdateIntervalMs must be positive (got {config.UpdateIntervalMs}).");

        if (config.MaxControlLoopErrors <= 0)
            errors.Add($"MaxControlLoopErrors must be positive (got {config.MaxControlLoopErrors}).");

        string[] validLogLevels = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];
        if (!validLogLevels.Contains(config.LogLevel, StringComparer.OrdinalIgnoreCase))
            errors.Add($"LogLevel '{config.LogLevel}' is not valid. Must be one of: {string.Join(", ", validLogLevels)}.");

        if (config.Controls.Count == 0)
            errors.Add("At least one control must be defined in Controls.");

        var controlAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var controlIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < config.Controls.Count; i++)
        {
            var c = config.Controls[i];
            if (string.IsNullOrWhiteSpace(c.Alias))
                errors.Add($"Controls[{i}]: Alias must not be empty.");
            else if (!controlAliases.Add(c.Alias))
                errors.Add($"Controls[{i}]: Duplicate alias '{c.Alias}'.");

            if (string.IsNullOrWhiteSpace(c.Identifier))
                errors.Add($"Controls[{i}] ('{c.Alias}'): Identifier must not be empty.");
            else if (!controlIdentifiers.Add(c.Identifier))
                errors.Add($"Controls[{i}] ('{c.Alias}'): Duplicate identifier '{c.Identifier}'.");
        }

        var sensorAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sensorIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < config.Sensors.Count; i++)
        {
            var s = config.Sensors[i];
            if (string.IsNullOrWhiteSpace(s.Alias))
                errors.Add($"Sensors[{i}]: Alias must not be empty.");
            else if (!sensorAliases.Add(s.Alias))
                errors.Add($"Sensors[{i}]: Duplicate alias '{s.Alias}'.");

            if (string.IsNullOrWhiteSpace(s.Identifier))
                errors.Add($"Sensors[{i}] ('{s.Alias}'): Identifier must not be empty.");
            else if (!sensorIdentifiers.Add(s.Identifier))
                errors.Add($"Sensors[{i}] ('{s.Alias}'): Duplicate identifier '{s.Identifier}'.");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "config.json validation failed:\n" + string.Join("\n", errors.Select(e => "  - " + e)));
    }
    
    public Dictionary<string, SensorConfig> SensorConfigsByAlias => _sensorConfigsByAlias;

    public Dictionary<string, SensorConfig> SensorConfigsByIdentifier => _sensorConfigsByIdentifier;

    public HashSet<string> SensorIdentifiers => _sensorIdentifiers;

    public Dictionary<string, ControlConfig> ControlConfigsByIdentifier => _controlConfigsByIdentifier;

    public Dictionary<string, ControlConfig> ControlConfigsByAlias => _controlConfigsByAlias;

    public HashSet<string> ControlIdentifiers => _controlIdentifiers;

    public Config Config => _config;

    public float? ConvertRPMToPercent(string alias, float rpm)
    {
        if (!_controlConfigsByAlias.TryGetValue(alias, out var controlConfig))
        {
            Log.Error("Control {Alias} not configured", alias);
            return null;
        }

        if (controlConfig.RPMCalibration.Count <= 1)
        {
            Log.Error("Control {Alias} has no valid RPM calibration data", alias);
            return null;
        }

        var rpmCalibration = controlConfig.RPMCalibration;
        float value = 0f;

        if (rpm < rpmCalibration[0].Rpm)
        {
            value = rpmCalibration[0].Control;
            Log.Debug("{Alias} {Rpm}RPM => {Value}", alias, rpm, value);
        }
        else if (rpm > rpmCalibration[^1].Rpm)
        {
            value = rpmCalibration[^1].Control;
            Log.Debug("{Alias} {Rpm}RPM => {Value}", alias, rpm, value);
        }
        else
        {
            int i;
            for (i = 0; i < rpmCalibration.Count - 1; i++)
            {
                var lower = rpmCalibration[i];
                var upper = rpmCalibration[i + 1];

                if (rpm >= lower.Rpm && rpm <= upper.Rpm)
                {
                    var rpmDelta = upper.Rpm - lower.Rpm;

                    if (rpmDelta == 0)
                    {
                        value = lower.Control;
                        Log.Debug("{Alias} {Rpm}RPM => {Value}", alias, rpm, value);
                        break;
                    }

                    value = lower.Control + (upper.Control - lower.Control) * ((rpm - lower.Rpm) / rpmDelta);
                    Log.Debug("{Alias} {Rpm}RPM => {Value}", alias, rpm, value);
                    break;
                }
            }

            if (i == rpmCalibration.Count - 1)
            {
                Log.Error("Calibration data is not sorted for {Alias}", alias);
                return null;
            }
        }

        return value;
    }

}

















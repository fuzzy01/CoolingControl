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
    private readonly Dictionary<string, ControlConfig> _controlConfigsByRPMSensorIdentifier;
    private readonly HashSet<string> _controlRPMSensorIdentifiers;
    private readonly object _configLock = new();

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
        _controlConfigsByRPMSensorIdentifier = _config.Controls
            .Where(c => !string.IsNullOrEmpty(c.RPMSensor))
            .ToDictionary(c => c.RPMSensor, c => c);
        _controlRPMSensorIdentifiers = _controlConfigsByRPMSensorIdentifier.Keys.ToHashSet();
    }

    public void SaveConfig()
    {
        lock (_configLock)
            WriteConfigUnlocked();
    }

    public string GetActiveProfile()
    {
        lock (_configLock)
            return _config.ActiveProfile ?? "";
    }

    public bool SetActiveProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Profile name must not be empty.", nameof(name));

        lock (_configLock)
        {
            if (_config.Profiles == null || !_config.Profiles.Contains(name))
                throw new ArgumentException($"Profile '{name}' is not listed in Profiles.", nameof(name));

            if (_config.ActiveProfile == name)
                return false;

            _config.ActiveProfile = name;
            WriteConfigUnlocked();
            return true;
        }
    }

    public void AddSensor(SensorConfig sensor)
    {
        lock (_configLock)
        {
            EnsureSensor(sensor, null);
            _config.Sensors.Add(sensor);
            WriteConfigUnlocked();
        }
    }

    public void UpdateSensor(string alias, SensorConfig sensor)
    {
        lock (_configLock)
        {
            var index = _config.Sensors.FindIndex(item => item.Alias == alias);
            if (index < 0)
                throw new ArgumentException($"Sensor '{alias}' was not found.", nameof(alias));

            var existing = _config.Sensors[index];
            sensor.Platform = existing.Platform;
            sensor.Identifier = existing.Identifier;
            EnsureSensor(sensor, alias);
            _config.Sensors[index] = sensor;
            WriteConfigUnlocked();
        }
    }

    public void DeleteSensor(string alias)
    {
        lock (_configLock)
        {
            if (_config.Sensors.RemoveAll(item => item.Alias == alias) == 0)
                throw new ArgumentException($"Sensor '{alias}' was not found.", nameof(alias));
            WriteConfigUnlocked();
        }
    }

    public void AddControl(ControlConfig control)
    {
        lock (_configLock)
        {
            control.RPMCalibration ??= [];
            EnsureControl(control, null);
            _config.Controls.Add(control);
            WriteConfigUnlocked();
        }
    }

    public void UpdateControl(string alias, ControlConfig control)
    {
        lock (_configLock)
        {
            var index = _config.Controls.FindIndex(item => item.Alias == alias);
            if (index < 0)
                throw new ArgumentException($"Control '{alias}' was not found.", nameof(alias));

            var existing = _config.Controls[index];
            control.Platform = existing.Platform;
            control.Identifier = existing.Identifier;
            control.RPMCalibration = existing.RPMCalibration;
            control.ThermalMinControl = existing.ThermalMinControl;
            EnsureControl(control, alias);
            _config.Controls[index] = control;
            WriteConfigUnlocked();
        }
    }

    public void DeleteControl(string alias)
    {
        lock (_configLock)
        {
            if (_config.Controls.RemoveAll(item => item.Alias == alias) == 0)
                throw new ArgumentException($"Control '{alias}' was not found.", nameof(alias));
            WriteConfigUnlocked();
        }
    }

    public void ReorderSensors(IReadOnlyList<string> aliases)
    {
        lock (_configLock)
        {
            _config.Sensors = Reorder(_config.Sensors, aliases, sensor => sensor.Alias);
            WriteConfigUnlocked();
        }
    }

    public void ReorderControls(IReadOnlyList<string> aliases)
    {
        lock (_configLock)
        {
            _config.Controls = Reorder(_config.Controls, aliases, control => control.Alias);
            WriteConfigUnlocked();
        }
    }

    private static List<T> Reorder<T>(List<T> items, IReadOnlyList<string> aliases, Func<T, string> aliasOf)
    {
        if (aliases.Count != items.Count)
            throw new ArgumentException("Alias list does not match the current entries.");

        var byAlias = items.ToDictionary(aliasOf);
        var seen = new HashSet<string>();
        var ordered = new List<T>(aliases.Count);
        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias) || !seen.Add(alias) || !byAlias.TryGetValue(alias, out var item))
                throw new ArgumentException("Alias list does not match the current entries.");
            ordered.Add(item);
        }

        return ordered;
    }

    private void EnsureSensor(SensorConfig sensor, string? ignoreAlias)
    {
        if (string.IsNullOrWhiteSpace(sensor.Alias))
            throw new ArgumentException("Alias must not be empty.");
        if (string.IsNullOrWhiteSpace(sensor.Identifier))
            throw new ArgumentException("Identifier must not be empty.");
        if (string.IsNullOrWhiteSpace(sensor.Platform))
            throw new ArgumentException("Platform must not be empty.");

        foreach (var existing in _config.Sensors)
        {
            if (ignoreAlias != null && existing.Alias == ignoreAlias)
                continue;
            if (existing.Alias == sensor.Alias)
                throw new ArgumentException($"Duplicate alias '{sensor.Alias}'.");
            if (existing.Identifier == sensor.Identifier)
                throw new ArgumentException($"Duplicate identifier '{sensor.Identifier}'.");
        }
    }

    private void EnsureControl(ControlConfig control, string? ignoreAlias)
    {
        if (string.IsNullOrWhiteSpace(control.Alias))
            throw new ArgumentException("Alias must not be empty.");
        if (string.IsNullOrWhiteSpace(control.Identifier))
            throw new ArgumentException("Identifier must not be empty.");
        if (string.IsNullOrWhiteSpace(control.Platform))
            throw new ArgumentException("Platform must not be empty.");

        foreach (var existing in _config.Controls)
        {
            if (ignoreAlias != null && existing.Alias == ignoreAlias)
                continue;
            if (existing.Alias == control.Alias)
                throw new ArgumentException($"Duplicate alias '{control.Alias}'.");
            if (existing.Identifier == control.Identifier)
                throw new ArgumentException($"Duplicate identifier '{control.Identifier}'.");
            if (!string.IsNullOrEmpty(control.RPMSensor)
                && !string.IsNullOrEmpty(existing.RPMSensor)
                && existing.RPMSensor == control.RPMSensor)
                throw new ArgumentException($"Duplicate RPMSensor '{control.RPMSensor}'.");
        }
    }

    private void WriteConfigUnlocked()
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

        if (config.BeatDetuneMinSeparationRpm <= 0)
            errors.Add($"BeatDetuneMinSeparationRpm must be positive (got {config.BeatDetuneMinSeparationRpm}).");

        if (config.BeatDetuneMaxNudgeRpm < 0)
            errors.Add($"BeatDetuneMaxNudgeRpm must be zero or positive (got {config.BeatDetuneMaxNudgeRpm}).");

        string[] validLogLevels = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];
        if (!validLogLevels.Contains(config.LogLevel))
            errors.Add($"LogLevel '{config.LogLevel}' is not valid. Must be one of: {string.Join(", ", validLogLevels)}.");

        if (config.Controls.Count == 0)
            Log.Warning("No controls defined in Controls. Fans will not be managed. Run list-sensors to discover identifiers, then add controls to config.json.");

        if (config.StatusServerEnabled && string.IsNullOrWhiteSpace(config.StatusServerBindAddress))
            errors.Add("StatusServerBindAddress must not be empty.");

        var profileNames = new HashSet<string>();
        var profiles = config.Profiles ?? [];
        for (int i = 0; i < profiles.Count; i++)
        {
            var name = profiles[i];
            if (string.IsNullOrWhiteSpace(name))
                errors.Add($"Profiles[{i}] must not be empty.");
            else if (!profileNames.Add(name))
                errors.Add($"Profiles[{i}]: Duplicate profile '{name}'.");
        }

        if (!string.IsNullOrEmpty(config.ActiveProfile) && !profileNames.Contains(config.ActiveProfile))
            errors.Add($"ActiveProfile '{config.ActiveProfile}' is not listed in Profiles.");

        var controlAliases = new HashSet<string>();
        var controlIdentifiers = new HashSet<string>();
        var rpmSensors = new HashSet<string>();
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

            if (!string.IsNullOrEmpty(c.RPMSensor) && !rpmSensors.Add(c.RPMSensor))
                errors.Add($"Controls[{i}] ('{c.Alias}'): Duplicate RPMSensor '{c.RPMSensor}'.");
        }

        var sensorAliases = new HashSet<string>();
        var sensorIdentifiers = new HashSet<string>();
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

    public Dictionary<string, ControlConfig> ControlConfigsByRPMSensorIdentifier => _controlConfigsByRPMSensorIdentifier;

    public HashSet<string> ControlRPMSensorIdentifiers => _controlRPMSensorIdentifiers;

    public Config Config => _config;

    internal string ConfigFilePath => _configFilePath;

    public (List<SensorConfig> Sensors, List<ControlConfig> Controls) SnapshotEntries()
    {
        lock (_configLock)
            return (_config.Sensors.ToList(), _config.Controls.ToList());
    }

    public float? ConvertRPMToPercent(string alias, float rpm)
    {
        if (!TryGetRpmCalibration(alias, out var rpmCalibration))
            return null;

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
        }

        return value;
    }

    public float? ConvertPercentToRpm(string alias, float percent)
    {
        if (!TryGetRpmCalibration(alias, out var rpmCalibration))
            return null;

        var sorted = rpmCalibration.OrderBy(point => point.Control).ToList();
        if (percent <= sorted[0].Control)
            return sorted[0].Rpm;
        if (percent >= sorted[^1].Control)
            return sorted[^1].Rpm;

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var lower = sorted[i];
            var upper = sorted[i + 1];
            var controlDelta = upper.Control - lower.Control;
            if (controlDelta == 0)
                continue;

            if (percent >= lower.Control && percent <= upper.Control)
                return lower.Rpm + (upper.Rpm - lower.Rpm) * ((percent - lower.Control) / controlDelta);
        }

        return null;
    }

    public bool TryGetEffectiveRpm(string alias, float requestedRpm, out float effectiveRpm, out float maxRpm)
    {
        effectiveRpm = 0f;
        maxRpm = 0f;
        if (!TryGetRpmCalibration(alias, out var rpmCalibration))
            return false;

        maxRpm = rpmCalibration[^1].Rpm;
        if (requestedRpm <= 0)
            effectiveRpm = requestedRpm;
        else if (requestedRpm <= rpmCalibration[0].Rpm)
            effectiveRpm = rpmCalibration[0].Rpm;
        else if (requestedRpm >= rpmCalibration[^1].Rpm)
            effectiveRpm = rpmCalibration[^1].Rpm;
        else
            effectiveRpm = requestedRpm;

        return true;
    }

    private bool TryGetRpmCalibration(string alias, out IReadOnlyList<RPMCalibrationData> rpmCalibration)
    {
        if (!_controlConfigsByAlias.TryGetValue(alias, out var controlConfig))
        {
            Log.Error("Control {Alias} not configured", alias);
            rpmCalibration = [];
            return false;
        }

        if (controlConfig.RPMCalibration.Count <= 1)
        {
            Log.Error("Control {Alias} has no valid RPM calibration data", alias);
            rpmCalibration = [];
            return false;
        }

        rpmCalibration = controlConfig.RPMCalibration;
        return true;
    }

}

















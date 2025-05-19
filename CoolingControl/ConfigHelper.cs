using System.Text.Json;
using Serilog;

namespace CoolingControl;

public class ConfigHelper
{
    private readonly Config _config;
    private readonly Dictionary<string, SensorConfig> _sensorConfigsByAlias;
    private readonly Dictionary<string, SensorConfig> _sensorConfigsByIdentifier;
    private readonly HashSet<string> _sensorIdentifiers;
    private readonly Dictionary<string, ControlConfig> _controlConfigsByAlias;
    private readonly Dictionary<string, ControlConfig> _controlConfigsByIdentifier;
    private readonly HashSet<string> _controlIdentifiers;

    public ConfigHelper(string configFilePath)
    {

        _config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configFilePath))
                    ?? throw new InvalidOperationException("Deserialized configuration is null.");
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
        File.WriteAllText("config/config.json", jsonString);
        Log.Information("Configuration saved successfully to config/config.json");
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
            Log.Debug("{Alias} {Rpm}RPM=>{Value}", alias, rpm, value);
        }
        else if (rpm > rpmCalibration[^1].Rpm)
        {
            value = rpmCalibration[^1].Control;
            Log.Debug("{Alias} {Rpm}RPM=>{Value}", alias, rpm, value);
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
                        Log.Debug("{Alias} {Rpm}RPM=>{Value}", alias, rpm, value);
                        break;
                    }

                    value = lower.Control + (upper.Control - lower.Control) * ((rpm - lower.Rpm) / rpmDelta);
                    Log.Debug("{Alias} {Rpm}RPM=>{Value}", alias, rpm, value);
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

















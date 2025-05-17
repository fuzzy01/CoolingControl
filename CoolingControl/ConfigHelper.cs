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

    public Dictionary<string, SensorConfig> SensorConfigsByAlias => _sensorConfigsByAlias;

    public Dictionary<string, SensorConfig> SensorConfigsByIdentifier => _sensorConfigsByIdentifier;

    public HashSet<string> SensorIdentifiers => _sensorIdentifiers;

    public Dictionary<string, ControlConfig> ControlConfigsByIdentifier => _controlConfigsByIdentifier;

    public Dictionary<string, ControlConfig> ControlConfigsByAlias => _controlConfigsByAlias;

    public HashSet<string> ControlIdentifiers => _controlIdentifiers;

    public Config Config => _config;
}

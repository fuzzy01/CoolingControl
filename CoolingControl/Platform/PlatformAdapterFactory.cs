namespace CoolingControl.Platform;

using CoolingControl.Platform.LHM;
using Serilog;

/// <summary>
/// Assembles a <see cref="CompositePlatformAdapter"/> from the built-in LHM adapter and any
/// plugins found in the <c>plugins/</c> directory next to the executable.
/// </summary>
public static class PlatformAdapterFactory
{
    private const string PluginsDir = "plugins";

    public static IPlatformAdapter Create(ConfigHelper config)
    {
        var adapters = new Dictionary<string, IPlatformAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            ["LHM"] = new LHMAdapter(config)
        };

        foreach (var (name, adapter) in PluginLoader.LoadAdapters(PluginsDir, config))
        {
            if (adapters.ContainsKey(name))
                Log.Warning("Plugin platform name '{Name}' conflicts with a built-in adapter — skipping", name);
            else
                adapters[name] = adapter;
        }

        var sensorPlatformMap = config.Config.Sensors
            .ToDictionary(s => s.Identifier, s => s.Platform, StringComparer.OrdinalIgnoreCase);

        // RPM sensor identifiers declared on controls need to be routable as sensors
        foreach (var ctrl in config.Config.Controls)
        {
            if (!string.IsNullOrEmpty(ctrl.RPMSensor) && !sensorPlatformMap.ContainsKey(ctrl.RPMSensor))
                sensorPlatformMap[ctrl.RPMSensor] = ctrl.Platform;
        }

        var controlPlatformMap = config.Config.Controls
            .ToDictionary(c => c.Identifier, c => c.Platform, StringComparer.OrdinalIgnoreCase);

        // Warn about platforms referenced in config that have no registered adapter
        foreach (var platform in sensorPlatformMap.Values.Concat(controlPlatformMap.Values).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!adapters.ContainsKey(platform))
                Log.Error("Platform '{Platform}' is referenced in config but no adapter is registered for it", platform);
        }

        return new CompositePlatformAdapter(adapters, sensorPlatformMap, controlPlatformMap);
    }
}

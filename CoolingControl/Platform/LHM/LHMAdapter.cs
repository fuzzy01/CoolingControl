namespace CoolingControl.Platform.LHM;

using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

/// <summary>
/// Adapter for hardware monitoring using LibreHardwareMonitor.
/// Manages the lifecycle of an underlying <see cref="Computer"/> instance
/// to retrieve sensor values and control hardware components.
/// </summary>
[PlatformAdapter("LHM")]
public class LHMAdapter : IPlatformAdapter, IDisposable
{
    private readonly Computer _computer;
    private readonly ConfigHelper _config;

    private record SensorCacheEntry(ISensor Sensor, IHardware Hardware);
    private Dictionary<string, SensorCacheEntry> _sensorCache = new();

    public LHMAdapter(ConfigHelper config)
    {
        _config = config;
        _computer = new Computer
        {
            IsCpuEnabled = config.Config.LHMConfig.CpuEnabled,
            IsGpuEnabled = config.Config.LHMConfig.GpuEnabled,
            IsMemoryEnabled = config.Config.LHMConfig.MemoryEnabled,
            IsStorageEnabled = config.Config.LHMConfig.StorageEnabled,
            IsNetworkEnabled = config.Config.LHMConfig.NetworkEnabled,
            IsMotherboardEnabled = config.Config.LHMConfig.MotherboardEnabled,
            IsControllerEnabled = config.Config.LHMConfig.ControllerEnabled,
            IsBatteryEnabled = config.Config.LHMConfig.BatteryEnabled,
            IsPsuEnabled = config.Config.LHMConfig.PsuEnabled
        };
        _computer.Open();
        BuildSensorCache();
    }

    public void Suspend()
    {
        if (!_config.Config.DisableLHMReleaseOnSuspend)
        {
            Log.Debug("Suspending hardware monitoring");
            _sensorCache.Clear();
            _computer.Close();
        }
    }

    public void Resume()
    {
        if (!_config.Config.DisableLHMReleaseOnSuspend)
        {
            Log.Debug("Resuming hardware monitoring");
            _computer.Open();
            BuildSensorCache();
        }
    }

    public Dictionary<string, float?> GetSensorValues(HashSet<string> sensorIdentifiers)
    {
        UpdateHardwareFor(sensorIdentifiers);
        return sensorIdentifiers.ToDictionary(
            id => id,
            id => _sensorCache.TryGetValue(id, out var e) ? e.Sensor.Value : (float?)null);
    }

    public Dictionary<string, float?> GetControlValues(HashSet<string> controlIdentifiers)
    {
        UpdateHardwareFor(controlIdentifiers);
        return controlIdentifiers.ToDictionary(
            id => id,
            id => _sensorCache.TryGetValue(id, out var e) ? e.Sensor.Value : (float?)null);
    }

    public Dictionary<string, bool> SetControls(Dictionary<string, float> controlValues)
    {
        var result = controlValues.ToDictionary(kv => kv.Key, _ => false);
        foreach (var (id, value) in controlValues)
        {
            if (!_sensorCache.TryGetValue(id, out var entry))
                continue;
            ApplyControl(entry.Sensor, value, result);
        }
        return result;
    }

    public Dictionary<string, bool> ReleaseControls(HashSet<string> controlIdentifiers)
    {
        var controlValues = controlIdentifiers.ToDictionary(f => f, f => IPlatformAdapter.DefaultControlValue);
        return SetControls(controlValues);
    }

    public void ListAllSensors()
    {
        var loggableSensorTypes = new HashSet<SensorType>
        {
            SensorType.Power,
            SensorType.Temperature,
            SensorType.Fan,
            SensorType.Load,
            SensorType.Control,
            SensorType.Level,
            SensorType.Frequency,
            SensorType.Flow,
            SensorType.Noise,
            SensorType.Humidity
        };

        Log.Information("Platform: LHM");
        Log.Information("Computer: {Name}", _computer.GetType().Name);
        foreach (var hardware in _computer.Hardware)
        {
            Log.Information("  Hardware: {Name} (Type: {HardwareType})", hardware.Name, hardware.HardwareType);
            hardware.Update();
            LogSensors(hardware, loggableSensorTypes, 2);
            LogSubHardware(hardware, loggableSensorTypes, 2);
        }
    }

    private void BuildSensorCache()
    {
        _sensorCache.Clear();
        foreach (var hw in _computer.Hardware)
            IndexHardware(hw);
    }

    private void IndexHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var sensor in hardware.Sensors)
            _sensorCache[sensor.Identifier.ToString()] = new SensorCacheEntry(sensor, hardware);
        foreach (var sub in hardware.SubHardware)
            IndexHardware(sub);
    }

    private void UpdateHardwareFor(HashSet<string> identifiers)
    {
        var toUpdate = new HashSet<IHardware>();
        foreach (var id in identifiers)
            if (_sensorCache.TryGetValue(id, out var e))
                toUpdate.Add(e.Hardware);
        foreach (var hw in toUpdate)
            hw.Update();
    }

    private static void ApplyControl(ISensor sensor, float value, Dictionary<string, bool> result)
    { 
        var id = sensor.Identifier.ToString();
        if (sensor.Control == null)
        {
            Log.Error("Control for {Identifier} is not available", id);
            return;
        }

        try
        {
            if (value == IPlatformAdapter.DefaultControlValue)
            {
                sensor.Control.SetDefault();
                result[id] = true;
                Log.Debug("Set control {Identifier} to default", id);
            }
            else
            {
                var clamped = Math.Clamp(value, sensor.Control.MinSoftwareValue, sensor.Control.MaxSoftwareValue);
                sensor.Control.SetSoftware(clamped);
                result[id] = true;
                Log.Debug("Set control {Identifier} to {ControlValue}", id, clamped);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set control {Identifier}: {Message}", id, ex.Message);
            try
            {
                sensor.Control.SetDefault();
                Log.Error("Reverted control {Identifier} to default", id);
            }
            catch (Exception revertEx)
            {
                Log.Error(revertEx, "Failed to revert control {Identifier} to default: {Message}", id, revertEx.Message);
            }
        }
    }

    private static void LogSubHardware(IHardware hardware, HashSet<SensorType> loggableSensorTypes, int indent)
    {
        var pad = new string(' ', 2 * indent);
        indent ++;
        foreach (var sub in hardware.SubHardware)
        {
            Log.Information("{Pad}SubHardware: {Name} (Type: {HardwareType})", pad, sub.Name, sub.HardwareType);
            sub.Update();
            LogSensors(sub, loggableSensorTypes, indent);
            LogSubHardware(sub, loggableSensorTypes, indent);
        }
    }

    private static void LogSensors(IHardware hardware, HashSet<SensorType> loggableSensorTypes, int indent)
    {
        var pad = new string(' ', 2 * indent);
        foreach (var sensor in hardware.Sensors)
        {
            if (!loggableSensorTypes.Contains(sensor.SensorType))
                continue;
            Log.Information(
                "{Pad}Sensor: {Name} (Type: {SensorType}, Identifier: {Identifier}, Value: {Value}, Max: {Max}, Min: {Min}, Unit: {Unit})",
                pad, sensor.Name, sensor.SensorType, sensor.Identifier,
                sensor.Value.HasValue ? sensor.Value.Value : "N/A",
                sensor.Max.HasValue ? sensor.Max.Value : "N/A",
                sensor.Min.HasValue ? sensor.Min.Value : "N/A",
                GetSensorUnit(sensor.SensorType));
        }
    }

    private static string GetSensorUnit(SensorType sensorType) => sensorType switch
    {
        SensorType.Temperature => "°C",
        SensorType.Humidity => "%",
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Power => "W",
        SensorType.Fan => "RPM",
        SensorType.Clock => "MHz",
        SensorType.Load => "%",
        SensorType.Control => "%",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Throughput => "MB/s",
        SensorType.Level => "%",
        SensorType.Frequency => "Hz",
        SensorType.Flow => "L/min",
        _ => ""
    };

    private bool _disposed = false;

    ~LHMAdapter()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _computer?.Close();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

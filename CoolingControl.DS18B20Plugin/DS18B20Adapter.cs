namespace CoolingControl.DS18B20Plugin;

using System.Collections.Concurrent;
using System.IO.Ports;
using CoolingControl.Platform;
using Serilog;

/// <summary>
/// Platform adapter for DS18B20 temperature sensors connected through a USB-to-serial adapter
/// that streams ASCII readings such as <c>t1=+28.70</c> at 9600 8N1.
/// </summary>
/// <remarks>
/// Sensor identifiers must be of the form <c>&lt;COMPORT&gt;/&lt;tag&gt;</c>, e.g. <c>COM5/t1</c>.
/// This adapter is sensor-only; it exposes no controls.
/// </remarks>
[PlatformAdapter(PlatformName)]
public sealed class DS18B20Adapter : IPlatformAdapter
{
    public const string PlatformName = "DS18B20";

    // Readings older than this are treated as stale (adapter disconnected or port dead).
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, SerialPortManager> _managers =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public DS18B20Adapter(ConfigHelper config)
    {
        var ports = config.Config.Sensors
            .Where(s => string.Equals(s.Platform, PlatformName, StringComparison.OrdinalIgnoreCase))
            .Select(s => TryParseIdentifier(s.Identifier, out var port, out _) ? port : null)
            .Where(port => port != null)
            .Select(port => port!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var port in ports)
            GetOrCreateManager(port);
    }

    public Dictionary<string, float?> GetSensorValues(HashSet<string> sensorIdentifiers) =>
        sensorIdentifiers.ToDictionary(id => id, GetValue);

    public Dictionary<string, float?> GetControlValues(HashSet<string> controlIdentifiers)
    {
        foreach (var id in controlIdentifiers)
            Log.Error("DS18B20: '{Id}' requested as a control, but this platform is sensor-only", id);
        return controlIdentifiers.ToDictionary(id => id, _ => (float?)null);
    }

    public Dictionary<string, bool> SetControls(Dictionary<string, float> controlValues)
    {
        foreach (var id in controlValues.Keys)
            Log.Error("DS18B20: '{Id}' cannot be set, this platform is sensor-only", id);
        return controlValues.Keys.ToDictionary(id => id, _ => false);
    }

    public Dictionary<string, bool> ReleaseControls(HashSet<string> controlIdentifiers) =>
        controlIdentifiers.ToDictionary(id => id, _ => false);

    public void ListAllSensors()
    {
        Log.Information("Platform: DS18B20 - configured ports: {Ports}", string.Join(", ", _managers.Keys));
        Log.Information("Platform: DS18B20 - available Windows COM ports: {Available}",
            string.Join(", ", SerialPort.GetPortNames()));

        foreach (var (port, manager) in _managers)
        {
            foreach (var (tag, reading) in manager.Snapshot())
            {
                var ageMs = (DateTime.UtcNow - reading.TimestampUtc).TotalMilliseconds;
                Log.Information("Platform: DS18B20 - {Port}/{Tag} = {Value} (age {AgeMs:F0}ms)",
                    port, tag, reading.Value, ageMs);
            }
        }
    }

    public void Suspend()
    {
        Log.Debug("Platform: DS18B20 - Suspend");
        foreach (var manager in _managers.Values)
            manager.Stop();
    }

    public void Resume()
    {
        Log.Debug("Platform: DS18B20 - Resume");
        foreach (var manager in _managers.Values)
            manager.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var manager in _managers.Values)
            manager.Dispose();
        _managers.Clear();

        GC.SuppressFinalize(this);
    }

    private float? GetValue(string identifier)
    {
        if (!TryParseIdentifier(identifier, out var port, out var tag))
        {
            Log.Error("DS18B20: identifier '{Id}' is not in the expected '<COMPORT>/<tag>' format", identifier);
            return null;
        }

        var manager = GetOrCreateManager(port);
        return manager.TryGetValue(tag, StaleAfter);
    }

    private SerialPortManager GetOrCreateManager(string port)
    {
        return _managers.GetOrAdd(port, p =>
        {
            var manager = new SerialPortManager(p);
            manager.Start();
            return manager;
        });
    }

    /// <summary>Parses an identifier of the form <c>COM5/t1</c> into its port name and lower-cased tag.</summary>
    private static bool TryParseIdentifier(string identifier, out string port, out string tag)
    {
        port = string.Empty;
        tag = string.Empty;

        var separatorIndex = identifier.IndexOf('/');
        if (separatorIndex <= 0 || separatorIndex == identifier.Length - 1)
            return false;

        port = identifier[..separatorIndex];
        tag = identifier[(separatorIndex + 1)..].ToLowerInvariant();
        return true;
    }
}

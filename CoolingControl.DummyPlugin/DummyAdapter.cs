namespace CoolingControl.DummyPlugin;

using CoolingControl.Platform;
using Serilog;

[PlatformAdapter("Dummy")]
public sealed class DummyAdapter : IPlatformAdapter
{
    private readonly Dictionary<string, float> _controlValues = new();

    public Dictionary<string, float?> GetSensorValues(HashSet<string> sensorIdentifiers) =>
        sensorIdentifiers.ToDictionary(id => id, _ => (float?)50.0f);

    public Dictionary<string, float?> GetControlValues(HashSet<string> controlIdentifiers) =>
        controlIdentifiers.ToDictionary(id => id,
            id => (float?)(_controlValues.TryGetValue(id, out var v) ? v : 50.0f));

    public Dictionary<string, bool> SetControls(Dictionary<string, float> controlValues)
    {
        foreach (var kvp in controlValues)
            _controlValues[kvp.Key] = kvp.Value;
        return controlValues.ToDictionary(kvp => kvp.Key, _ => true);
    }

    public Dictionary<string, bool> ReleaseControls(HashSet<string> controlIdentifiers)
    {
        foreach (var id in controlIdentifiers)
            _controlValues.Remove(id);
        return controlIdentifiers.ToDictionary(id => id, _ => true);
    }

    public void ListAllSensors() =>
        Log.Information("Platform: Dummy");

    public void Suspend() => Log.Debug("Platform: Dummy - Suspend");

    public void Resume() => Log.Debug("Platform: Dummy - Resume");

    public void Dispose() { }
}

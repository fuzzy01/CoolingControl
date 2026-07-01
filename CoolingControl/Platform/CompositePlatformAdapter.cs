namespace CoolingControl.Platform;

using Serilog;

/// <summary>
/// Routes <see cref="IPlatformAdapter"/> calls to the appropriate underlying adapter based on
/// each identifier's declared platform. Fan-outs (Suspend, Resume, Dispose, ListAllSensors)
/// are dispatched to every registered adapter.
/// </summary>
public sealed class CompositePlatformAdapter : IPlatformAdapter
{
    private readonly Dictionary<string, IPlatformAdapter> _adapters;
    private readonly Dictionary<string, string> _sensorPlatformMap;
    private readonly Dictionary<string, string> _controlPlatformMap;
    private bool _disposed;

    public CompositePlatformAdapter(
        Dictionary<string, IPlatformAdapter> adapters,
        Dictionary<string, string> sensorPlatformMap,
        Dictionary<string, string> controlPlatformMap)
    {
        _adapters = adapters;
        _sensorPlatformMap = sensorPlatformMap;
        _controlPlatformMap = controlPlatformMap;
    }

    public Dictionary<string, float?> GetSensorValues(HashSet<string> sensorIdentifiers)
    {
        return DispatchGet(sensorIdentifiers, _sensorPlatformMap,
            (adapter, ids) => adapter.GetSensorValues(ids));
    }

    public Dictionary<string, float?> GetControlValues(HashSet<string> controlIdentifiers)
    {
        return DispatchGet(controlIdentifiers, _controlPlatformMap,
            (adapter, ids) => adapter.GetControlValues(ids));
    }

    public Dictionary<string, bool> SetControls(Dictionary<string, float> controlValues)
    {
        var result = new Dictionary<string, bool>();
        foreach (var (ids, adapter) in GroupByPlatform(controlValues.Keys, _controlPlatformMap))
        {
            var controlSubset = controlValues
                .Where(kvp => ids.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            
            Dictionary<string, bool> res = adapter.SetControls(controlSubset);
            
            foreach (var kvp in res)
            {
                result[kvp.Key] = kvp.Value;
            }

        }
        return result;
    }

    public Dictionary<string, bool> ReleaseControls(HashSet<string> controlIdentifiers)
    {
        var result = new Dictionary<string, bool>();
        foreach (var (ids, adapter) in GroupByPlatform(controlIdentifiers, _controlPlatformMap))
        {
            foreach (var kvp in adapter.ReleaseControls(ids))
            {
                result[kvp.Key] = kvp.Value;
            }

        }
        return result;
    }

    public void ListAllSensors()
    {
        foreach (var adapter in _adapters.Values)
            adapter.ListAllSensors();
    }

    public void Suspend()
    {
        foreach (var adapter in _adapters.Values)
            adapter.Suspend();
    }

    public void Resume()
    {
        foreach (var adapter in _adapters.Values)
            adapter.Resume();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        foreach (var adapter in _adapters.Values)
        {
            adapter.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    // Groups identifiers by their adapter, logging errors for any unmapped ones.
    private List<(HashSet<string> ids, IPlatformAdapter adapter)> GroupByPlatform(
        IEnumerable<string> identifiers, Dictionary<string, string> platformMap)
    {
        var groups = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in identifiers)
        {
            if (!platformMap.TryGetValue(id, out var platformName))
            {
                Log.Error("Identifier '{Id}' has no platform mapping", id);
                continue;
            }
            if (!_adapters.TryGetValue(platformName, out _))
            {
                Log.Error("Platform '{Platform}' for identifier '{Id}' is not registered", platformName, id);
                continue;
            }
            if (!groups.TryGetValue(platformName, out var set))
                groups[platformName] = set = [];
            set.Add(id);
        }

        return groups.Select(kvp => (kvp.Value, _adapters[kvp.Key])).ToList();
    }

    private Dictionary<string, float?> DispatchGet(
        HashSet<string> identifiers,
        Dictionary<string, string> platformMap,
        Func<IPlatformAdapter, HashSet<string>, Dictionary<string, float?>> call)
    {
        var result = new Dictionary<string, float?>();
        foreach (var (ids, adapter) in GroupByPlatform(identifiers, platformMap))
        {
            foreach (var kvp in call(adapter, ids))
                result[kvp.Key] = kvp.Value;
        }
        return result;
    }
}

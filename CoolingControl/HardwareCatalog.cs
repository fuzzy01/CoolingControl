namespace CoolingControl;

using CoolingControl.Platform;

public sealed class HardwareCatalog
{
    private readonly object _gate = new();
    private List<HardwareChannel> _rows = [];
    private bool _refreshRequested;
    private int _generation;

    public IReadOnlyList<HardwareChannel> Rows => _rows;

    public bool IsRefreshRequested
    {
        get
        {
            lock (_gate)
                return _refreshRequested;
        }
    }

    public void Refresh(IPlatformAdapter adapter)
    {
        _rows = adapter.GetHardwareCatalog().ToList();
    }

    public void Publish(IReadOnlyList<HardwareChannel> rows)
    {
        _rows = rows.ToList();
        lock (_gate)
        {
            _refreshRequested = false;
            _generation++;
            Monitor.PulseAll(_gate);
        }
    }

    public IReadOnlyList<HardwareChannel> WaitForRefresh(TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        lock (_gate)
        {
            var seen = _generation;
            _refreshRequested = true;
            while (_generation == seen)
            {
                var remaining = deadline - Environment.TickCount64;
                if (remaining <= 0)
                    break;
                if (!Monitor.Wait(_gate, TimeSpan.FromMilliseconds(remaining)))
                    break;
            }

            return _rows;
        }
    }
}

internal static class HardwareSelection
{
    public static void EnsureSensorChannel(IEnumerable<HardwareChannel> catalog, string platform, string identifier)
    {
        var channel = Find(catalog, platform, identifier);
        if (channel.IsControl)
            throw new ArgumentException($"'{identifier}' is a control channel.");
    }

    public static void EnsureControlChannel(IEnumerable<HardwareChannel> catalog, string platform, string identifier)
    {
        var channel = Find(catalog, platform, identifier);
        if (!channel.IsControl)
            throw new ArgumentException($"'{identifier}' is not a control channel.");
    }

    public static void EnsureFanChannel(IEnumerable<HardwareChannel> catalog, string platform, string? rpmSensor)
    {
        if (string.IsNullOrEmpty(rpmSensor))
            return;

        var channel = Find(catalog, platform, rpmSensor);
        if (channel.IsControl || !string.Equals(channel.SensorType, "Fan", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{rpmSensor}' is not a Fan channel on platform '{platform}'.");
    }

    private static HardwareChannel Find(IEnumerable<HardwareChannel> catalog, string platform, string identifier)
    {
        var channel = catalog.FirstOrDefault(item =>
            item.Platform == platform && item.Identifier == identifier);
        if (channel == null)
            throw new ArgumentException($"'{platform}' '{identifier}' is not in the hardware catalog.");
        return channel;
    }
}

namespace CoolingControl;

using System;
using System.Collections.Generic;

public interface IStatusSnapshot
{
    void Update(Dictionary<string, float?> sensors, Dictionary<string, float> controls, DateTime timestamp);
    (Dictionary<string, float?> sensors, Dictionary<string, float> controls, DateTime timestamp) GetSnapshot();
    DateTime StartTime { get; }
}

public class StatusSnapshot : IStatusSnapshot
{
    private readonly object _lockObj = new();
    private Dictionary<string, float?> _lastSensorValues = new();
    private Dictionary<string, float> _lastControlValues = new();
    private DateTime _lastUpdateTime = DateTime.UtcNow;
    public DateTime StartTime { get; } = DateTime.UtcNow;

    public void Update(Dictionary<string, float?> sensors, Dictionary<string, float> controls, DateTime timestamp)
    {
        lock (_lockObj)
        {
            _lastSensorValues = new Dictionary<string, float?>(sensors);
            _lastControlValues = new Dictionary<string, float>(controls);
            _lastUpdateTime = timestamp;
        }
    }

    public (Dictionary<string, float?> sensors, Dictionary<string, float> controls, DateTime timestamp) GetSnapshot()
    {
        lock (_lockObj)
        {
            return (
                new Dictionary<string, float?>(_lastSensorValues),
                new Dictionary<string, float>(_lastControlValues),
                _lastUpdateTime
            );
        }
    }
}

namespace CoolingControl;

using System;
using System.Collections.Generic;

public interface IStatusSnapshot
{
    void Update(Dictionary<string, float?> sensors, Dictionary<string, float> controls,
                Dictionary<string, float?> controlRpm, DateTime timestamp);
    (Dictionary<string, float?> sensors, Dictionary<string, float> controls,
     Dictionary<string, float?> controlRpm, DateTime timestamp) GetSnapshot();
    (Dictionary<string, List<float?>> sensors, Dictionary<string, List<float>> controls) GetHistory();
    DateTime StartTime { get; }
}

public class StatusSnapshot : IStatusSnapshot
{
    private readonly object _lockObj = new();
    private readonly Dictionary<string, Queue<float?>> _sensorHistory = new();
    private readonly Dictionary<string, Queue<float>> _controlHistory = new();
    private Dictionary<string, float?> _lastSensorValues = new();
    private Dictionary<string, float> _lastControlValues = new();
    private Dictionary<string, float?> _lastControlRpmValues = new();
    private DateTime _lastUpdateTime = DateTime.UtcNow;
    private const int MaxHistorySize = 300; // 5 minutes at 1 sample/sec

    public DateTime StartTime { get; } = DateTime.UtcNow;

    public void Update(Dictionary<string, float?> sensors, Dictionary<string, float> controls,
                       Dictionary<string, float?> controlRpm, DateTime timestamp)
    {
        lock (_lockObj)
        {
            _lastSensorValues = new Dictionary<string, float?>(sensors);
            _lastControlValues = new Dictionary<string, float>(controls);
            _lastControlRpmValues = new Dictionary<string, float?>(controlRpm);
            _lastUpdateTime = timestamp;

            // Append to history and prune to max size
            foreach (var kvp in sensors)
            {
                if (!_sensorHistory.ContainsKey(kvp.Key))
                    _sensorHistory[kvp.Key] = new Queue<float?>();
                _sensorHistory[kvp.Key].Enqueue(kvp.Value);
                while (_sensorHistory[kvp.Key].Count > MaxHistorySize)
                    _sensorHistory[kvp.Key].Dequeue();
            }

            foreach (var kvp in controls)
            {
                if (!_controlHistory.ContainsKey(kvp.Key))
                    _controlHistory[kvp.Key] = new Queue<float>();
                _controlHistory[kvp.Key].Enqueue(kvp.Value);
                while (_controlHistory[kvp.Key].Count > MaxHistorySize)
                    _controlHistory[kvp.Key].Dequeue();
            }
        }
    }

    public (Dictionary<string, float?> sensors, Dictionary<string, float> controls,
            Dictionary<string, float?> controlRpm, DateTime timestamp) GetSnapshot()
    {
        lock (_lockObj)
        {
            return (
                new Dictionary<string, float?>(_lastSensorValues),
                new Dictionary<string, float>(_lastControlValues),
                new Dictionary<string, float?>(_lastControlRpmValues),
                _lastUpdateTime
            );
        }
    }

    public (Dictionary<string, List<float?>> sensors, Dictionary<string, List<float>> controls) GetHistory()
    {
        lock (_lockObj)
        {
            var sensorHistoryDict = new Dictionary<string, List<float?>>();
            foreach (var kvp in _sensorHistory)
                sensorHistoryDict[kvp.Key] = new List<float?>(kvp.Value);

            var controlHistoryDict = new Dictionary<string, List<float>>();
            foreach (var kvp in _controlHistory)
                controlHistoryDict[kvp.Key] = new List<float>(kvp.Value);

            return (sensorHistoryDict, controlHistoryDict);
        }
    }
}

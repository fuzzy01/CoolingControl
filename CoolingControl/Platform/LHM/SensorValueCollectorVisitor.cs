namespace CoolingControl.Platform.LHM;

using LibreHardwareMonitor.Hardware;

/// <summary>
/// A visitor implementation for collecting sensor values from a computer's hardware components.
/// </summary>
public class SensorValueCollectorVisitor : IVisitor
{
    private readonly HashSet<string> _targetIdentifiers;
    private readonly Dictionary<string, float?> _sensorValues;

    public SensorValueCollectorVisitor(HashSet<string> targetIdentifiers)
    {
        _targetIdentifiers = targetIdentifiers;
        _sensorValues = [];
    }

    public Dictionary<string, float?> SensorValues => _sensorValues;

    public void VisitComputer(IComputer computer)
    {
        computer.Traverse(this);
    }

    public void VisitHardware(IHardware hardware)
    {
        // Refresh sensor data
        hardware.Update();

        // Visit sub-hardware
        foreach (IHardware subHardware in hardware.SubHardware)
        {
            subHardware.Accept(this);
        }

        // Visit sensors
        foreach (ISensor sensor in hardware.Sensors)
        {
            sensor.Accept(this);
        }
    }

    public void VisitSensor(ISensor sensor)
    {
        if (_targetIdentifiers.Contains(sensor.Identifier.ToString()))
        {
            // Store value (nullable)
            _sensorValues[sensor.Identifier.ToString()] = sensor.Value;
        }
    }

    public void VisitParameter(IParameter parameter)
    {
        // Parameters are not relevant for this task
    }
}
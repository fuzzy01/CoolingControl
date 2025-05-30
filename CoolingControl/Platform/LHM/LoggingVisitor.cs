namespace CoolingControl.Platform.LHM;

using LibreHardwareMonitor.Hardware;
using Serilog;

/// <summary>
/// A visitor implementation for logging the structure and details of a computer system,
/// including its hardware, sensors, and parameters.
/// </summary>
public class LoggingVisitor : IVisitor
{
    private readonly ILogger _logger;
    private readonly HashSet<SensorType> _loggableSensorTypes;
    private int _indentLevel = 0;

    public LoggingVisitor(ILogger logger, HashSet<SensorType> loggableSensorTypes)
    {
        _logger = logger;
        _loggableSensorTypes = loggableSensorTypes;
    }

    private string GetIndent() => new string(' ', _indentLevel * 2);

    public void VisitComputer(IComputer computer)
    {
        _logger.Information("{Indent}Computer: {Name}", GetIndent(), computer.GetType().Name);
        _indentLevel++;
        computer.Traverse(this);
        _indentLevel--;
    }

    public void VisitHardware(IHardware hardware)
    {
        if (_indentLevel == 1)
        {
            _logger.Information("{Indent}Hardware: {Name} (Type: {HardwareType})",
                GetIndent(), hardware.Name, hardware.HardwareType);
        }
        _indentLevel++;

        // Refresh sensor data
        hardware.Update();

        // Visit sub-hardware
        foreach (IHardware subHardware in hardware.SubHardware)
        {
            _logger.Information("{Indent}SubHardware: {Name} (Type: {HardwareType})",
                GetIndent(), subHardware.Name, subHardware.HardwareType);
            _indentLevel++;
            subHardware.Accept(this);
            _indentLevel--;
        }

        // Visit sensors
        foreach (ISensor sensor in hardware.Sensors)
        {
            sensor.Accept(this);
        }

        _indentLevel--;
    }

    public void VisitSensor(ISensor sensor)
    {
        if (_loggableSensorTypes.Contains(sensor.SensorType))
        {
            _logger.Information("{Indent}Sensor: {Name} (Type: {SensorType}, Identifier: {Identifier}, Value: {Value}, Max: {Max}, Min: {Min}, Unit: {Unit})",
                GetIndent(), sensor.Name, sensor.SensorType, sensor.Identifier,
                sensor.Value.HasValue ? sensor.Value.Value : "N/A",
                sensor.Max.HasValue ? sensor.Max.Value : "N/A",
                sensor.Min.HasValue ? sensor.Min.Value : "N/A",
                    GetSensorUnit(sensor.SensorType));
        }
    }

    public void VisitParameter(IParameter parameter)
    {
        _logger.Information("{Indent}Parameter: {Name} (Value: {Value})",
            GetIndent(), parameter.Name, parameter.Value);
    }

    // Helper method to determine the unit for sensor types
    public static string GetSensorUnit(SensorType sensorType)
    {
        return sensorType switch
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
    }
}
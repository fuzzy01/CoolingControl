namespace CoolingControl.Platform.LHM;

using LibreHardwareMonitor.Hardware;
using Serilog;

/// <summary>
/// A visitor implementation for setting control values on hardware sensors.
/// </summary>
public class ControlSetterVisitor : IVisitor
{
    private readonly Dictionary<string, float> _controlValues;
    private readonly Dictionary<string, bool> _setControls;

    public ControlSetterVisitor(Dictionary<string, float> controlValues)
    {
        _controlValues = controlValues;
        _setControls = _controlValues.ToDictionary(f => f.Key, _ => false);
    }

    public Dictionary<string, bool> SetControls => _setControls;

    public void VisitComputer(IComputer computer)
    {
        computer.Traverse(this);
    }

    public void VisitHardware(IHardware hardware)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.SensorType == SensorType.Control)
            {
                string identifier = sensor.Identifier.ToString();
                if (!_controlValues.TryGetValue(identifier, out var controlValue))
                {
                    continue;
                }

                if (sensor.Control == null)
                {
                    Log.Error("Control for {Identifier} is not available", sensor.Identifier.ToString);
                    continue;
                }

                try
                {
                    // Set the control value
                    if (controlValue == IPlatformAdapter.DefaultControlValue)
                    {
                        // release the control 
                        sensor.Control.SetDefault();
                        _setControls[identifier] = true;
                        Log.Debug("Set control {Identifier} to default", sensor.Identifier);
                    }
                    else
                    {
                        controlValue = Math.Clamp(controlValue, sensor.Control.MinSoftwareValue, sensor.Control.MaxSoftwareValue);
                        sensor.Control.SetSoftware(controlValue);
                        _setControls[identifier] = true;
                        Log.Debug("Set control {Identifier} to {ControlValue}", sensor.Identifier, controlValue);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to set control {Identifier}: {Message}", identifier, ex.Message);
                    // Failsafe: Revert to default control
                    try
                    {
                        sensor.Control.SetDefault();
                        Log.Error("Reverted control {Identifier} to default", identifier);
                    }
                    catch (Exception revertEx)
                    {
                        Log.Error(revertEx, "Failed to revert control {Identifier} to default: {Message}", identifier, revertEx.Message);
                    }
                }
            }
        }

        // Visit sub-hardware
        foreach (IHardware subHardware in hardware.SubHardware)
        {
            subHardware.Accept(this);
        }
    }

    public void VisitSensor(ISensor sensor)
    {
        // Handled in VisitHardware to avoid redundant checks
    }

    public void VisitParameter(IParameter parameter)
    {
        // Parameters are not relevant for this task
    }
}

namespace CoolingControl.Platform.LHM;

using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

public class LHMAdapter : IPlatformAdapter, IDisposable
{
    private readonly Computer _computer;

    public LHMAdapter(ConfigHelper config) 
    {
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
    }
    
    public void Suspend()
    {
        Log.Debug("Suspending hardware monitoring");
        _computer.Close();
    }

    public void Resume()
    {
        Log.Debug("Resuming hardware monitoring");
        _computer.Open();
     }

    public Dictionary<string, float?> GetSensorValues(HashSet<string> sensorIdentifiers)
    {
        var visitor = new SensorValueCollectorVisitor(sensorIdentifiers);
        _computer.Accept(visitor);
        return visitor.SensorValues;
    }

    public Dictionary<string, float?> GetControlValues(HashSet<string> controlIdentifiers)
    {
        var visitor = new SensorValueCollectorVisitor(controlIdentifiers);
        _computer.Accept(visitor);
        return visitor.SensorValues;
    }

    public Dictionary<string, bool> SetControls(Dictionary<string, float> controlValues)
    {
        var controlSetterVisitor = new ControlSetterVisitor(controlValues);
        _computer.Accept(controlSetterVisitor);
        return controlSetterVisitor.SetControls;
    }

    public Dictionary<string, bool> ReleaseControls(HashSet<string> controlIdentifiers)
    {
        Dictionary<string, float> controlValues = controlIdentifiers.ToDictionary(f => f, f => ControlSetterVisitor.DefaultControlValue);
        var controlSetterVisitor = new ControlSetterVisitor(controlValues);
        _computer.Accept(controlSetterVisitor);
        return controlSetterVisitor.SetControls;
    }

    public void ListAllSensors()
    {
        var visitor = new LoggingVisitor(Log.Logger);
        _computer.Accept(visitor);
    }

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

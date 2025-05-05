namespace CoolingControl.Platform;

using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

/// <summary>
/// Provides a default implementation for calibrating RPM controls on a hardware platform.
/// Handles configuration, value tracking, and logic for interacting with controls and sensors.
/// </summary>
/// <remarks>
/// This class implements the <see cref="IRPMCalibrator"/> interface and provides logic for managing sensors and controls,
/// including value adjustment, state tracking, and control application.
/// </remarks>
public class DefaultRPMCalibrator : IRPMCalibrator
{
    private readonly IPlatformAdapter _adapter;
    private readonly Dictionary<string, ControlConfig> _controlConfigsByAlias;

    public DefaultRPMCalibrator(Config config, IPlatformAdapter adapter)
    {
        _adapter = adapter;
        _controlConfigsByAlias = config.Controls.ToDictionary(f => f.Alias, f => f);
    }


    public float? GetControlValue(string alias)
    {
        if (!_controlConfigsByAlias.TryGetValue(alias, out var controlConfig))
        {
            Log.Error("Control {Alias} not configured", alias);
            return null;
        }

        var res = _adapter.GetControlValues([controlConfig.Identifier]);
        if (!res.TryGetValue(controlConfig.Identifier, out var value))
        {
            Log.Error("Failed to retrieve control value for {Alias}", alias);    
            return null;    
        }
        return value;
    }

    public float? GetRPMSensorValue(string alias)
    {
        if (!_controlConfigsByAlias.TryGetValue(alias, out var controlConfig))
        {
            Log.Error("Control {Alias} not configured", alias);
            return null;
        }

        if (string.IsNullOrEmpty(controlConfig.RPMSensor))
        {
            Log.Error("Control {Alias} RPM sensor not configured", alias);
            return null;
        }

        var res = _adapter.GetSensorValues([controlConfig.RPMSensor]);
        if (!res.TryGetValue(controlConfig.RPMSensor, out var value))
        {
            Log.Error("Failed to retrieve RPM sensor value for {Alias}", alias);    
            return null;    
        }
        return value;
    }

    public bool SetControl(string alias, float controlValue)
    {
        if (!_controlConfigsByAlias.TryGetValue(alias, out var controlConfig))
        {
            Log.Error("Control {Alias} not configured", alias);
            return false;
        }

        var res = _adapter.SetControls(new Dictionary<string, float> { { controlConfig.Identifier, controlValue } });

        if (!res.TryGetValue(controlConfig.Identifier, out var value) || !value)
        {
            Log.Error("Failed to set control value for {Alias}", alias);
            return false;
        }
        return value;
    }

    public bool ReleaseControl(string alias)
    {
        if (!_controlConfigsByAlias.TryGetValue(alias, out var controlConfig))
        {
            Log.Error("Control {Alias} not configured", alias);
            return false;
        }

        var res = _adapter.ReleaseControls([controlConfig.Identifier]);
        if (!res.TryGetValue(controlConfig.Identifier, out var value) || !value)
        {
            Log.Error("Failed to release control for {Alias}", alias);
            return false;
        }  
        return value; 
    }


    private bool _disposed = false;

    ~DefaultRPMCalibrator()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _adapter?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

}
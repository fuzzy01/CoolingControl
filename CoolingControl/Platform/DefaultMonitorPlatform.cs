namespace CoolingControl.Platform;

using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Unix.Native;
using Serilog;

/// <summary>
/// Provides a base implementation for monitoring and controlling hardware platforms with sensors and controls.
/// Handles configuration, value tracking, and logic for step changes, minimum start/stop thresholds, and state management.
/// </summary>
/// <remarks>
/// This abstract class implements <see cref="IMonitoringPlatform"/> and <see cref="IRPMCalibrator"/> interfaces,
/// and provides common logic for managing sensors and controls, including value adjustment, state tracking,
/// and control application. Platform-specific implementations must override the abstract methods to interact with hardware.
/// </remarks>
public class DefaultMonitorPlatform : IMonitoringPlatform
{
    private readonly IPlatformAdapter _adapter;
    private readonly Dictionary<string, SensorConfig> _sensorConfigsByIdentifier;
    private readonly HashSet<string> _sensorIdentifiers;
    private readonly Dictionary<string, ControlConfig> _controlConfigsByAlias;
    private readonly Dictionary<string, ControlConfig> _controlConfigsByIdentifier;
    private readonly HashSet<string> _controlIdentifiers;
    private readonly Dictionary<string, bool> _controlStates;
    private readonly Dictionary<string, float> _previousControlValues;

    public DefaultMonitorPlatform(Config config, IPlatformAdapter adapter)
    {
        _adapter = adapter;
        _sensorConfigsByIdentifier = config.Sensors.ToDictionary(f => f.Identifier, f => f);
        _sensorIdentifiers = config.Sensors.Select(f => f.Identifier).ToHashSet();
        _controlConfigsByAlias = config.Controls.ToDictionary(f => f.Alias, f => f);
        _controlConfigsByIdentifier = config.Controls.ToDictionary(f => f.Identifier, f => f);
        _controlIdentifiers = config.Controls.Select(f => f.Identifier).ToHashSet();

        // Initialize previous control values to current values
        _previousControlValues = GetControlValues().Where(kvp => kvp.Value.HasValue).ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? 0f);
        _controlStates = _previousControlValues.ToDictionary(f => f.Key, f => f.Value != 0f);
    }

    public void Suspend()
    {
        _adapter.Suspend();
    }

    public void Resume()
    {
        _adapter.Resume();
        var currentValues = GetControlValues();
        _previousControlValues.Clear();
        _controlStates.Clear();
        foreach (var kvp in currentValues)
        {
            if (kvp.Value.HasValue)
            {
                _previousControlValues[kvp.Key] = kvp.Value.Value;
                _controlStates[kvp.Key] = kvp.Value != 0f;
            }
        }
        // Reapply control values and take control
        SetControls(_previousControlValues.ToDictionary(kvp => kvp.Key, kvp => kvp.Value), true);
    }

    public Dictionary<string, float?> GetSensorValues()
    {
        return _adapter.GetSensorValues(_sensorIdentifiers).ToDictionary(kvp => _sensorConfigsByIdentifier[kvp.Key].Alias, kvp => kvp.Value);
    }

    public Dictionary<string, float?> GetControlValues()
    {
        return _adapter.GetControlValues(_controlIdentifiers).ToDictionary(kvp => _controlConfigsByIdentifier[kvp.Key].Alias, kvp => kvp.Value);
    }

    public void ListAllSensors()
    {
        _adapter.ListAllSensors();
    }

    public Dictionary<string, bool> ReleaseControls()
    {
        return _adapter.ReleaseControls(_controlIdentifiers);
    }

    public Dictionary<string, bool> SetControls(Dictionary<string, float> controlValues, bool force = false)
    {
        // Alias to identifier mapping, and apply min stop / min start, step up / step down logic
        var adjustedControlValues = new Dictionary<string, float>();
        foreach (var kvp in controlValues)
        {
            string alias = kvp.Key;
            float controlValue = kvp.Value;

            if (!_controlConfigsByAlias.TryGetValue(alias, out var controlConfig))
            {
                Log.Error("Control {Alias} not configured", kvp.Key);
                continue;
            }

            float adjustedControlValue = controlValue;

            // Apply step up / step down logic
            float previousControlValue = _previousControlValues[alias];
            if (adjustedControlValue > previousControlValue)
            {
                float maxIncrease = controlConfig.StepUp;
                if (maxIncrease != 0f && adjustedControlValue > previousControlValue + maxIncrease)
                {
                    adjustedControlValue = previousControlValue + maxIncrease;
                    Log.Debug("Limited {Alias} speed increase to {AdjustedControlValue}% (StepUp: {MaxIncrease}%)",
                       alias, adjustedControlValue, maxIncrease);
                }
            }
            else if (adjustedControlValue < previousControlValue)
            {
                float maxDecrease = controlConfig.StepDown;
                if (maxDecrease != 0f && adjustedControlValue < previousControlValue - maxDecrease)
                {
                    adjustedControlValue = previousControlValue - maxDecrease;
                    Log.Debug("Limited {Alias} speed decrease to {AdjustedControlValue}% (StepDown: {MaxDecrease}%)",
                        alias, adjustedControlValue, maxDecrease);
                }
            }

            // Apply min stop / min start logic
            bool isCurrentlyRunning = _controlStates[alias];

            if (isCurrentlyRunning)
            {
                // If running, ensure speed is above min stop or set to 0
                if (controlValue > 0 && controlValue < controlConfig.MinStop)
                {
                    adjustedControlValue = controlConfig.MinStop;
                    Log.Debug("Adjusted {Alias} to min stop {MinStop}%", alias, controlConfig.MinStop);
                }
            }
            else
            {
                // If stopped, ensure speed is above min start to start the fan
                if (controlValue > 0 && controlValue < controlConfig.MinStart)
                {
                    adjustedControlValue = controlConfig.MinStart;
                    Log.Debug("Adjusted {Alias} to min start {MinStart}%", alias, controlConfig.MinStart);
                }
            }

            // Skip setting control if the value is the same as the previous one
            if (force || adjustedControlValue != _previousControlValues[alias])
            {
                // Update control state
                _controlStates[alias] = adjustedControlValue > 0;
                _previousControlValues[alias] = adjustedControlValue;

                adjustedControlValues[controlConfig.Identifier.ToString()] = adjustedControlValue;
            }
        }

        // Set controls using the adjusted values
       var res = _adapter.SetControls(adjustedControlValues);
       foreach (var kvp in res)
       {
        if (!kvp.Value)
        {
            // Log an error if the control could not be set
            Log.Error("Failed to set control {Control}", kvp.Key);
           }
       }
       return res;
    }
   
     private bool _disposed = false;

     ~DefaultMonitorPlatform()
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
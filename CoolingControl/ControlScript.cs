namespace CoolingControl;

using NLua;
using Serilog;

/// <summary>
/// Represents a control script that integrates with a Lua script to process sensor data
/// and compute control values.
/// </summary>
public class ControlScript : IDisposable
{
    private readonly Lua _lua;
    private readonly LuaFunction _calculate_controls;
    private readonly LuaFunction? _on_resume;
    private readonly Dictionary<string, ControlConfig> _controlConfigsByAlias;

    public ControlScript(Config config)
    {
        _lua = new Lua();
        _lua.LoadCLRPackage();
        _lua.RegisterFunction("log_debug", typeof(ControlScript).GetMethod(nameof(LuaLogDebug), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
        _lua.RegisterFunction("log_information", typeof(ControlScript).GetMethod(nameof(LuaLogInformation), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
        _lua.DoFile(config.ScriptPath);

        _calculate_controls = _lua["calculate_controls"] as LuaFunction ?? throw new InvalidOperationException("Lua function 'calculate_controls' is not defined in the Lua script");
        _on_resume = _lua["on_resume"] as LuaFunction;

        _controlConfigsByAlias = config.Controls.ToDictionary(f => f.Alias, f => f);
        _lua["control_config"] = _controlConfigsByAlias;
    }

    private static void LuaLogDebug(string message)
    {
        Log.Debug("[Lua] {Message}", message);
    }

    private static void LuaLogInformation(string message)
    {
        Log.Information("[Lua] {Message}", message);
    }

    public void OnResume()
    {
        if (_on_resume != null)
        {
            _on_resume.Call();
        }
    }

    public Dictionary<string, float> CalculateControls(Dictionary<string, float?> sensorValues)
    {
        Log.Debug("Sensor values: {SensorValues}", sensorValues);
        // Pass sensor data to Lua
        _lua["sensors"] = sensorValues;

        // Call the Lua function
        LuaTable result = _calculate_controls.Call(_lua["sensors"])[0] as LuaTable ?? throw new InvalidOperationException("Lua function 'calculate_controls' did not return a valid table");

        // Parse the result
        var controlValues = new Dictionary<string, float>();
        foreach (var key in result.Keys)
        {
            if (result[key] is LuaTable entry)
            {
                if (entry["alias"] is not string alias)
                {
                    Log.Error("Lua table entry {Key} is missing 'alias'", key);
                    continue;
                }

                if (entry["value"] != null)
                {
                    var value = Convert.ToSingle(entry["value"]);
                    controlValues.Add(alias, value);
                    // Log.Debug("Control value: {Alias} = {Value}", alias, value);
                    continue;
                }

                if (entry["rpm"] != null)
                {
                    // Convert RPM to control value
                    var rpm = Convert.ToSingle(entry["rpm"]);

                    if (!_controlConfigsByAlias.TryGetValue(alias, out var controlConfig))
                    {
                        Log.Error("Control {Alias} not configured", alias);
                        continue;
                    }

                    if (controlConfig.RPMCalibration.Count <= 1)
                    {
                        Log.Error("Control {Alias} has no valid RPM calibration data", alias);
                        continue;
                    }

                    var rpmCalibration = controlConfig.RPMCalibration;
                    float value = 0f;

                    if (rpm < rpmCalibration[0].Rpm)
                    {
                        value = rpmCalibration[0].Control;
                        Log.Debug("{Alias} {Rpm} RPM is below minimum calibration data, setting control to {Value}", alias, rpm, value);
                    }
                    else if (rpm > rpmCalibration[^1].Rpm)
                    {
                        value = rpmCalibration[^1].Control;
                        Log.Debug("{Alias} {Rpm} RPM is above maximum calibration data, setting control to {Value}", alias, rpm, value);
                    }
                    else
                    {
                        int i;
                        for (i = 0; i < rpmCalibration.Count - 1; i++)
                        {
                            var lower = rpmCalibration[i];
                            var upper = rpmCalibration[i + 1];

                            if (rpm >= lower.Rpm && rpm <= upper.Rpm)
                            {
                                var rpmDelta = upper.Rpm - lower.Rpm;

                                if (rpmDelta == 0)
                                {
                                    value = lower.Control;
                                    Log.Debug("{Alias} {Rpm} RPM is {LowerRpm}, setting control to {Value}", alias, rpm, lower.Rpm, value);
                                    break;
                                }

                                value = lower.Control + (upper.Control - lower.Control) * ((rpm - lower.Rpm) / rpmDelta);
                                Log.Debug("{Alias} {Rpm} RPM is between {LowerRpm} and {UpperRpm}, setting control to {Value}", alias, rpm, lower.Rpm, upper.Rpm, value);
                                break;
                            }
                        }

                        if (i == rpmCalibration.Count - 1)
                        {
                            Log.Error("Calibration data is not sorted for {Alias}", alias);
                            continue;
                        }
                    }

                    controlValues.Add(alias, value);
                    continue;
                }
            }
            else
            {
                Log.Error("Invalid Lua table entry for key: {Key}", key);
            }
        }

        Log.Debug("Control values: {ControlValues}", controlValues);
        return controlValues;
    }

    private bool _disposed = false;

    ~ControlScript()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _lua?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
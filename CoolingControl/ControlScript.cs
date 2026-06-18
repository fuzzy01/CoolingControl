namespace CoolingControl;

using NLua;
using Serilog;

/// <summary>
/// Represents a control script that integrates with a Lua script to process sensor data
/// and compute control values.
/// </summary>
public class ControlScript : IDisposable
{
    private readonly ConfigHelper _config;
    private readonly Lua _lua;
    private readonly LuaFunction _calculate_controls;
    private readonly LuaFunction? _on_start;
    private readonly LuaFunction? _on_stop;
    private readonly LuaFunction? _on_suspend;
    private readonly LuaFunction? _on_resume;

    public ControlScript(ConfigHelper config)
    {
        _config = config;
        _lua = new Lua();
        _lua.LoadCLRPackage();
        _lua.RegisterFunction("log_debug", typeof(ControlScript).GetMethod(nameof(LuaLogDebug), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
        _lua.RegisterFunction("log_information", typeof(ControlScript).GetMethod(nameof(LuaLogInformation), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
        _lua.RegisterFunction("log_error", typeof(ControlScript).GetMethod(nameof(LuaLogError), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
        try
        {
            _lua.DoFile(config.Config.ScriptPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load Lua script '{config.Config.ScriptPath}': {ex.Message}", ex);
        }

        _calculate_controls = _lua["calculate_controls"] as LuaFunction ?? throw new InvalidOperationException($"Lua script '{config.Config.ScriptPath}' must define a 'calculate_controls' function");
        _on_start = _lua["on_start"] as LuaFunction;
        _on_stop = _lua["on_stop"] as LuaFunction;
        _on_suspend = _lua["on_suspend"] as LuaFunction;
        _on_resume = _lua["on_resume"] as LuaFunction;

        _lua["control_config"] = _config.ControlConfigsByAlias;
        _lua["sensor_config"] = _config.SensorConfigsByAlias;

        if (_lua["initialize"] is LuaFunction initialize)
        {
            Log.Debug("Calling Lua initialize function");
            initialize.Call();
        }
    }

    private static void LuaLogDebug(string message)
    {
        Log.Debug("[Lua] {Message}", message);
    }

    private static void LuaLogInformation(string message)
    {
        Log.Information("[Lua] {Message}", message);
    }

    private static void LuaLogError(string message)
    {
        Log.Error("[Lua] {Message}", message);
    }

    public void OnSuspend()
    {
        if (_on_suspend != null)
        {
            Log.Debug("Calling Lua on_suspend function");
            _on_suspend.Call();
        }
    }

    public void OnResume()
    {
        if (_on_resume != null)
        {
            Log.Debug("Calling Lua on_resume function");
            _on_resume.Call();
        }
    }

    public void OnStart()
    {
        if (_on_start != null)
        {
            Log.Debug("Calling Lua on_start function");
            _on_start.Call();
        }
    }

    public void OnStop()
    {
        if (_on_stop != null)
        {
            Log.Debug("Calling Lua on_stop function");
            _on_stop.Call();
        }
    }

    public Dictionary<string, float> CalculateControls(Dictionary<string, float?> sensorValues)
    {
        Log.Debug("Sensor values: {SensorValues}", sensorValues);
        // Pass sensor data to Lua
        _lua["sensors"] = sensorValues;

        // Call the Lua function
        var callResult = _calculate_controls.Call(_lua["sensors"]);
        LuaTable result = (callResult.Length > 0 ? callResult[0] as LuaTable : null) ?? throw new InvalidOperationException("Lua function 'calculate_controls' did not return a valid table");

        // Parse the result
        var controlValues = new Dictionary<string, float>();
        foreach (var key in result.Keys)
        {
            if (result[key] is LuaTable entry)
            {
                if (entry["alias"] is not string alias || string.IsNullOrWhiteSpace(alias))
                {
                    Log.Error("Lua table entry {Key} is missing 'alias'", key);
                    continue;
                }

                if (!_config.ControlConfigsByAlias.ContainsKey(alias))
                {
                    Log.Error("Lua returned unknown control alias '{Alias}'", alias);
                    continue;
                }

                float value;
                try
                {
                    if (entry["value"] != null)
                    {
                        value = Convert.ToSingle(entry["value"]);
                    }
                    else if (entry["rpm"] != null)
                    {
                        var rpm = Convert.ToSingle(entry["rpm"]);
                        var converted = _config.ConvertRPMToPercent(alias, rpm);
                        if (converted == null)
                            continue;
                        value = converted.Value;
                    }
                    else
                    {
                        Log.Error("Lua table entry {Key} for alias '{Alias}' has neither 'value' nor 'rpm'", key, alias);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Invalid numeric value in Lua entry {Key} for alias '{Alias}'", key, alias);
                    continue;
                }

                if (controlValues.ContainsKey(alias))
                {
                    Log.Warning("Duplicate Lua control alias '{Alias}' detected; overwriting previous value", alias);
                }
                controlValues[alias] = value;
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
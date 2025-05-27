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
        _lua.DoFile(config.Config.ScriptPath);

        _calculate_controls = _lua["calculate_controls"] as LuaFunction ?? throw new InvalidOperationException("Lua function 'calculate_controls' is not defined in the Lua script");
        _on_suspend = _lua["on_suspend"] as LuaFunction;
        _on_resume = _lua["on_resume"] as LuaFunction;

        _lua["control_config"] = _config.ControlConfigsByAlias;

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
                    continue;
                }

                if (entry["rpm"] != null)
                {
                    // Convert RPM to control value
                    var rpm = Convert.ToSingle(entry["rpm"]);
                    var value = _config.ConvertRPMToPercent(alias, rpm);
                    if (value == null)
                        continue;

                    controlValues.Add(alias, (float)value);
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
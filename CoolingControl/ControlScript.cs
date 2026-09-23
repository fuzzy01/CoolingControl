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
    private readonly LuaFunction? _on_power_source_changed;
    private readonly LuaFunction? _on_profile_changed;
    private string _activeProfile = "";

    public ControlScript(ConfigHelper config)
    {
        _config = config;
        _lua = new Lua();
        _lua.LoadCLRPackage();
        _lua.RegisterFunction("log_debug", typeof(ControlScript).GetMethod(nameof(LuaLogDebug), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
        _lua.RegisterFunction("log_information", typeof(ControlScript).GetMethod(nameof(LuaLogInformation), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
        _lua.RegisterFunction("log_error", typeof(ControlScript).GetMethod(nameof(LuaLogError), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
        _activeProfile = config.Config.ActiveProfile ?? "";
        _lua["active_profile"] = _activeProfile;
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
        _on_power_source_changed = _lua["on_power_source_changed"] as LuaFunction;
        _on_profile_changed = _lua["on_profile_changed"] as LuaFunction;
        _activeProfile = _config.Config.ActiveProfile ?? "";
        _lua["active_profile"] = _activeProfile;

        _lua["control_config"] = BuildLuaControlConfigTable();
        _lua["sensor_config"] = BuildLuaSensorConfigTable();

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

    private LuaTable CreateLuaTable()
    {
        var result = _lua.DoString("return {}");
        return (result.Length > 0 ? result[0] as LuaTable : null) ?? throw new InvalidOperationException("Failed to create Lua table");
    }

    private LuaTable BuildLuaSensorConfigTable()
    {
        var sensorConfigTable = CreateLuaTable();
        foreach (var (alias, sensor) in _config.SensorConfigsByAlias)
        {
            var sensorTable = CreateLuaTable();
            sensorTable["alias"] = sensor.Alias;
            sensorTable["identifier"] = sensor.Identifier;
            sensorTable["platform"] = sensor.Platform;
            sensorConfigTable[alias] = sensorTable;
        }

        return sensorConfigTable;
    }

    private LuaTable BuildLuaControlConfigTable()
    {
        var controlConfigTable = CreateLuaTable();
        foreach (var (alias, control) in _config.ControlConfigsByAlias)
        {
            var controlTable = CreateLuaTable();
            controlTable["alias"] = control.Alias;
            controlTable["identifier"] = control.Identifier;
            controlTable["platform"] = control.Platform;
            controlTable["step_up"] = control.StepUp;
            controlTable["step_down"] = control.StepDown;
            controlTable["min_start"] = control.MinStart;
            controlTable["min_stop"] = control.MinStop;
            controlTable["zero_rpm"] = control.ZeroRPM;
            controlTable["rpm_sensor"] = control.RPMSensor;
            controlTable["thermal_min_control"] = control.ThermalMinControl;

            var rpmCalibration = CreateLuaTable();
            for (int i = 0; i < control.RPMCalibration.Count; i++)
            {
                var point = control.RPMCalibration[i];
                var pointTable = CreateLuaTable();
                pointTable["control"] = point.Control;
                pointTable["rpm"] = point.Rpm;
                rpmCalibration[i + 1] = pointTable;
            }

            controlTable["rpm_calibration"] = rpmCalibration;
            controlConfigTable[alias] = controlTable;
        }

        return controlConfigTable;
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

    public void OnPowerSourceChanged(bool isAcPowered)
    {
        if (_on_power_source_changed != null)
        {
            Log.Debug("Calling Lua on_power_source_changed function with isAcPowered={IsAcPowered}", isAcPowered);
            _on_power_source_changed.Call(isAcPowered);
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

    public void SetActiveProfile(string name)
    {
        name ??= "";
        if (_activeProfile == name)
            return;

        _activeProfile = name;
        _lua["active_profile"] = name;
        if (_on_profile_changed != null)
        {
            Log.Debug("Calling Lua on_profile_changed function with profile={Profile}", name);
            _on_profile_changed.Call(name);
        }
    }

    public Dictionary<string, float> CalculateControls(Dictionary<string, float?> sensorValues)
    {
        Log.Debug("Sensor values: {SensorValues}", sensorValues);
        _lua["active_profile"] = _activeProfile;
        // Pass sensor data to Lua
        _lua["sensors"] = sensorValues;

        // Call the Lua function
        var callResult = _calculate_controls.Call(_lua["sensors"]);
        LuaTable result = (callResult.Length > 0 ? callResult[0] as LuaTable : null) ?? throw new InvalidOperationException("Lua function 'calculate_controls' did not return a valid table");

        var requests = ParseControlRequests(result);
        var controlValues = ApplyBeatDetune(requests);

        Log.Debug("Control values: {ControlValues}", controlValues);
        return controlValues;
    }

    private readonly record struct ScriptRequest(string Alias, bool IsRpm, float Amount);

    private Dictionary<string, ScriptRequest> ParseControlRequests(LuaTable result)
    {
        var requests = new Dictionary<string, ScriptRequest>();
        foreach (var key in result.Keys)
        {
            if (result[key] is not LuaTable entry)
            {
                Log.Error("Invalid Lua table entry for key: {Key}", key);
                continue;
            }

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

            bool isRpm;
            float amount;
            try
            {
                if (entry["value"] != null)
                {
                    isRpm = false;
                    amount = Convert.ToSingle(entry["value"]);
                }
                else if (entry["rpm"] != null)
                {
                    isRpm = true;
                    amount = Convert.ToSingle(entry["rpm"]);
                    if (!_config.TryGetEffectiveRpm(alias, amount, out _, out _))
                        continue;
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

            if (requests.ContainsKey(alias))
                Log.Warning("Duplicate Lua control alias '{Alias}' detected; overwriting previous value", alias);

            requests[alias] = new ScriptRequest(alias, isRpm, amount);
        }

        return requests;
    }

    private Dictionary<string, float> ApplyBeatDetune(Dictionary<string, ScriptRequest> requests)
    {
        var controlValues = new Dictionary<string, float>();
        bool anyDetune = requests.Values.Any(request => _config.ControlConfigsByAlias[request.Alias].BeatDetune);
        if (!anyDetune)
        {
            foreach (var request in requests.Values)
                AddUnchanged(controlValues, request);
            return controlValues;
        }

        var spreadFans = new List<BeatFan>();
        foreach (var request in requests.Values)
        {
            if (!TryGetSpreadFan(request, out var fan))
            {
                AddUnchanged(controlValues, request);
                continue;
            }

            spreadFans.Add(fan);
        }

        foreach (var placement in BeatDetuner.Adjust(
            spreadFans,
            _config.Config.BeatDetuneMinSeparationRpm,
            _config.Config.BeatDetuneMaxNudgeRpm))
        {
            LogBeatDetune(placement);
            var percent = _config.ConvertRPMToPercent(placement.Alias, placement.ToRpm);
            if (percent == null)
                continue;
            controlValues[placement.Alias] = percent.Value;
        }

        return controlValues;
    }

    private bool TryGetSpreadFan(ScriptRequest request, out BeatFan fan)
    {
        fan = default;
        if (!_config.ControlConfigsByAlias[request.Alias].BeatDetune || request.Amount <= 0f)
            return false;

        float requestedRpm;
        if (request.IsRpm)
        {
            requestedRpm = request.Amount;
        }
        else
        {
            var rpm = _config.ConvertPercentToRpm(request.Alias, request.Amount);
            if (rpm == null)
                return false;
            requestedRpm = rpm.Value;
        }

        if (!_config.TryGetEffectiveRpm(request.Alias, requestedRpm, out var effectiveRpm, out var maxRpm))
            return false;
        if (effectiveRpm <= 0f)
            return false;

        fan = new BeatFan(request.Alias, effectiveRpm, maxRpm);
        return true;
    }

    private void AddUnchanged(Dictionary<string, float> controlValues, ScriptRequest request)
    {
        if (!request.IsRpm)
        {
            controlValues[request.Alias] = request.Amount;
            return;
        }

        var percent = _config.ConvertRPMToPercent(request.Alias, request.Amount);
        if (percent == null)
            return;
        controlValues[request.Alias] = percent.Value;
    }

    private static void LogBeatDetune(BeatPlacement placement)
    {
        if (placement.ToRpm == placement.FromRpm)
            return;

        if (placement.NudgeCapped && placement.CalibrationCapped)
        {
            Log.Debug(
                "Beat detune {Alias} {From} RPM -> {To} RPM (stopped by nudge cap and calibration max)",
                placement.Alias, placement.FromRpm, placement.ToRpm);
        }
        else if (placement.NudgeCapped)
        {
            Log.Debug(
                "Beat detune {Alias} {From} RPM -> {To} RPM (stopped by nudge cap)",
                placement.Alias, placement.FromRpm, placement.ToRpm);
        }
        else if (placement.CalibrationCapped)
        {
            Log.Debug(
                "Beat detune {Alias} {From} RPM -> {To} RPM (stopped by calibration max)",
                placement.Alias, placement.FromRpm, placement.ToRpm);
        }
        else
        {
            Log.Debug("Beat detune {Alias} {From} RPM -> {To} RPM", placement.Alias, placement.FromRpm, placement.ToRpm);
        }
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
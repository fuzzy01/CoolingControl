namespace CoolingControl;

using Microsoft.Extensions.Hosting;
using Serilog;
using CoolingControl.Platform.LHM;
using CoolingControl.Platform;
using System.Text.Json;

public class ControlCalibration : BackgroundService
{
    private readonly Config _config;
    private readonly IRPMCalibrator _calibrator;
    private readonly string _control_alias;
    private readonly Dictionary<string, ControlConfig> _controlConfigsByAlias;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;

    public ControlCalibration(Config config, string control_alias, IHostApplicationLifetime hostApplicationLifetime)
    {
        _config = config;
        _calibrator = new DefaultRPMCalibrator(_config, new LHMAdapter(_config));
        _control_alias = control_alias;
        _controlConfigsByAlias = config.Controls.ToDictionary(f => f.Alias, f => f);
        _hostApplicationLifetime = hostApplicationLifetime;
    }

    protected override Task ExecuteAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => Calibrate(cancellationToken), cancellationToken);
    }

    protected void Calibrate(CancellationToken cancellationToken)
    {
        var controls = _control_alias != null ? [_control_alias] : _config.Controls.Select(f => f.Identifier).ToHashSet();
        
        try
        {
            foreach (var control_alias in controls)
            {
                Log.Information("Calibrating control: {Alias}", control_alias);

                var minStart = FindMinStart(control_alias, cancellationToken);
                if (minStart == null)
                {
                    Log.Error("Failed to calibrate MinStart for {Alias}. Exiting ...", control_alias);
                    return;
                }
                _controlConfigsByAlias[control_alias].MinStart = (float)minStart;

                var minStop = FindMinStop(control_alias, cancellationToken);
                if (minStop == null)
                {
                    Log.Error("Failed to calibrate MinStop for {Alias}. Exiting ...", control_alias);
                    return;
                }
                _controlConfigsByAlias[control_alias].MinStop = (float)minStop;

                var rpmCalibration = CalibrateRPMCurve(control_alias, cancellationToken);
                if (rpmCalibration == null)
                {
                    Log.Error("Failed to calibrate RPM curve for {Alias}. Exiting ...", control_alias);
                    return;
                }
                _controlConfigsByAlias[control_alias].RPMCalibration = rpmCalibration;
            }

            // Save the config
            var jsonString = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText("config/config.json", jsonString);
            Log.Information("Config saved to config/config.json");

        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error in control loop");
        }
        finally
        {
            _hostApplicationLifetime.StopApplication();

            foreach (var control_alias in controls)
            {
                _calibrator.ReleaseControl(control_alias);
            }

            _calibrator.Dispose();
        }

    }

    protected bool StopControl(string control_alias, CancellationToken cancellationToken)
    {
        // Stop the control first
        if (!_calibrator.SetControl(control_alias, 0f))
            return false;

        // Wait for the control to stop
        for (int i = 0; i <= 10; i++)
        {
            Task.Delay(1000, cancellationToken).Wait(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var rpm = _calibrator.GetRPMSensorValue(control_alias);
            if (!rpm.HasValue)
                return false;
            if (rpm == 0)
                break;
        }

        return true;
    }

    protected float? FindMinStart(string control_alias, CancellationToken cancellationToken)
    {
        // Stop the control first
        if (!StopControl(control_alias, cancellationToken))
            return null;

        // Find the minimum start value
        for (int control_value = 0; control_value <= 100; control_value++)
        {
            if (!_calibrator.SetControl(control_alias, control_value))
                return null;

            Task.Delay(2000, cancellationToken).Wait(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var rpm = _calibrator.GetRPMSensorValue(control_alias);
            if (!rpm.HasValue)
                return null;
            if (rpm > 0f)
            {
                // Check for stability
                if (!StopControl(control_alias, cancellationToken))
                    return null;

                if (!_calibrator.SetControl(control_alias, control_value))
                    return null;
                Task.Delay(6000, cancellationToken).Wait(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                var stableRpm = _calibrator.GetRPMSensorValue(control_alias);
                if (!stableRpm.HasValue)
                    return null;

                if (stableRpm > 0f)
                {
                    Log.Information("Control {Alias} started at {Value}% ({RPM} RPM)", control_alias, control_value, stableRpm);
                    return control_value;
                }

                Log.Information("Control {Alias} started at {Value}% ({RPM} RPM) but was not stable", control_alias, control_value, rpm);
            }
        }

        Log.Error("Control {Alias} failed to start", control_alias);
        return null;
    }

    protected float? FindMinStop(string control_alias, CancellationToken cancellationToken)
    {
        // Assume control is spinning
        var current = _calibrator.GetControlValue(control_alias);
        if (current == null)
            return null;

        for (int control_value = (int)Math.Round((float)current); control_value >= 0; control_value--)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_calibrator.SetControl(control_alias, control_value))
                return null;

            Task.Delay(6000, cancellationToken).Wait(cancellationToken);

            var rpm = _calibrator.GetRPMSensorValue(control_alias);
            if (!rpm.HasValue)
                return null;
            if (rpm == 0)
            {
                Log.Information("Control {Alias} stopped at {Value}% ({RPM} RPM)", control_alias, control_value, rpm);
                return control_value + 1f;
            }
        }

        Log.Information("Control {Alias} did not to stop", control_alias);
        return 0;
    }

    protected List<RPMCalibrationData>? CalibrateRPMCurve(string control_alias, CancellationToken cancellationToken)
    {
        List<RPMCalibrationData> res = [];

        for (int control_value = 100; control_value >= 0; control_value -= 10)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_calibrator.SetControl(control_alias, control_value))
                return null;

            Task.Delay(control_value == 100 ? 10000 : 6000, cancellationToken).Wait(cancellationToken);

            // Make multiple measurments
            float avg_rpm = 0f;
            for (int i = 0; i < 10; i++)
            {
                var rpm = _calibrator.GetRPMSensorValue(control_alias);
                if (!rpm.HasValue)
                    return null;
                if (rpm == 0f)
                {
                    avg_rpm = 0f;
                    break;
                }
                avg_rpm += (float)rpm;

                Task.Delay(500, cancellationToken).Wait(cancellationToken);
            }
            avg_rpm /= 10f;

            var r = (float)Math.Round(avg_rpm);
            Log.Information("Control {Alias} {Value}=>{Rpm}", control_alias, control_value, r);
            res.Insert(0, new RPMCalibrationData { Control = control_value, Rpm = r });
        }

        return res;
    }

}
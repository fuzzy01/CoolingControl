namespace CoolingControl;

using Microsoft.Extensions.Hosting;
using Serilog;
using CoolingControl.Platform;

public record TempCalibrationParams(string ControlAlias, string SensorAlias, float MaxTemp);

public class TempCalibration : BackgroundService
{
    private readonly ConfigHelper _config;
    private readonly IRPMCalibrator _calibrator;
    private readonly TempCalibrationParams _params;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;

    private const int StepSize = 5;
    private const int WarmUpMs = 180_000;
    private const int StabilizeMs = 120_000;
    private const int TempSampleCount = 10;
    private const int TempSampleIntervalMs = 500;

    public TempCalibration(ConfigHelper config, TempCalibrationParams parameters, IHostApplicationLifetime hostApplicationLifetime)
    {
        _config = config;
        _calibrator = new DefaultRPMCalibrator(config, PlatformAdapterFactory.Create(config));
        _params = parameters;
        _hostApplicationLifetime = hostApplicationLifetime;
    }

    protected override Task ExecuteAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => Calibrate(cancellationToken), cancellationToken);
    }

    private void Calibrate(CancellationToken cancellationToken)
    {
        try
        {
            Log.Information("Temperature calibration: find minimum '{Control}' % to keep '{Sensor}' below {MaxTemp}°C",
                _params.ControlAlias, _params.SensorAlias, _params.MaxTemp);
            Log.Information("Apply maximum CPU load now, then press Enter to start...");
            Console.ReadLine();

            Log.Information("Setting '{Control}' to 100%, waiting {Seconds}s for temperature to stabilize...",
                _params.ControlAlias, WarmUpMs / 1000);
            if (!_calibrator.SetControl(_params.ControlAlias, 100f))
            {
                Log.Error("Failed to set control '{Alias}' to 100%", _params.ControlAlias);
                return;
            }

            Task.Delay(WarmUpMs, cancellationToken).GetAwaiter().GetResult();

            var baseTemp = ReadAverageTemp(cancellationToken);
            if (!baseTemp.HasValue)
                return;

            Log.Information("Temperature at 100% fan: {Temp:F1}°C", baseTemp);
            if (baseTemp > _params.MaxTemp)
                Log.Warning("Temperature {Temp:F1}°C already exceeds {MaxTemp}°C at 100% — consider better cooling", baseTemp, _params.MaxTemp);

            if (!_config.ControlConfigsByAlias.TryGetValue(_params.ControlAlias, out var ctrl))
            {
                Log.Error("Control alias '{Alias}' not found in config", _params.ControlAlias);
                return;
            }
            float minStop = ctrl.MinStop;

            float minSafe = 100f;
            bool foundLimit = false;

            for (int step = 100 - StepSize; step >= minStop; step -= StepSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Log.Information("Setting '{Control}' to {Value}%, waiting {Seconds}s...",
                    _params.ControlAlias, step, StabilizeMs / 1000);

                if (!_calibrator.SetControl(_params.ControlAlias, step))
                {
                    Log.Error("Failed to set control '{Alias}' to {Value}%", _params.ControlAlias, step);
                    return;
                }

                Task.Delay(StabilizeMs, cancellationToken).GetAwaiter().GetResult();

                var temp = ReadAverageTemp(cancellationToken);
                if (!temp.HasValue)
                    return;

                Log.Information("  '{Control}' {Value}% => {Temp:F1}°C", _params.ControlAlias, step, temp);

                if (temp > _params.MaxTemp)
                {
                    minSafe = step + StepSize;
                    foundLimit = true;
                    Log.Information("Temperature exceeded {MaxTemp}°C at {Value}% — limit found", _params.MaxTemp, step);
                    break;
                }

                minSafe = step;
            }

            if (!foundLimit)
                Log.Information("Temperature stayed below {MaxTemp}°C at all fan speeds (lowest tested: {MinSafe}%)", _params.MaxTemp, minSafe);

            Log.Information("Result: minimum safe '{Control}' = {MinSafe}% to keep '{Sensor}' below {MaxTemp}°C",
                _params.ControlAlias, minSafe, _params.SensorAlias, _params.MaxTemp);

            Log.Information("Save {MinSafe}% as ThermalMinControl for '{Control}' in config.json? (y/n)", minSafe, _params.ControlAlias);
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (answer == "y" || answer == "yes")
            {
                ctrl.ThermalMinControl = minSafe;
                _config.SaveConfig();
                Log.Information("Saved ThermalMinControl = {MinSafe}% for '{Control}'", minSafe, _params.ControlAlias);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on Ctrl+C
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error during temperature calibration");
        }
        finally
        {
            _calibrator.ReleaseControl(_params.ControlAlias);
            _calibrator.Dispose();
            _hostApplicationLifetime.StopApplication();
        }
    }

    private float? ReadAverageTemp(CancellationToken cancellationToken)
    {
        float sum = 0f;
        for (int i = 0; i < TempSampleCount; i++)
        {
            var temp = _calibrator.GetSensorValue(_params.SensorAlias);
            if (!temp.HasValue)
            {
                Log.Error("Failed to read sensor '{Sensor}'", _params.SensorAlias);
                return null;
            }
            sum += temp.Value;
            if (i < TempSampleCount - 1)
                Task.Delay(TempSampleIntervalMs, cancellationToken).GetAwaiter().GetResult();
        }
        return sum / TempSampleCount;
    }
}

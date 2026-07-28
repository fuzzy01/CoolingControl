namespace CoolingControl;

using Microsoft.Extensions.Hosting;
using Serilog;
using CoolingControl.Platform;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;

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
        var cts = new CancellationTokenSource();
        var tasks = new List<Task>();

        try
        {
            Log.Information("Temperature calibration: find minimum '{Control}' % to keep '{Sensor}' below {MaxTemp}°C",
                _params.ControlAlias, _params.SensorAlias, _params.MaxTemp);
            Log.Information("Applying maximum CPU load now ...");

            int stressThreadCount = Environment.ProcessorCount;
            for (int threadCount = 0; threadCount < stressThreadCount; threadCount++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Start CPU-intensive task
                tasks.Add(Task.Run(() => StressCpuLow(cts.Token), cancellationToken));
            }

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
            try
            {
                // Cancel all tasks
                cts.Cancel();
                Task.WaitAll([.. tasks], cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
            }

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


       private unsafe static void StressCpuLow(CancellationToken cancellationToken)
    {
        if (!Avx2.IsSupported)
        {
            throw new NotSupportedException("AVX2 not supported on this CPU.");
        }

        // Small arrays for integer operations
        const int vectorSize = 64; // 8 integers per vector, 8 vectors
        int[] a = new int[vectorSize];
        int[] b = new int[vectorSize];
        int[] result = new int[vectorSize];

        // Initialize with simple values
        for (int i = 0; i < vectorSize; i++)
        {
            a[i] = i;
            b[i] = i + 1;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            fixed (int* pA = a, pB = b, pR = result)
            {
                for (int i = 0; i < vectorSize; i += 8) // Process 8 integers per vector
                {
                    if (i + 7 >= vectorSize) break;

                    // Load vectors
                    Vector256<int> va = Avx2.LoadVector256(pA + i);
                    Vector256<int> vb = Avx2.LoadVector256(pB + i);

                    // Simple integer addition
                    Vector256<int> vResult = Avx2.Add(va, vb);

                    // Store result
                    Avx2.Store(pR + i, vResult);
                }
            }

            // Prevent compiler optimization
            if (result[0] == 0) Console.WriteLine("Never happens");
        }
    }
}

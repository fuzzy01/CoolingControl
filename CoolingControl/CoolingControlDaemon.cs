namespace CoolingControl;

using Microsoft.Extensions.Hosting;
using Serilog;
using Microsoft.Win32;
using System.Collections.Concurrent;
using CoolingControl.Platform;

/// <summary>
/// A daemon that monitors hardware sensors and applies control settings
/// based on a user-defined script. The daemon operates in a continuous loop,
/// periodically updating the hardware controls.
/// </summary>
public class CoolingControlDaemon : BackgroundService
{
    private readonly ConfigHelper _config;
    private readonly IMonitoringPlatform _monitor;
    private readonly ControlScript _script;
    private readonly CSVLogger _CSVLogger;
    private readonly IStatusSnapshot _statusSnapshot;
    private readonly int _intervalMs;
    // private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly BlockingCollection<PowerEvent> _messageQueue;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;

    public CoolingControlDaemon(ConfigHelper config, IHostApplicationLifetime hostApplicationLifetime, IStatusSnapshot statusSnapshot)
    {
        _config = config;
        _monitor = new DefaultMonitorPlatform(_config, PlatformAdapterFactory.Create(_config));
        _script = new ControlScript(_config);
        _CSVLogger = new CSVLogger(_config);
        _statusSnapshot = statusSnapshot;
        _intervalMs = _config.Config.UpdateIntervalMs;
        _messageQueue = new BlockingCollection<PowerEvent>();
        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        _hostApplicationLifetime = hostApplicationLifetime;
    }

    private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                _messageQueue.Add(new PowerEvent(PowerModes.Suspend));
                Log.Debug("PowerEvent: System is suspending");
                break;
            case PowerModes.Resume:
                _messageQueue.Add(new PowerEvent(PowerModes.Resume));
                Log.Debug("PowerEvent: System is resuming");
                break;
            case PowerModes.StatusChange:
                var isAcPowered = PowerSourceStatus.GetIsAcPowered();
                if (isAcPowered.HasValue)
                {
                    _messageQueue.Add(new PowerEvent(PowerModes.StatusChange, isAcPowered.Value));
                    Log.Information("PowerEvent: Power source changed to {PowerSource}", isAcPowered.Value ? "AC" : "battery");
                }
                else
                {
                    Log.Debug("PowerEvent: Power status changed, but its source is unavailable");
                }
                break;
        }
    }

    protected override Task ExecuteAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => ControlLoop(cancellationToken), cancellationToken);
    }

    public override void Dispose()
    {
        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        base.Dispose();
    }

    private void ControlLoop(CancellationToken cancellationToken)
    {
        try
        {
            _script.OnStart();
            bool isSuspended = false;
            var recentErrors = new Queue<DateTime>();
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!isSuspended)
                    {
                        // Get sensor data
                        var sensorData = _monitor.GetSensorValues();
    
                        // Execute script to get control settings
                        var settings = _script.CalculateControls(sensorData);

                        if (_config.Config.EnableCSVLogging)
                        {
                            // Log sensor data to CSV
                            _CSVLogger.LogData(sensorData, settings);
                        }

                        // Apply control settings
                        var res = _monitor.SetControls(settings);

                        if (_config.Config.StatusServerEnabled)
                        {
                            // Update status snapshot for HTTP server
                            var controlRpmData = _monitor.GetControlRPMValues();
                            _statusSnapshot.Update(sensorData, settings, controlRpmData, DateTime.UtcNow);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in control loop");
                    var now = DateTime.UtcNow;
                    recentErrors.Enqueue(now);
                    while (recentErrors.Count > 0 && (now - recentErrors.Peek()).TotalSeconds > 60)
                        recentErrors.Dequeue();
                    if (recentErrors.Count >= _config.Config.MaxControlLoopErrors)
                    {
                        Log.Error("Too many errors in control loop, stopping service");
                        throw new InvalidOperationException("Too many errors in control loop, stopping service");
                        // break;
                    }
                }

                if (_messageQueue.TryTake(out var powerEvent, _intervalMs, cancellationToken))
                {
                    switch (powerEvent.Mode)
                    {
                        case PowerModes.Suspend:
                            _script.OnSuspend();
                            _monitor.Suspend();
                            isSuspended = true;
                            break;
                        case PowerModes.Resume:
                            _monitor.Resume();
                            _script.OnResume();
                            isSuspended = false;
                            break;
                        case PowerModes.StatusChange:
                            _script.OnPowerSourceChanged(powerEvent.IsAcPowered!.Value);
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error in control loop");
            Environment.ExitCode = 1;
        }
        finally
        {
            _script.OnStop();

            // Set controls to default values on exit
            _monitor.ReleaseControls();

            _hostApplicationLifetime.StopApplication();

            _monitor.Dispose();
            _script.Dispose();
        }

        Log.Information("CoolingControl service stopped");
    }

    private readonly record struct PowerEvent(PowerModes Mode, bool? IsAcPowered = null);
}
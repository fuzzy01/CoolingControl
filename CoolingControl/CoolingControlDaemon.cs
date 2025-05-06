namespace CoolingControl;

using Microsoft.Extensions.Hosting;
using Serilog;
using Microsoft.Win32;
using System.Collections.Concurrent;
using CoolingControl.Platform.LHM;
using CoolingControl.Platform;

/// <summary>
/// A daemon that monitors hardware sensors and applies control settings
/// based on a user-defined script. The daemon operates in a continuous loop,
/// periodically updating the hardware controls.
/// </summary>
public class CoolingControlDaemon : BackgroundService
{
    private readonly Config _config;
    private readonly IMonitoringPlatform _monitor;
    private readonly ControlScript _script;
    private readonly int _intervalMs;
    // private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly BlockingCollection<PowerModes> _messageQueue;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;

    public CoolingControlDaemon(Config config, IHostApplicationLifetime hostApplicationLifetime)
    {
        _config = config;
        _monitor = new DefaultMonitorPlatform(_config, new LHMAdapter(_config));
        _script = new ControlScript(_config);
        _intervalMs = _config.UpdateIntervalMs;
        _messageQueue = new BlockingCollection<PowerModes>();
        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        _hostApplicationLifetime = hostApplicationLifetime;
    }

    private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                _messageQueue.Add(PowerModes.Suspend);
                Log.Debug("PowerEvent: System is suspending");
                break;
            case PowerModes.Resume:
                _messageQueue.Add(PowerModes.Resume);
                Log.Debug("PowerEvent: System is resuming");
                break;
            case PowerModes.StatusChange:
                Log.Debug("PowerEvent: Power status changed");
                break;
        }
    }

    protected override Task ExecuteAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => ControlLoop(cancellationToken), cancellationToken);
    }

    private void ControlLoop(CancellationToken cancellationToken)
    {
        try
        {
            bool isSuspended = false;
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

                        // Apply control settings
                        var res = _monitor.SetControls(settings);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in control loop");
                }

                if (_messageQueue.TryTake(out var powerMode, _intervalMs, cancellationToken))
                {
                    switch (powerMode)
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
        }
        finally
        {
            _hostApplicationLifetime.StopApplication();

            // Set controls to default values on exit
            _monitor.ReleaseControls();

            _monitor.Dispose();
            _script.Dispose();
        }

        Log.Information("CoolingControl service stopped");
    }
}
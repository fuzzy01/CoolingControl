namespace CoolingControl;

public readonly record struct ConfigReloadStatus(bool Running, string Message);

/// <summary>
/// One pending config reload. The config page records it. The control loop takes it.
/// </summary>
public sealed class ConfigReload
{
    private readonly object _gate = new();
    private bool _pending;
    private bool _running;
    private string _message = "";

    public bool TryBegin(bool calibrationBusy, out int statusCode, out string message)
    {
        lock (_gate)
        {
            if (calibrationBusy)
            {
                statusCode = 409;
                message = "Calibration is already running.";
                return false;
            }

            if (_pending || _running)
            {
                statusCode = 409;
                message = "Config reload is already running.";
                return false;
            }

            _pending = true;
            _message = "Reloading config. Control pauses while the saved config and script load.";
            statusCode = 202;
            message = _message;
            return true;
        }
    }

    public bool TryTake()
    {
        lock (_gate)
        {
            if (!_pending)
                return false;

            _pending = false;
            _running = true;
            return true;
        }
    }

    public void Finish(bool saved)
    {
        lock (_gate)
        {
            _pending = false;
            _running = false;
            _message = saved ? "Config reloaded." : "Config reload failed.";
        }
    }

    public ConfigReloadStatus Status()
    {
        lock (_gate)
            return new ConfigReloadStatus(_pending || _running, _message);
    }
}

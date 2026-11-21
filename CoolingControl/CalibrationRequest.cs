namespace CoolingControl;

public readonly record struct CalibrationStatus(bool Running, string Alias, string Message);

/// <summary>
/// One pending calibration alias. The config page records it. The control loop takes it.
/// </summary>
public sealed class CalibrationRequest
{
    private readonly object _gate = new();
    private string? _pending;
    private bool _running;
    private string _alias = "";
    private string _message = "";

    public bool TryBegin(IReadOnlyList<ControlConfig> controls, string? alias, out int statusCode, out string message)
    {
        lock (_gate)
        {
            if (_pending != null || _running)
            {
                statusCode = 409;
                message = "Calibration is already running.";
                return false;
            }

            if (!TryValidate(controls, alias, out message))
            {
                statusCode = 400;
                return false;
            }

            _pending = alias;
            _alias = alias!;
            _message = alias == "all"
                ? "Calibrating all controls. Fans will run through the sweep. Control pauses until it finishes."
                : $"Calibrating {alias}. Fans will run through the sweep. Control pauses until it finishes.";
            statusCode = 202;
            message = _message;
            return true;
        }
    }

    public bool TryTake(out string alias)
    {
        lock (_gate)
        {
            if (_pending == null)
            {
                alias = "";
                return false;
            }

            alias = _pending;
            _pending = null;
            _running = true;
            _alias = alias;
            return true;
        }
    }

    public void Finish(bool saved, string alias)
    {
        lock (_gate)
        {
            _pending = null;
            _running = false;
            _alias = alias;
            _message = saved ? "Calibration saved." : "Calibration failed.";
        }
    }

    public CalibrationStatus Status()
    {
        lock (_gate)
            return new CalibrationStatus(_pending != null || _running, _alias, _message);
    }

    private static bool TryValidate(IReadOnlyList<ControlConfig> controls, string? alias, out string message)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            message = "Missing alias.";
            return false;
        }

        if (alias == "all")
        {
            if (controls.Any(control => string.IsNullOrEmpty(control.RPMSensor)))
            {
                message = "Calibration needs an RPM sensor on every control.";
                return false;
            }

            message = "";
            return true;
        }

        var control = controls.FirstOrDefault(item => item.Alias == alias);
        if (control == null)
        {
            message = $"Unknown control '{alias}'.";
            return false;
        }

        if (string.IsNullOrEmpty(control.RPMSensor))
        {
            message = $"Control '{alias}' has no RPM sensor.";
            return false;
        }

        message = "";
        return true;
    }
}

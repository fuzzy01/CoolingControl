namespace CoolingControl.Platform;

/// <summary>
/// Defines an interface for monitoring and controlling platform-specific sensors and controls.
/// </summary>
public interface IMonitoringPlatform : IDisposable
{
    /// <summary>
    /// Retrieves the current values of all defined sensors.
    /// </summary>
    /// <returns>
    /// A dictionary mapping sensor aliases to their current values. 
    /// The value is <c>null</c> if the sensor value is unavailable.
    /// </returns>
    Dictionary<string, float?> GetSensorValues();

    /// <summary>
    /// Retrieves the current values of all defined controls.
    /// </summary>
    /// <returns>
    /// A dictionary mapping control aliases to their current values. 
    /// The value is <c>null</c> if the control value is unavailable.
    /// </returns>
    Dictionary<string, float?> GetControlValues();

    /// <summary>
    /// Sets the specified control values.
    /// </summary>
    /// <param name="controlValues">
    /// A dictionary mapping control alias to the values to set.
    /// </param>
    /// <param name="force">
    /// If <c>true</c>, forces the control values to be set even if they are already at the desired value.
    /// Default is <c>false</c>.
    /// </param>
    /// <returns>
    /// A dictionary mapping control aliases to a boolean indicating whether the set operation was successful.
    /// </returns>/// 
    Dictionary<string, bool> SetControls(Dictionary<string, float> controlValues, bool force = false);

    /// <summary>
    /// Releases control over all managed controls, returning them to their default or unmanaged state.
    /// </summary>
    /// <returns>
    /// A dictionary mapping control aliases to a boolean indicating whether the release was successful.
    /// </returns>
    Dictionary<string, bool> ReleaseControls();

    /// <summary>
    /// Lists all available sensors to the output or log.
    /// </summary>
    void ListAllSensors();

    /// <summary>
    /// Suspends monitoring and control operations.
    /// </summary>
    void Suspend();

    /// <summary>
    /// Resumes monitoring and control operations after a suspension.
    /// </summary>
    void Resume();
}

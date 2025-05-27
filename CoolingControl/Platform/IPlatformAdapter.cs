namespace CoolingControl.Platform;

/// <summary>
/// Defines an interface for platform-specific adapters that provide access to sensor and control values,
/// as well as methods to manage and interact with hardware controls and sensors.
/// </summary>
/// <remarks>
/// Implementations of this interface should handle the retrieval and manipulation of hardware sensor and control data,
/// and provide mechanisms to suspend and resume platform-specific operations.
/// </remarks>
public interface IPlatformAdapter : IDisposable

{
    /// <summary>
    /// Special value that signals that the control should be set to a default or unmanaged state.
    /// </summary>
    public const float DefaultControlValue = -9999f;

    /// <summary>
    /// Retrieves the current values of the specified sensors.
    /// </summary>
    /// <param name="sensorIdentifiers">A set of sensor identifiers to query.</param>
    /// <returns>
    /// A dictionary mapping each sensor identifier to its current value, or <c>null</c> if unavailable.
    /// </returns>
    Dictionary<string, float?> GetSensorValues(HashSet<string> sensorIdentifiers);

    /// <summary>
    /// Retrieves the current values of the specified controls.
    /// </summary>
    /// <param name="controlIdentifiers">A set of control identifiers to query.</param>
    /// <returns>
    /// A dictionary mapping each control identifier to its current value, or <c>null</c> if unavailable.
    /// </returns>
    Dictionary<string, float?> GetControlValues(HashSet<string> controlIdentifiers);

    /// <summary>
    /// Sets the specified control values.
    /// </summary>
    /// <param name="controlValues">A dictionary mapping control identifiers to the values to set.</param>
    /// <returns>
    /// A dictionary mapping each control identifier to a boolean indicating success or failure.
    /// </returns>
    Dictionary<string, bool> SetControls(Dictionary<string, float> controlValues);

    /// <summary>
    /// Releases (resets) the specified controls to their default state.
    /// </summary>
    /// <param name="controlIdentifiers">A set of control identifiers to release.</param>
    /// <returns>
    /// A dictionary mapping each control identifier to a boolean indicating success or failure.
    /// </returns>
    Dictionary<string, bool> ReleaseControls(HashSet<string> controlIdentifiers);

    /// <summary>
    /// Lists all available sensors and controls on the platform.
    /// </summary>
    void ListAllSensors();

    /// <summary>
    /// Suspends platform-specific operations, such as hardware polling or monitoring.
    /// </summary>
    void Suspend();

    /// <summary>
    /// Resumes platform-specific operations after a suspension.
    /// </summary>
    void Resume();
}


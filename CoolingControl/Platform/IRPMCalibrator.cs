namespace CoolingControl.Platform;

/// <summary>
/// Defines an interface for RPM calibration, providing methods to get and set control values,
/// retrieve RPM sensor values, and release control for specified aliases or identifiers.
/// </summary>
public interface IRPMCalibrator : IDisposable
{
    /// <summary>
    /// Gets the current control value associated with the specified alias.
    /// </summary>
    /// <param name="alias">The alias identifying the control.</param>
    /// <returns>The control value if available; otherwise, <c>null</c>.</returns>
    float? GetControlValue(string alias);

    /// <summary>
    /// Gets the current RPM sensor value associated with the specified control.
    /// </summary>
    /// <param name="alias">The alias identifying the control.</param>
    /// <returns>The RPM sensor value if available; otherwise, <c>null</c>.</returns>
    float? GetRPMSensorValue(string alias);

    /// <summary>
    /// Sets the control value for the specified alias.
    /// </summary>
    /// <param name="alias">The alias identifying the control.</param>
    /// <param name="controlValue">The value to set for the control.</param>
    /// <returns><c>true</c> if the control value was set successfully; otherwise, <c>false</c>.</returns>
    bool SetControl(string alias, float controlValue);

    /// <summary>
    /// Releases (resets) the specified control to its default state.
    /// </summary>
    /// <param name="alias">The identifier for which to release control.</param>
    /// <returns><c>true</c> if the control was released successfully; otherwise, <c>false</c>.</returns>
    bool ReleaseControl(string alias);
}
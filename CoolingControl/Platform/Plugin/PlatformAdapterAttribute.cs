namespace CoolingControl.Platform;

/// <summary>
/// Declares the platform name that this <see cref="IPlatformAdapter"/> implementation handles.
/// Required on every adapter class, including plugins.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PlatformAdapterAttribute(string platformName) : Attribute
{
    public string PlatformName { get; } = platformName;
}

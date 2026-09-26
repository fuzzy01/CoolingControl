namespace CoolingControl.Platform;

public sealed record HardwareChannel(
    string Platform,
    string Identifier,
    string Name,
    string HardwareName,
    string SensorType,
    bool IsControl);

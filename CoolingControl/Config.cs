namespace CoolingControl;

/// <summary>
/// Represents the configuration settings for the CoolingControl service.
/// </summary>
public class Config
{
    public string ScriptPath { get; set; } = "cooling_control.lua";
    public int UpdateIntervalMs { get; set; } = 1000;
    public string LogLevel { get; set; } = "Information";
    public LHMConfigParameters LHMConfig { get; set; } = new LHMConfigParameters();
    public List<ControlConfig> Controls { get; set; } = [];
    public List<SensorConfig> Sensors { get; set; } = [];
}

public class LHMConfigParameters
{
    public bool CpuEnabled { get; set; } = true;
    public bool GpuEnabled { get; set; } = true;
    public bool MotherboardEnabled { get; set; } = false;
    public bool MemoryEnabled { get; set; } = false;
    public bool StorageEnabled { get; set; } = false;
    public bool NetworkEnabled { get; set; } = false;
    public bool ControllerEnabled { get; set; } = false;
    public bool BatteryEnabled { get; set; } = false;
    public bool PsuEnabled { get; set; } = false;
}

public class ControlConfig
{
    public string Platform { get; set; } = "LHM";
    public required string Identifier { get; set; }
    public required string Alias { get; set; }
    public float StepUp { get; set; } = 8.0f;
    public float StepDown { get; set; } = 8.0f;
    public float MinStop { get; set; } = 20.0f;
    public float MinStart { get; set; } = 20.0f;
    public bool ZeroRPM { get; set; } = false;
    public string RPMSensor { get; set; } = string.Empty;
    public List<RPMCalibrationData> RPMCalibration { get; set; } = new List<RPMCalibrationData>();
}

public class RPMCalibrationData
{
    public float Control { get; set; }
    public float Rpm { get; set; }
}

public class SensorConfig
{
    public string Platform { get; set; } = "LHM";
    public required string Identifier { get; set; }
    public required string Alias { get; set; }   
}
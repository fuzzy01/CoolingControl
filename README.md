# CoolingControl Documentation

## Overview

`CoolingControl` is an application for controlling fans and pumps based on any system sensors with flexible control logic customized for your PC build. Unlike traditional fan control software, `CoolingControl` offers unparalleled flexibility with Lua-based scripting, allowing users to define custom logic for any sensor, not just temperature. You can write your own control logic from scratch or use one of the examples. A Lua library with common algorithms is also included. The application runs as a Windows service or in console mode, leveraging Libre Hardware Monitor for hardware access.

If you have an AIO cooler, it is preferred using the coolant temperature sensor for fan control. If your AIO does not have a coolant temperature sensor, it is recommended attaching a thermal sensor to the outgoing tube at the radiator and connecting it to a motherboard connector (most Asus motherboards has a connector labeled "T Sensor"). Some sensors that work and can be easily purchased: XSPC Wire Sensor 10k, Phobya 10k Temperature Sensor.


### Features

- **Hardware Support**:
  - **RPM-Based fan/pump control**: Set fan/pumps speeds in RPM (e.g., 1000 RPM) instead of percentages, with calibration data mapping RPM to hardware-compatible percentages.
  - **Fan/Pump calibration**: Fan/pump parameters (min start, min stop, RPM curve) are autocalibrated and permanently stored in config file.
- **Control logic features**: Users can write their own control logic in Lua, with a library of common algorithms included.
  - **Sensor-based control**: Uses any system sensor (e.g., CPU/GPU temperatures, CPU/GPU power, coolant temperature) for fan/pump control.
  - **Ramp up/Ramp down control**: Smoothly ramps up/down fan speeds to prevent sudden changes.
  - **Exponential Moving Average**: Smooths sensor data, filters out spikes.
  - **Hysteresis**: Prevents rapid fan/pump speed changes, ensuring stability.
  - **PID control**: PID control for AIO fan speed based on coolant temperature.
  - **Global State**: Maintains state across control updates.
- **Plugin Support**: Extend hardware access by dropping a plugin's DLL (and its dependencies) into its own subdirectory under `plugins/`. Any class implementing `IPlatformAdapter` and decorated with `[PlatformAdapter("Name")]` is discovered automatically at startup. Individual sensors and controls can be assigned to different platforms via the `Platform` field in `config.json`.
- **Service Management**: Runs as a Windows service or in console mode.
- **Lifecycle events**: Service startup/shutdown and power events (suspend/resume) trigger Lua callbacks so the script can be notified and manage state.
- **Resource Cleanup**: Proper releasing of hardware resources to BIOS control on service stop.
- **Installer**: Inno Setup based installer preserves user-modified files `config.json` and `cooling_control.lua`.

## Installation

### Prerequisites

- Windows 10 or later.
- .NET 8 runtime  or .NET 10 runtime (optional).
- Administrator privileges for installation and service management.
- Compatible hardware supported by Libre Hardware Monitor.

### Using the Pre-Built Installer

1. Download the installer `CoolingControlSetup-net8.exe` from Releases. Alternatively, if you have .NET 9 installed (optional), you can use `CoolingControlSetup-net9.exe`.
2. Run the installer as administrator.
3. Follow the wizard:
   - Installs to `C:\Program Files\CoolingControl`.
   - Creates the service `CoolingControl` and starts it.

### Building the Application

1. Clone the Repository

   ```cmd
   git clone https://github.com/fuzzy01/coolingcontrol.git
   ```

2. Build the Project
   - Open the solution directory in VSCode and build the installer with `Build Inno Setup Installer net8.0` task.
3. Run the installer as administrator.
4. Follow the wizard:
   - Installs to `C:\Program Files\CoolingControl`.
   - Creates the service `CoolingControl` and starts it.

## First setup after installation

The installer creates a bare bone `config.json` and `cooling_control.lua` in the config directory.Follow the following steps to configure the application. See the details in usage section below.

1. **Stop the service**: Run `sc.exe stop CoolingControl` to stop the service before making changes.
2. **List available sensors**: Run `CoolingControl.exe list-sensors` to identify available sensors (e.g., CPU, GPU temperatures and power) and controls (e.g., fans and pumps)
3. **Edit config.json**: Modify `config.json` to set up sensors and controls. Define sensors and controls using aliases and hardware IDs from the list-sensors output.

- For each control (fan/pump), specify the following:
  - `Alias`: A user-friendly name for the fan/pump (e.g., "AIO Fan").
  - `Identifier`: The hardware ID of the fan/pump (e.g., "/lpc/nct6798d/0/control/1").
  - `RPMSensor`: The RPM sensor ID for the fan/pump (e.g., "/lpc/nct6798d/0/fan/1").
- For each sensor, specify the following:
  - `Alias`: A user-friendly name for the sensor (e.g., "CPU Package").
  - `Identifier`: The hardware ID of the sensor (e.g., "/intelcpu/0/temperature/22").

**Example**:

```json
{
  "ScriptPath": "config/cooling_control.lua",
  "UpdateIntervalMs": 1000,
  "LogLevel": "Information",
  "MaxControlLoopErrors": 10,
  "LHMConfig": {
    "CpuEnabled": true,
    "GpuEnabled": true,
    "MotherboardEnabled": true,
    "MemoryEnabled": false,
    "StorageEnabled": false,
    "NetworkEnabled": false,
    "ControllerEnabled": false,
    "BatteryEnabled": false,
    "PsuEnabled": false
  },
  "Controls": [
    {
      "Identifier": "/lpc/nct6798d/0/control/1",
      "Alias": "AIO Fan",
      "RPMSensor": "/lpc/nct6798d/0/fan/1"
    },
    {
      "Identifier": "/lpc/nct6798d/0/control/5",
      "Alias": "AIO Pump",
      "RPMSensor": "/lpc/nct6798d/0/fan/5"
    },
    {
      "Identifier": "/lpc/nct6798d/0/control/0",
      "Alias": "Case Fan",
      "RPMSensor": "/lpc/nct6798d/0/fan/0"
    },
    {
      "Identifier": "/gpu-nvidia/0/control/1",
      "Alias": "GPU Fan",
      "MinStop": 30,
      "MinStart": 30,
      "ZeroRPM": true
    }
  ],
  "Sensors": [
    {
      "Identifier": "/intelcpu/0/temperature/22",
      "Alias": "CPU Package"
    },
    {
      "Identifier": "/intelcpu/0/power/0",
      "Alias": "CPU Power"
    },
    {
      "Identifier": "/gpu-nvidia/0/temperature/0",
      "Alias": "GPU Core"
    },
    {
      "Platform": "LHM",
      "Identifier": "/gpu-nvidia/0/load/5",
      "Alias": "GPU Board Power"
    },    
    {
      "Identifier": "/lpc/nct6798d/0/temperature/8",
      "Alias": "T Sensor"
    }
  ]
}
```

- Description of the fields:
  - `ScriptPath`: Path to the Lua script for fan control logic (e.g., `config/cooling_control.lua`).
  - `UpdateIntervalMs`: Interval in milliseconds between reading sensors, running control logic and setting controls (default: 1000 ms).
  - `LogLevel`: Logging level (e.g., "Information", "Debug").
  - `MaxControlLoopErrors`: Number of errors allowed within a 60-second window before the service stops. Increase this to tolerate brief sensor glitches (default: 10). When the count in that window reaches three quarters of this limit, the status page lists an alert and the tray shows a Windows toast.
  - `StatusServerEnabled`: Enable or disable the HTTP status server (default: true).
  - `StatusServerPort`: Port for the HTTP status server dashboard (default: 19999).
  - `StatusServerBindAddress`: Address the HTTP status server binds to (default: `"localhost"`). Set to `"+"` to allow access from other devices on the network (`"0.0.0.0"` is not a valid HTTP.sys prefix).
  - `Profiles`: Optional list of profile names, for example `["silent", "balanced", "performance"]`. An empty list leaves the script on one curve and the dashboard shows no profile buttons.
  - `ActiveProfile`: Profile name the script starts with. It must be one of `Profiles`, or `""`. The status page can change it, and the new name is written back to `config.json`.
  - `LHMConfig`: Configuration for Libre Hardware Monitor (LHM) sensor type groups.
  - `Controls`: List of fan/pump controls with their aliases, identifiers, and RPM sensors.
  - `Sensors`: List of sensors with their aliases and identifiers.
  - `Alias`: A user-friendly name for the sensor (e.g., "CPU Package").
  - `AlertMax`: Optional maximum for this sensor. A reading at or above it raises an alert. The alert stays until the reading falls 5% below that maximum, so a load test does not raise a new toast on every small change. Omit it to leave the sensor out of temperature alerts. A missing reading does not alert. Example: `"AlertMax": 90` on the CPU Package sensor.
  - `Identifier`: The hardware ID of the sensor (e.g., "/intelcpu/0/temperature/22").
  - `Platform`: The platform adapter that provides this sensor or control. Defaults to `"LHM"` (Libre Hardware Monitor). Set to the name of a plugin adapter to route this entry to a plugin (see [Plugin Support](#plugin-support)).
  - `StepUp`: Maximum step up in % per update interval (default: 8%).
  - `StepDown`: Maximum step down in % per update interval (default: 8%).
  - `ZeroRPM`: If true, when the control is set to 0 RPM the control is handed back to the hardware, it is needed to support GPU fans (default: false).
  - `RPMSensor`: The RPM sensor ID for the fan/pump (e.g., "/lpc/nct6798d/0/fan/1").
  - `BeatDetuneMinSeparationRpm`: Minimum RPM gap between fans that opt in to beat detune (default: 150). A smaller positive gap is opened by raising the faster fan.
  - `BeatDetuneMaxNudgeRpm`: Largest RPM increase beat detune may apply to one fan on a tick (default: 200).
  - `BeatDetune`: When true, this fan takes part in beat detune (default: false). Leave it false on pumps and on matched GPU fans.

4. **Calibrate fans and pumps**: Run `CoolingControl.exe calibrate all` to generate calibration data for all fans and pumps. This will update `config.json` with the calibration data.
5. **Edit cooling_control.lua**: Customize the Lua script for specific control logic. You can use the provided examples (cooling_control_aio_sample.lua, cooling_control_aircooling_sample.lua, cooling_control_profiles_sample.lua) or create your own. The script is executed every UpdateIntervalMs, and the `calculate_controls` function is called to determine the fan/pump speeds based on the sensor data. The script can use any sensor data available in the system that is define in `config.json`. You can specify the control value (fan/pump speed) in RPM or percentage. The script can also use the provided Lua library for common algorithms (e.g., exponential moving average, hysteresis, linear curve). Ramp up/down and min start/min stop logic is applied by the app framework, no need to handle it the control script. Controls with `BeatDetune` set are also spread apart in RPM by the framework after the script returns. See [Beat detune](#beat-detune).

**Example for AIO without coolant temperature sensor**:

```lua
local cf = require("config/cooling_functions")

function on_start()
    log_information("CoolingControl started")
    -- Example: Log control and sensor configuration
    for alias, ctrl in pairs(control_config) do
        log_debug("Control: " .. alias .. " (min_start=" .. ctrl.min_start .. "%, min_stop=" .. ctrl.min_stop .. "%)")
    end
    for alias, sensor in pairs(sensor_config) do
        log_debug("Sensor: " .. alias .. " (identifier=" .. sensor.identifier .. ")")
    end
end

function on_resume()
    cf.on_resume()
end

-- Silent AIO fan curve base on CPU temperature, adjust as needed based on your system and how silent you want it to be
local cpu_fan_curve =  { 
                        { sensor_value = 45, control_value = 600 }, 
                        { sensor_value = 65, control_value = 900 },
                        { sensor_value = 78, control_value = 1300 }, 
                        { sensor_value = 85, control_value = 2230 } } 

-- Silent case fan curve base on GPU temperature, adjust as needed based on your system and how silent you want it to be
local case_fan_curve =  { 
                        { sensor_value = 62, control_value = 500 }, 
                        { sensor_value = 68, control_value = 700 },
                        { sensor_value = 75, control_value = 1000 }, 
                        { sensor_value = 83, control_value = 1600 } } 
                        

-- max_case_fan_speed / max_aio_fan_speed
local case_aio_fan_scale = 1600 / 2230

function calculate_controls(sensors)
    local result = {}

    local cpu_temp = sensors["CPU Package"] or 50

    -- Apply moving average
    cpu_temp = cf.apply_ema("CPU Package", cpu_temp)
   
    -- Calc AIO fan
    local aio_fan_rpm = cf.apply_linear_curve(cpu_temp, cpu_fan_curve)
   
    -- Apply hysteresis based on CPU temperature
    aio_fan_rpm = cf.apply_hysteresis("AIO Fan", aio_fan_rpm, cpu_temp, 30, 100, 4, 2)

    table.insert(result, { alias = "AIO Fan", rpm = aio_fan_rpm })  

    -- AIO pump speed is fixed
    local aio_pump_speed = 80
    
    table.insert(result, { alias = "AIO Pump", value = aio_pump_speed })
  
    -- Case fan: Based on GPU temperature mixed with AIO fan
    local gpu_temp = sensors["GPU Core"] or 50

    -- Apply moving average
    gpu_temp = cf.apply_ema("GPU Core", gpu_temp)

    -- Apply fan curve
    local case_fan_rpm = cf.apply_linear_curve(gpu_temp, case_fan_curve)

    -- Apply hysteresis based on GPU temperature
    case_fan_rpm = cf.apply_hysteresis("Case Fan", case_fan_rpm, gpu_temp, 45, 83, 4, 2)

    -- Mix with AIO fan, max 1600 RPM for case fan to keep it quiet
    case_fan_rpm = math.min(1600, math.max(aio_fan_rpm * case_aio_fan_scale, case_fan_rpm))
    
    table.insert(result, { alias = "Case Fan", rpm = case_fan_rpm })
  
    return result
end
```

- Description of the functions:
  - `calculate_controls(sensors)`: Main function for calculating fan/pump speeds based on sensor data. The `sensors` parameter is a table containing the sensor values defined in `config.json`. The function returns a table with the calculated RPM values (rpm field) or percentage value (value field) for each control defined in `config.json`. Ramp up/down and min start/min stop logic is applied by the app framework, no need to handle it the control script.
  - `on_start()`: Called when the service starts up. Use this to initialize state, reset counters, or perform startup tasks.
  - `on_stop()`: Called when the service is shutting down. Use this to clean up resources or log final state.
  - `on_resume()`: Called when the system resumes from sleep. You can use this to reset any state.
  - `on_suspend()`: Called when the system is about to suspend. You can use this to save state or prepare for suspension.
  - `on_power_source_changed(is_ac_powered)`: Called when Windows detects a transition between AC and battery power. `is_ac_powered` is `true` on AC power and `false` on battery power.
  - `on_profile_changed(name)`: Called when the active profile changes. `name` is the new profile name. Call `cf.on_resume()` here to clear EMA, hysteresis, and PID state from the previous curve.

- Available Lua global tables:
  - `active_profile`: The current profile name. It is `""` when `Profiles` is empty. Read it inside `calculate_controls` and pick the curve table for that name. `cooling_control_profiles_sample.lua` shows one way to do that.
  - `sensors`: The current sensor values (e.g., `sensors["CPU Package"]`)
  - `control_config`: Configuration for all controls by alias. Each entry has:
    - `alias`: The control name
    - `identifier`: Hardware identifier
    - `platform`: The platform (e.g., "LHM")
    - `step_up`: Maximum step up in % per update interval
    - `step_down`: Maximum step down in % per update interval
    - `min_start`: Minimum control value to start
    - `min_stop`: Minimum control value to stop
    - `zero_rpm`: Whether 0 RPM returns control to hardware
    - `rpm_sensor`: The RPM sensor identifier
    - `rpm_calibration`: Array of calibration points with `control` (%) and `rpm` (RPM) values
    - `thermal_min_control`: Optional thermal minimum control value
  - `sensor_config`: Configuration for all sensors by alias. Each entry has:
    - `alias`: The sensor name
    - `identifier`: Hardware identifier
    - `platform`: The platform (e.g., "LHM")
- Available Lua global functions (registered by the host, not by `cooling_functions.lua`):
  - `log_debug(message)`: Logs a debug message to the log file.
  - `log_information(message)`: Logs an information message to the log file.
  - `log_error(message)`: Logs an error message to the log file.
- Description of the functions in the Lua library `cooling_functions.lua`:
  - `cf.on_resume()`: A function that should be called when the system resumes from sleep.
  - `cf.apply_ema()`: A function that applies exponential moving average to smooth out sensor readings.
  - `cf.apply_linear_curve()`: A function that applies a linear curve to map sensor values to fan/pump speeds based on the defined curve.
  - `cf.apply_hysteresis()`: A function that applies hysteresis logic to prevent rapid changes in fan/pump speeds based on sensor fluctuations. Its `response_time` argument is the burst hold: the last fan speed stays in place for that many ticks.
  - `cf.apply_heat_debt(mass_temp, curve_rpm, floor, payback_rpm)`: While `mass_temp` is above `floor`, the returned speed does not fall below `payback_rpm`. At or below `floor` the curve speed is returned unchanged. `response_time` is the burst hold; this function is the warm-mass floor.
  - `cf.aio_fan_pid_control()`: A function that calculates the fan speed based on the coolant temperature, using PID control. Limits for fan speeds should be set according to noise preferences and AIO size.

## Usage

### Running in Console Mode

- Open Command Prompt (cmd.exe) or PowerShell with administrative priviliges.
- Change to root directory and execute it without any parameters

  ```cmd
  cd "C:\Program Files\CoolingControl"
  CoolingControl.exe
  ```

- Outputs logs to console and `logs\cooling_control.log`.
- Press **Ctrl+C** to stop.

### Running as a Service

- The installer installs the CoolingControl service. You can stop/start it from 'services.msc'
- Also you can stop/start from the command line:
- Stop:
  
  ```cmd
  sc.exe stop CoolingControl
  sc.exe start CoolingControl
  ```

- Logs to `logs\cooling_control.log` in the application directory.

### Monitoring Status (HTTP Dashboard)

- The application runs a local HTTP status server on `localhost:19999` (configurable via `StatusServerPort` and `StatusServerBindAddress` in `config.json`)
- Open a browser and navigate to: `http://localhost:19999`
- The dashboard displays:
  - **Sensors**: Real-time sensor values (temperatures, power, load, etc.) with aliases
  - **Controls**: Current control outputs (fan/pump speeds in RPM or %)
  - **Service Info**: Uptime, last update time, script path, update interval
  - **Profile**: When `Profiles` is non-empty, one button per name. The active name is highlighted. Choosing a button switches the profile on the next control tick.
- The dashboard auto-refreshes every second
- JSON API available at: `http://localhost:19999/api/status` (includes `activeProfile`, `profiles`, and `alerts`)
- Switch profile with `POST http://localhost:19999/api/profile` and body `{ "name": "silent" }`. The request is accepted only from the same machine; other devices receive 403 even when `StatusServerBindAddress` is `"+"`. An unknown name receives 400. A successful change is saved to `config.json`.
- Prometheus metrics available at: `http://localhost:19999/metrics` — exposes `sensor_value{name="..."}` and `control_output{name="..."}` gauges for Grafana integration
- To allow access from other devices on the network, set `"StatusServerBindAddress": "+"` in `config.json`
- Disable the server with `"StatusServerEnabled": false` in `config.json` if not needed

### Tray

`CoolingControlTray.exe` runs in the logged-on user's notification area. It is a separate program from the Windows service. The menu lists active alerts, the configured sensor values, the control outputs, and the active profile when `Profiles` is set. A new alert is also shown as a Windows toast titled CoolingControl. The menu also shows uptime and how long ago the service last updated. Choosing a profile calls `POST /api/profile`. **Open dashboard** and a double-click open `http://localhost:19999/` when the port is the default. **Exit** closes the tray and leaves the service running.

The installer can add a login shortcut for every user. Uncheck "Start CoolingControl tray at login" to skip that. The port comes from `StatusServerPort` in `config\config.json` next to the exe. When the status server is disabled, the icon stays and the menu says so.

### Listing Sensors

- First stop the service if it is running:
  
  ```cmd
  sc.exe stop CoolingControl
  ```
  
- Identify available sensors (e.g., CPU, GPU temperatures and power) and controls (e.g., fans and pumps) using the following command:

  ```cmd
    CoolingControl.exe list-sensors
  ```

- This command lists all available sensors and their names, which you can use in the `config.json` file to configure sensors and controls.

### Calibrating Fans and Pumps

- The calibration process requires the service to be stopped first. You can do this with the following command:
  
    ```cmd
    sc.exe stop CoolingControl
    ```

- The calibration process requires the fans and pumps defined in the `config.json` file to be calibrated.
- Use the `CoolingControl.exe calibrate` command to generate calibration data for a specific fan or pumps or for all of them. This will update `config.json` file with the calibration data.

  ```cmd
    CoolingControl.exe calibrate "Case Fan" 
    CoolingControl.exe calibrate all
  ```

- The calibration process will take some time, as it needs to measure the RPM of the fans and pumps at different speeds.
- The calibration data includes the minimum start and stop RPM, as well as the RPM curve for each fan and pump.
- The calibration data is used to convert RPM values to percentages for the fan control logic.

### Beat detune

Fans whose speeds sit a few tens of RPM apart produce a slow beat. Set `BeatDetune` to true on each fan that should take part. After the script returns, CoolingControl sorts those fans by requested RPM and raises one when it is closer than `BeatDetuneMinSeparationRpm` (default 150) to the fan just below it. It never lowers a fan. A raise stops at `BeatDetuneMaxNudgeRpm` (default 200) and at the top of that fan's RPM calibration.

Fans already at least that far apart are left alone. Two fans whose requested speeds are within 1 RPM stay matched, including when that pair is raised together to clear a slower fan. A request at or below 0 stays out of the pass, so a stopped fan is not spun up to open a gap. Pumps and matched GPU fans stay out by leaving `BeatDetune` false.

150 RPM is about a 2.5 Hz rotational beat (`gap / 60`). Step ramp can still carry a measured speed through that window while the duty catches up. The status page and CSV log show the detuned request, before ramp and zero-RPM handback.

## Plugin Support

CoolingControl can load external hardware adapters from a `plugins/` subdirectory next to the executable. This allows adding sensor and control sources beyond Libre Hardware Monitor without modifying the application.

### How it works

Each plugin lives in its own subdirectory under `plugins/`, named to match its main DLL, e.g. `plugins/MyAdapter/MyAdapter.dll` plus any of its private dependency DLLs. Keeping every plugin in its own folder isolates its dependencies from other plugins, so two plugins can each ship a different version of the same dependency without conflicts.

At startup, every subdirectory of `plugins/` is scanned for a DLL named after that subdirectory (e.g. `plugins/MyAdapter/MyAdapter.dll`); a subdirectory without a matching DLL is skipped with a warning. Any class in that DLL that:

1. Implements `IPlatformAdapter`
2. Is decorated with `[PlatformAdapter("YourName")]`
3. Has a constructor accepting `(ConfigHelper config)` or a parameterless constructor

…is instantiated and registered under its declared platform name. Individual sensors and controls are routed to the correct adapter via the `Platform` field in `config.json`.

### Writing a plugin

Reference `CoolingControl.dll` from your class library project and implement the interface:

```csharp
using CoolingControl.Platform;

[PlatformAdapter("MyAdapter")]
public sealed class MyAdapter : IPlatformAdapter
{
    public Dictionary<string, float?> GetSensorValues(HashSet<string> sensorIdentifiers) { ... }
    public Dictionary<string, float?> GetControlValues(HashSet<string> controlIdentifiers) { ... }
    public Dictionary<string, bool> SetControls(Dictionary<string, float> controlValues) { ... }
    public Dictionary<string, bool> ReleaseControls(HashSet<string> controlIdentifiers) { ... }
    public void ListAllSensors() { ... }
    public void Suspend() { ... }
    public void Resume() { ... }
    public void Dispose() { ... }
}
```

Build to a DLL and copy your plugin's own output (the DLL plus any of its private dependency DLLs) into its own subdirectory named to match the DLL, e.g. `plugins/MyAdapter/MyAdapter.dll`. Then reference its sensors and controls in `config.json` using `"Platform": "MyAdapter"`.

### Using a plugin sensor in config.json

```json
{
  "Sensors": [
    { "Platform": "MyAdapter", "Identifier": "myadapter/temp1", "Alias": "Custom Temp" }
  ]
}
```

### Example: DummyPlugin

`CoolingControl.DummyPlugin` is an in-tree reference implementation. It registers as `"Dummy"` and returns a static `50.0` for all sensor reads. Build the `CoolingControl.DummyPlugin` project to compile and automatically copy it to the host's `plugins/CoolingControl.DummyPlugin/` directory.

### Example: DS18B20Plugin

`CoolingControl.DS18B20Plugin` reads DS18B20 temperature sensors connected through a USB-to-serial adapter that continuously streams ASCII readings at 9600 baud, 8 data bits, no parity, 1 stop bit (8N1), one reading per line, e.g.:

```
t1=+28.70
t2=+29.20
```

It registers as `"DS18B20"` and is sensor-only (no controls). Sensor identifiers must use the form `<COMPORT>/<tag>`, where `<tag>` is the sensor label reported by the adapter (`t1`, `t2`, …):

```json
{
  "Sensors": [
    { "Platform": "DS18B20", "Identifier": "COM5/t1", "Alias": "Coolant In" },
    { "Platform": "DS18B20", "Identifier": "COM5/t2", "Alias": "Coolant Out" }
  ]
}
```

The plugin opens one serial port per distinct COM port referenced in `config.json`, and automatically reconnects if the adapter is unplugged or the port fails to open. A reading is considered stale (returned as `null`) if none has been received for more than 5 seconds. Notes:

- Windows can occasionally reassign the COM port number (e.g. if the adapter is moved to a different USB port). If `config.json` stops matching, check Device Manager → Ports (COM & LPT) for the adapter's current port, and optionally pin it via Properties → Port Settings → Advanced → COM Port Number.
- Which physical DS18B20 sensor maps to `t1` vs `t2` is decided by the adapter firmware's 1-Wire discovery order, not by this plugin.

## Configuration

### config.json

Located in the application directory (e.g., `C:\Program Files\CoolingControl\config`), `config.json` defines aliases, hardware IDs, calibration data, log level, and update intervals. User modifications to `config.json` are preserved during installation or reinstallation.

## License

This project is licensed under the GNU General Public License v3.0. See the [LICENSE](LICENSE) file for details.

## Contributing

- Submit issues or pull requests to the repository (if applicable).
- Share calibration data or Lua scripts for common hardware.

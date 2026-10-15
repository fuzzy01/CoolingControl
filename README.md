# CoolingControl Documentation

## Contents

- [Overview](#overview)
- [Installation](#installation)
  - [Prerequisites](#prerequisites)
  - [Using the Pre-Built Installer](#using-the-pre-built-installer)
  - [Building the Application](#building-the-application)
- [First setup after installation](#first-setup-after-installation)
  - [Hardware config page](#hardware-config-page)
  - [Command line](#command-line)
  - [Calibrate and edit the script](#calibrate-and-edit-the-script)
- [Usage](#usage)
  - [Running in Console Mode](#running-in-console-mode)
  - [Running as a Service](#running-as-a-service)
  - [Monitoring Status (HTTP Dashboard)](#monitoring-status-http-dashboard)
  - [Tray](#tray)
  - [Listing Sensors](#listing-sensors)
  - [Calibrating Fans and Pumps](#calibrating-fans-and-pumps)
  - [Temperature calibration (experimental)](#temperature-calibration-experimental)
  - [Beat detune](#beat-detune)
- [Plugin Support](#plugin-support)
  - [How it works](#how-it-works)
  - [Writing a plugin](#writing-a-plugin)
  - [Using a plugin sensor in config.json](#using-a-plugin-sensor-in-configjson)
  - [Libre Hardware Monitor](#libre-hardware-monitor)
  - [DummyPlugin](#dummyplugin)
  - [DS18B20Plugin](#ds18b20plugin)
- [Configuration](#configuration)
- [License](#license)
- [Contributing](#contributing)

## Overview

`CoolingControl` is an application for controlling fans and pumps based on any system sensors with flexible control logic customized for your PC build. Unlike traditional fan control software, `CoolingControl` offers unparalleled flexibility with Lua-based scripting, allowing users to define custom logic for any sensor, not just temperature. You can write your own control logic from scratch or use one of the examples. A Lua library with common algorithms is also included. The application runs as a Windows service or in console mode. Hardware access comes from plugins. Libre Hardware Monitor ships as the included `LHM` plugin.

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
- **Plugin Support**: Every hardware source is a plugin. Each one lives in its own subdirectory under `plugins/`, as a DLL plus its private dependencies. A class that implements `IPlatformAdapter` and is decorated with `[PlatformAdapter("Name")]` is loaded at startup. Libre Hardware Monitor is the included `LHM` plugin. Set `Platform` on a sensor or control to choose which plugin serves it.
- **Service Management**: Runs as a Windows service or in console mode.
- **Lifecycle events**: Service startup/shutdown and power events (suspend/resume) trigger Lua callbacks so the script can be notified and manage state.
- **Resource Cleanup**: Proper releasing of hardware resources to BIOS control on service stop.
- **Installer**: Inno Setup based installer preserves user-modified `config.json` and `cooling_control.lua`. Sample scripts and `config_sample.json` are replaced on upgrade.

## Installation

### Prerequisites

- Windows 10 or later.
- The .NET 8 runtime for `CoolingControlSetup-net8.exe`, or the .NET 10 runtime for `CoolingControlSetup-net10.exe`.
- Administrator privileges for installation and service management.
- Hardware supported by a loaded plugin. The included `LHM` plugin uses Libre Hardware Monitor.

### Using the Pre-Built Installer

1. Download `CoolingControlSetup-net8.exe` from Releases when the machine has the .NET 8 runtime, or `CoolingControlSetup-net10.exe` when it has the .NET 10 runtime.
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
   - Open the solution directory in VSCode and build the installer with the `Build Inno Setup Installer net8.0` task or the `Build Inno Setup Installer net10.0` task.
3. Run the installer as administrator.
4. Follow the wizard:
   - Installs to `C:\Program Files\CoolingControl`.
   - Creates the service `CoolingControl` and starts it.

## First setup after installation

The installer creates a bare-bones `config.json` and `cooling_control.lua` in the config directory. Choose the sensors and controls in one of two ways, then calibrate the fans and edit the Lua script.

- **Hardware config page.** The CoolingControl service must be running.
- **Command line.** Stop the service first.

Both write the same `config.json`. Restart the service before a newly saved sensor or control is read or driven.

### Hardware config page

1. Start the service if it is stopped: `sc.exe start CoolingControl`. The installer starts it.
2. Open `http://localhost:19999/config`. The status page at `http://localhost:19999` links here as **Hardware config**.
3. Add a sensor or control by picking a channel. Edit, delete, or reorder the rows already saved in `config.json`.

The page leaves calibration points and `ThermalMinControl` unchanged. What the columns show is described in [Monitoring Status (HTTP Dashboard)](#monitoring-status-http-dashboard). Continue with [Calibrate and edit the script](#calibrate-and-edit-the-script). Starting the service at the end of that section loads the saved entries.

### Command line

Stop the service first: `sc.exe stop CoolingControl`.

1. **List available sensors.** From `C:\Program Files\CoolingControl`, run `CoolingControl.exe list-sensors`. Each line includes the platform, hardware name, channel name, sensor type, identifier, and whether the channel is a control.
2. **Edit `config.json`.** Add sensors and controls using the aliases and hardware identifiers from that list. When the file is saved, continue with [Calibrate and edit the script](#calibrate-and-edit-the-script).

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
    "MemoryEnabled": true,
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
  - `MaxControlLoopErrors`: Number of errors allowed within a 60-second window before the service stops. Increase this to tolerate brief sensor glitches (default: 10). When the count in that window reaches the ceiling of three quarters of this limit, the status page lists an alert and the tray shows a Windows toast.
  - `EnableCSVLogging`: When true, each successful control tick appends one row to `logs\cooling_control_log.csv`. The row is a timestamp, the sensor readings, and the control duties requested by the script after beat detune and before ramp (default: false).
  - `DisableLHMReleaseOnSuspend`: When false, the `LHM` plugin closes Libre Hardware Monitor on suspend and opens it again on resume. Set to true to leave that session open across suspend (default: false).
  - `StatusServerEnabled`: Enable or disable the HTTP status server (default: true).
  - `StatusServerPort`: Port for the HTTP status server dashboard (default: 19999).
  - `StatusServerBindAddress`: Address the HTTP status server binds to (default: `"localhost"`). Set to `"+"` to allow access from other devices on the network (`"0.0.0.0"` is not a valid HTTP.sys prefix).
  - `Profiles`: Optional list of profile names, for example `["silent", "balanced", "performance"]`. An empty list leaves the script on one curve and the dashboard shows no profile buttons.
  - `ActiveProfile`: Profile name the script starts with. It must be one of `Profiles`, or `""`. The status page can change it, and the new name is written back to `config.json`.
  - `LHMConfig`: Which Libre Hardware Monitor groups are opened. The default `config.json` and `config_sample.json` set `CpuEnabled`, `GpuEnabled`, `MotherboardEnabled`, and `MemoryEnabled` to true. `StorageEnabled`, `NetworkEnabled`, `ControllerEnabled`, `BatteryEnabled`, and `PsuEnabled` are false.
  - `Controls`: List of fan/pump controls with their aliases, identifiers, and RPM sensors.
  - `Sensors`: List of sensors with their aliases and identifiers.
  - `Alias`: A user-friendly name for the sensor (e.g., "CPU Package").
  - `AlertMax`: Optional maximum for this sensor. A reading at or above it raises an alert. The alert stays until the reading falls below that maximum by 5% or by 1, whichever drop is larger, so a load test does not raise a new toast on every small change. Omit it to leave the sensor out of temperature alerts. A missing reading does not alert. Example: `"AlertMax": 90` on the CPU Package sensor. An `AlertMax` of 60 clears below 57.
  - `Identifier`: The hardware ID of the sensor (e.g., "/intelcpu/0/temperature/22").
  - `Platform`: The platform adapter that provides this sensor or control. Defaults to `"LHM"` (Libre Hardware Monitor). Set to the name of a plugin adapter to route this entry to a plugin (see [Plugin Support](#plugin-support)).
  - `StepUp`: Maximum step up in % per update interval (default: 8%).
  - `StepDown`: Maximum step down in % per update interval (default: 8%).
  - `ZeroRPM`: If true, when the control is set to 0 RPM the control is handed back to the hardware, it is needed to support GPU fans (default: false).
  - `RPMSensor`: The RPM sensor ID for the fan/pump (e.g., "/lpc/nct6798d/0/fan/1").
  - `BeatDetuneMinSeparationRpm`: Minimum RPM gap between fans that opt in to beat detune (default: 150). A smaller positive gap is opened by raising the faster fan.
  - `BeatDetuneMaxNudgeRpm`: Largest RPM increase beat detune may apply to one fan on a tick (default: 200).
  - `BeatDetune`: When true, this fan takes part in beat detune (default: false). Leave it false on pumps and on matched GPU fans.

### Calibrate and edit the script

Do this after the sensors and controls are saved, from either the page or the command line. Stop the service before calibration: `sc.exe stop CoolingControl`.

1. **Calibrate fans and pumps.** Run `CoolingControl.exe calibrate all` to generate calibration data for all fans and pumps. This updates `config.json` with the calibration data. To calibrate one control, pass its alias, as described in [Calibrating Fans and Pumps](#calibrating-fans-and-pumps).
2. **Edit `cooling_control.lua`.** Customize the Lua script for your control logic. You can use the provided examples (`cooling_control_aio_sample.lua`, `cooling_control_aio_with_sensor_sample.lua`, `cooling_control_aircooling_sample.lua`, `cooling_control_gpu_sample.lua`, `cooling_control_profiles_sample.lua`, `cooling_control_cfg_access_sample.lua`) or create your own. The script runs every `UpdateIntervalMs`, and `calculate_controls` sets the fan and pump speeds from the sensors defined in `config.json`. A control value can be an RPM or a percentage. The included Lua library supplies common algorithms such as exponential moving average, hysteresis, and a linear curve. Ramp up, ramp down, min start, and min stop are applied by the application, so the script does not handle them. Controls with `BeatDetune` set are also spread apart in RPM by the application after the script returns. See [Beat detune](#beat-detune).
3. **Start the service:** `sc.exe start CoolingControl`.

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

- Outputs logs to the console and to a daily file `logs\cooling_controlYYYYMMDD.log`.
- Press **Ctrl+C** to stop.

### Running as a Service

- The installer installs the CoolingControl service. You can stop/start it from 'services.msc'
- Also you can stop/start from the command line:
- Stop:
  
  ```cmd
  sc.exe stop CoolingControl
  sc.exe start CoolingControl
  ```

- Logs to a daily file `logs\cooling_controlYYYYMMDD.log` in the application directory.

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
- The status page links to the hardware config page as **Hardware config**. The page itself is `http://localhost:19999/config`. It lists the sensors and controls saved in `config.json`. The Channel column uses the same `hardware / name (type)` text as the add list, and the RPM sensor column uses the fan name from that list. You can edit parameters other than calibration and `ThermalMinControl`, delete an entry, add one by picking a channel, or move a row up or down. The catalog is read once when the page opens, and again only if that read was empty and you add or edit a control. Add, edit, delete, and reorder are accepted only from the same machine. Restart the service before a saved entry is read or driven. `list-sensors` still prints the discovery log when the service is stopped.
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

- Each log line is one channel: platform, hardware name, channel name, sensor type, identifier, and whether it is a control. Use the identifier and platform in `config.json`. The same list is what the hardware config page offers when you add a sensor or control.

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

### Temperature calibration (experimental)

`calibrate-temp` finds a control duty that holds one sensor at or below a target temperature. Stop the service first. The CPU stress load uses AVX2.

```cmd
CoolingControl.exe calibrate-temp "AIO Fan" "CPU Package" 85
```

The target is parsed with a dot as the decimal separator. When the run finishes, the program asks whether to save the result as `ThermalMinControl` on that control. The value is stored in `config.json` and exposed to Lua as `thermal_min_control`. The service does not apply it by itself, and the hardware config page does not edit it.

### Beat detune

Fans whose speeds sit a few tens of RPM apart produce a slow beat. Set `BeatDetune` to true on each fan that should take part. After the script returns, CoolingControl sorts those fans by requested RPM and raises one when it is closer than `BeatDetuneMinSeparationRpm` (default 150) to the fan just below it. It never lowers a fan. A raise stops at `BeatDetuneMaxNudgeRpm` (default 200) and at the top of that fan's RPM calibration.

Fans already at least that far apart are left alone. Two fans whose requested speeds are within 1 RPM stay matched, including when that pair is raised together to clear a slower fan. A request at or below 0 stays out of the pass, so a stopped fan is not spun up to open a gap. Pumps and matched GPU fans stay out by leaving `BeatDetune` false.

150 RPM is about a 2.5 Hz rotational beat (`gap / 60`). Step ramp can still carry a measured speed through that window while the duty catches up. The status page and CSV log show the detuned request, before ramp and zero-RPM handback.

## Plugin Support

Every hardware source, including Libre Hardware Monitor, is a plugin. Plugins are loaded from `plugins/` next to `CoolingControl.exe`. An installed copy looks in `C:\Program Files\CoolingControl\plugins`.

### How it works

Each plugin lives in its own subdirectory, and the DLL name matches the directory name: `plugins/MyAdapter/MyAdapter.dll`, plus any private dependency DLLs in that same folder. A subdirectory without that DLL is skipped. Separate folders keep dependency versions from colliding when two plugins ship different builds of the same library.

At startup the loader reflects over that DLL. A class is loaded when all of the following are true:

1. It implements `IPlatformAdapter`.
2. It is decorated with `[PlatformAdapter("YourName")]`.
3. It has a constructor that accepts `(ConfigHelper config)`, or a parameterless constructor. The `ConfigHelper` constructor is used when both exist.

The platform name on the attribute is the name you put in `Platform`. A second plugin that declares the same name is skipped. A sensor or control whose `Platform` has no loaded plugin is logged as an error and is not read or driven. `Platform` defaults to `"LHM"`.

`GetHardwareCatalog` returns the channels `list-sensors` prints and the hardware config page offers when you add an entry. Each channel is a `HardwareChannel`:

| Field | Role |
| --- | --- |
| `Platform` | The adapter's platform name. |
| `Identifier` | The value stored in `config.json`. |
| `Name` | The channel name shown in the add list. |
| `HardwareName` | The device name shown before the channel name. |
| `SensorType` | Shown in parentheses in the add list, for example `Temperature` or `Control`. |
| `IsControl` | True for a control channel, false for a sensor. |

The add list text is `HardwareName / Name (SensorType)`. An adapter with nothing to offer returns an empty list.

### Writing a plugin

Reference `CoolingControl.dll` from a class library and implement the interface:

```csharp
using CoolingControl.Platform;

[PlatformAdapter("MyAdapter")]
public sealed class MyAdapter : IPlatformAdapter
{
    public MyAdapter(ConfigHelper config) { }

    public Dictionary<string, float?> GetSensorValues(HashSet<string> sensorIdentifiers) { ... }
    public Dictionary<string, float?> GetControlValues(HashSet<string> controlIdentifiers) { ... }
    public Dictionary<string, bool> SetControls(Dictionary<string, float> controlValues) { ... }
    public Dictionary<string, bool> ReleaseControls(HashSet<string> controlIdentifiers) { ... }

    public IReadOnlyList<HardwareChannel> GetHardwareCatalog() =>
    [
        new("MyAdapter", "myadapter/temp1", "Temp 1", "My Device", "Temperature", false)
    ];

    public void Suspend() { }
    public void Resume() { }
    public void Dispose() { }
}
```

Copy the plugin DLL and its private dependencies to `plugins/MyAdapter/`. The in-tree plugin projects do that on build, into `plugins/<ProjectName>/` under the host output directory.

### Using a plugin sensor in config.json

```json
{
  "Sensors": [
    { "Platform": "MyAdapter", "Identifier": "myadapter/temp1", "Alias": "Custom Temp" }
  ]
}
```

### Libre Hardware Monitor

`CoolingControl.LHMPlugin` is included with the application and registers as `"LHM"`. It opens Libre Hardware Monitor for the groups enabled in `LHMConfig`. Its catalog is the cached Power, Temperature, Fan, Load, Control, Level, Frequency, Flow, Noise, and Humidity sensors from that session. A Libre Hardware Monitor control is a channel with `IsControl` true. The default `config.json` and `config_sample.json` set `CpuEnabled`, `GpuEnabled`, `MotherboardEnabled`, and `MemoryEnabled` to true. `StorageEnabled`, `NetworkEnabled`, `ControllerEnabled`, `BatteryEnabled`, and `PsuEnabled` are false.

On suspend the plugin closes Libre Hardware Monitor and drops its sensor cache, then opens it again on resume. `DisableLHMReleaseOnSuspend` skips that close and reopen.

### DummyPlugin

`CoolingControl.DummyPlugin` is an in-tree sample. It registers as `"Dummy"`, returns `50.0` for every sensor, and remembers control values that were set (an unset control also reads as `50.0`). `GetHardwareCatalog` returns an empty list, so Dummy channels do not appear in the add list or in `list-sensors`. The project targets .NET 8. Building it copies the DLL to `plugins/CoolingControl.DummyPlugin/`.

### DS18B20Plugin

`CoolingControl.DS18B20Plugin` reads DS18B20 temperature sensors through a USB-to-serial adapter that streams ASCII readings at 9600 baud, 8 data bits, no parity, 1 stop bit (8N1), one reading per line, for example:

```
t1=+28.70
t2=+29.20
```

It registers as `"DS18B20"` and is sensor-only. `GetControlValues` and `SetControls` fail for any identifier passed as a control. Sensor identifiers use `<COMPORT>/<tag>`, where `<tag>` is the label the adapter sends (`t1`, `t2`, …):

```json
{
  "Sensors": [
    { "Platform": "DS18B20", "Identifier": "COM5/t1", "Alias": "Coolant In" },
    { "Platform": "DS18B20", "Identifier": "COM5/t2", "Alias": "Coolant Out" }
  ]
}
```

The constructor opens one serial port for each distinct COM port named by a `DS18B20` sensor in `config.json`. The reader reconnects if the adapter is unplugged or the port fails to open. A reading older than 5 seconds is returned as `null`. The catalog lists tags that have been received: the hardware name is the COM port, the channel name is the tag, the type is `Temperature`, and `IsControl` is false. A port that has not sent a tag yet is not in the add list.

- Windows can reassign the COM port number, for example after the adapter moves to another USB port. If `config.json` stops matching, check Device Manager → Ports (COM & LPT), and pin the number under Properties → Port Settings → Advanced → COM Port Number.
- Which physical DS18B20 is `t1` or `t2` is decided by the adapter firmware's 1-Wire discovery order.

## Configuration

### config.json

`config.json` is in the application config directory, for example `C:\Program Files\CoolingControl\config`. It defines aliases, hardware identifiers, calibration data, log level, and update intervals. The installer keeps an existing `config.json` and `cooling_control.lua`. Sample scripts and `config_sample.json` are replaced on upgrade.

## License

This project is licensed under the GNU General Public License v3.0. See the [LICENSE](LICENSE) file for details.

## Contributing

- Submit issues or pull requests to the repository (if applicable).
- Share calibration data or Lua scripts for common hardware.

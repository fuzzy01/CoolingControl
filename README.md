# CoolingControl Documentation

## Overview

`CoolingControl` is an application for controlling fans and pumps based on any system sensors with flexible control logic customized for your PC build. Unlike traditional fan control software, `CoolingControl` offers unparalleled flexibility with Lua-based scripting, allowing users to define custom logic for any sensor, not just temperature. You can write your own control logic from scratch or use one of the examples. A Lua library with common algorithms is also included. The application runs as a Windows service or in console mode, leveraging Libre Hardware Monitor for hardware access.

### Features

- **Hardware Support**:
  - **RPM-Based fan/pump control**: Set fan/pumps speeds in RPM (e.g., 1000 RPM) instead of percentages, with calibration data mapping RPM to hardware-compatible percentages.
  - **Fan/Pump calibration**: Fan/pump parameters (min start, min stop, RPM curve) are autocalibrated and permanently stored in config file.
- **Control logic features**: Users can write their own control logic in Lua, with a library of common algorithms included.
  - **Sensor-based control**: Uses any system sensor (e.g., CPU/GPU temperatures, power) for fan/pump control.
  - **Ramp up/Ramp down control**: Smoothly ramps up/down fan speeds to prevent sudden changes.
  - **Exponential Moving Average**: Smooths sensor data, filters out spikes.
  - **Hysteresis**: Prevents rapid fan/pump speed changes, ensuring stability.
  - **Global State**: Maintains state across control updates.
- **Service Management**: Runs as a Windows service or in console mode.
- **Windows power event support**: Power events (suspend/resume) are handled by the application, and control Lua script is also notified.
- **Resource Cleanup**: Proper releasing of hardware resources to BIOS control on service stop.
- **Installer**: Inno Setup based installer preserves user-modified files `config.json` and `cooling_control.lua`.

## Installation

### Prerequisites

- Windows 10 or later.
- .NET 8 runtime  or .NET 9 runtime (optional).
- Administrator privileges for installation and service management.
- Compatible hardware supported by Libre Hardware Monitor.

### Using the Pre-Built Installer

1. Download the installer `CoolingControlSetup-net8.exe` from Releases. Alternatively, if you have .NET 9 installed (optional), you can use `CoolingControlSetup-net9.exe`.
2. Run the installer as administrator.
3. Follow the wizard:
   - Installs to `C:\Program Files (x86)\CoolingControl`.
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
   - Installs to `C:\Program Files (x86)\CoolingControl`.
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
        "Alias": "AIO Fan",
        "Identifier": "/lpc/nct6798d/0/control/1",
        "RPMSensor": "/lpc/nct6798d/0/fan/1"
      },
      {
        "Alias": "AIO Pump",
        "Identifier": "/lpc/nct6798d/0/control/5",
        "RPMSensor": "/lpc/nct6798d/0/fan/5"
      },
      {
        "Alias": "Case Fan",
        "Identifier": "/lpc/nct6798d/0/control/0",
        "RPMSensor": "/lpc/nct6798d/0/fan/0"
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
        "Identifier": "/gpu-nvidia/0/temperature/2",
        "Alias": "GPU Hot Spot"
      },
      {
        "Identifier": "/gpu-nvidia/0/power/0",
        "Alias": "GPU Power"
      }
    ]
  }
```

- Description of the fields:
  - `ScriptPath`: Path to the Lua script for fan control logic (e.g., `config/cooling_control.lua`).
  - `UpdateIntervalMs`: Interval in milliseconds between reading sensors, running control logic and setting controls (default: 1000 ms).  
  - `LogLevel`: Logging level (e.g., "Information", "Debug").
  - `LHMConfig`: Configuration for Libre Hardware Monitor (LHM) sensor type groups.
  - `Controls`: List of fan/pump controls with their aliases, identifiers, and RPM sensors.
  - `Sensors`: List of sensors with their aliases and identifiers.
  - `Alias`: A user-friendly name for the sensor (e.g., "CPU Package").
  - `Identifier`: The hardware ID of the sensor (e.g., "/intelcpu/0/temperature/22").
  - `RPMSensor`: The RPM sensor ID for the fan/pump (e.g., "/lpc/nct6798d/0/fan/1").
  - `StepUp`: Maximum step up in % per update interval (default: 8%).
  - `StepDown`: Maximum step down in % per update interval (default: 8%).

4. **Calibrate fans and pumps**: Run `CoolingControl.exe calibrate all` to generate calibration data for all fans and pumps. This will update `config.json` with the calibration data.
5. **Edit cooling_control.lua**: Customize the Lua script for specific control logic. You can use the provided examples (cooling_control_aio_sample.lua, cooling_control_aircooling_sample.kua) or create your own. The script is executed every UpdateIntervalMs, and the `calculate_controls` function is called to determine the fan/pump speeds based on the sensor data. The script can use any sensor data available in the system that is define in `config.json`. You can specify the control value (fan/pump speed) in RPM or percentage. The script can also use the provided Lua library for common algorithms (e.g., exponential moving average, hysteresis, linear curve). Ramp up/down and min start/min stop logic is applied by the app framework, no need to handle it the control script.

**Example for AIO**:

```lua
-- Control script for AIO cooling system

local cf = require("config/cooling_functions")

-- Fan configuration (example, adjust as needed)
local case_gpu_fan_curve =  { { sensor_value = 80, control_value = 600 }, { sensor_value = 90, control_value = 1200 } } 

function on_resume()
    cf.on_resume()
end

function calculate_controls(sensors)
    local result = {}

    local cpu_power = sensors["CPU Power"] or 50
    
    -- Apply moving avarage
    cpu_power = cf.apply_ema("CPU Power", cpu_power)
    -- log_debug(string.format("EMA CPU Power %f",  cpu_power))
    
    -- Calc AIO control, silent profile: max fan rpm 1700, max pump rpm 2800
    local aio_res = cf.aio_control(cpu_power, 50, 253, 1700, 2800, 680, 1500)
    -- local aio_res = cf.aio_control(cpu_power, 50, 300, 1700, 3342, 680, 2000)
    local aio_pump_rpm = aio_res.pump_rpm
    local aio_fan_rpm = aio_res.fan_rpm
    -- log_debug(string.format("AIO Pump RPM %f AIO Fan RPM %f",  aio_pump_rpm, aio_fan_rpm))
    
    -- Apply hysteresis
    aio_pump_rpm = cf.apply_hysteresis("AIO Pump", aio_pump_rpm, cpu_power, 50, 253, 5, 20)
    aio_fan_rpm = cf.apply_hysteresis("AIO Fan", aio_fan_rpm, cpu_power, 50, 253, 5, 20)
    -- log_debug(string.format("Post Hyst AIO Pump RPM %f AIO Fan RPM %f",  aio_pump_rpm, aio_fan_rpm))

    table.insert(result, { alias = "AIO Pump", rpm = aio_pump_rpm })
    table.insert(result, { alias = "AIO Fan", rpm = aio_fan_rpm })  
    
    -- Case fan: Based on GPU temperature
    local gpu_temp = sensors["GPU Core"] or 50

    -- Apply moving avarage
    gpu_temp = cf.apply_ema("GPU Core", gpu_temp)

    -- Apply fan curve
    local case_fan_rpm = cf.apply_linear_curve(gpu_temp, case_gpu_fan_curve)
    -- log_debug(string.format("Case Fan RPM: %f", case_fan_rpm))

    -- Apply hysteresis
    case_fan_rpm = cf.apply_hysteresis("Case Fan", case_fan_rpm, gpu_temp, 50, 100, 4, 2)
    -- log_debug(string.format("Post Hyst Case Fan RPM: %f", case_fan_rpm))

    -- Mix with AIO fan 
    case_fan_rpm = math.max(aio_fan_rpm * 0.8, case_fan_rpm)
    -- Limit to 1200 RPM
    case_fan_rpm = math.min(case_fan_rpm, 1200)
    -- log_debug(string.format("Mixed Case Fan RPM: %f", case_fan_rpm))
    
    table.insert(result, { alias = "Case Fan", rpm = case_fan_rpm })

    return result
end
```

- Description of the functions:
  - `calculate_controls(sensors)`: Main function for calculating fan/pump speeds based on sensor data. The `sensors` parameter is a table containing the sensor values defined in `config.json`. The function returns a table with the calculated RPM values (rpm field) or percentage value (value field) for each control defined in `config.json`. Ramp up/down and min start/min stop logic is applied by the app framework, no need to handle it the control script.
  - `on_resume()`: Called when the system resumes from sleep. You can use this to reset any state.
  - `on_suspend()`: Called when the system is about to suspend. You can use this to reset any state.
- Description of the functions in the Lua library `cooling_functions.lua`:
  - `cf.on_resume()`: A function that should be called when the system resumes from sleep.
  - `cf.apply_ema()`: A function that applies exponential moving average to smooth out sensor readings.
  - `cf.apply_linear_curve()`: A function that applies a linear curve to map sensor values to fan/pump speeds based on the defined curve.
  - `cf.apply_hysteresis()`: A function that applies hysteresis logic to prevent rapid changes in fan/pump speeds based on sensor fluctuations.
  - `cf.aio_control()`: A function that calculates the fan and pump speeds based on the CPU power and other parameters. Limits for pump and fan speeds should be set according to noise preferences and AIO size.
  - `cf.log_debug()`: A function that logs debug messages to the log file. You can use this to log any information you need for debugging purposes.
  - `cf.log_info()`: A function that logs information messages to the log file. You can use this to log any information you need for debugging purposes.
  - `cf.log_error()`: A function that logs error messages to the log file. You can use this to log any information you need for debugging purposes.

## Usage

### Running in Console Mode

- Open Command Prompt (cmd.exe) or PowerShell with administrative priviliges.
- Change to root directory and execute it without any parameters

  ```cmd
  cd "C:\Program Files (x86)\CoolingControl"
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

## Configuration

### config.json

Located in the application directory (e.g., `C:\Program Files\CoolingControl\config`), `config.json` defines aliases, hardware IDs, calibration data, log level, and update intervals. User modifications to `config.json` are preserved during installation or reinstallation.

## License

This project is licensed under the GNU General Public License v3.0. See the [LICENSE](LICENSE) file for details.

## Contributing

- Submit issues or pull requests to the repository (if applicable).
- Share calibration data or Lua scripts for common hardware.

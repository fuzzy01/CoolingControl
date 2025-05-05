-- Control script for CoolingControl daemon

local cf = require("config/cooling_functions")

function on_resume()
    log_debug("On Resume")
    cf.on_resume()
end

function calculate_controls(sensors)
    local result = {}

    -- Use one of samples or write your own algorithm

    return result
end
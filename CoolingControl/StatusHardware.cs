namespace CoolingControl;

using System.Net;
using System.Text;
using System.Text.Json;
using CoolingControl.Platform;

public partial class StatusServer
{
    private void HandleApiConfig(HttpListenerResponse response)
    {
        var (sensors, controls) = _config.SnapshotEntries();

        var json = JsonSerializer.Serialize(new
        {
            sensors = sensors.Select(sensor => new
            {
                platform = sensor.Platform,
                identifier = sensor.Identifier,
                alias = sensor.Alias,
                alertMax = sensor.AlertMax
            }),
            controls = controls.Select(control => new
            {
                platform = control.Platform,
                identifier = control.Identifier,
                alias = control.Alias,
                stepUp = control.StepUp,
                stepDown = control.StepDown,
                minStart = control.MinStart,
                minStop = control.MinStop,
                zeroRpm = control.ZeroRPM,
                rpmSensor = control.RPMSensor,
                beatDetune = control.BeatDetune
            })
        });
        WriteJson(response, 200, json);
    }

    private void HandleApiHardware(HttpListenerResponse response)
    {
        var rows = _hardwareCatalog.WaitForRefresh(TimeSpan.FromSeconds(2));
        var json = JsonSerializer.Serialize(new
        {
            hardware = rows.Select(channel => new
            {
                platform = channel.Platform,
                identifier = channel.Identifier,
                name = channel.Name,
                hardware = channel.HardwareName,
                type = channel.SensorType,
                isControl = channel.IsControl
            })
        });
        WriteJson(response, 200, json);
    }

    private void HandleHardwarePage(HttpListenerResponse response)
    {
        var buffer = Encoding.UTF8.GetBytes(HardwarePageHtml);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Flush();
        response.Close();
    }

    private void HandleHardwareEntry(HttpListenerContext context, string collection, string? alias)
    {
        var request = context.Request;
        var response = context.Response;
        if (!request.IsLocal)
        {
            response.StatusCode = 403;
            response.Close();
            return;
        }

        var method = request.HttpMethod;
        try
        {
            if (collection == "sensors" && method == "POST" && alias == null)
            {
                var sensor = ReadSensor(request);
                HardwareSelection.EnsureSensorChannel(_hardwareCatalog.Rows, sensor.Platform, sensor.Identifier);
                _config.AddSensor(sensor);
                WriteJson(response, 200, JsonSerializer.Serialize(new { alias = sensor.Alias, identifier = sensor.Identifier }));
                return;
            }

            if (collection == "controls" && method == "POST" && alias == null)
            {
                var control = ReadControl(request);
                HardwareSelection.EnsureControlChannel(_hardwareCatalog.Rows, control.Platform, control.Identifier);
                HardwareSelection.EnsureFanChannel(_hardwareCatalog.Rows, control.Platform, control.RPMSensor);
                _config.AddControl(control);
                WriteJson(response, 200, JsonSerializer.Serialize(new { alias = control.Alias, identifier = control.Identifier }));
                return;
            }

            if (collection == "sensors" && method == "PUT" && alias != null)
            {
                var sensor = ReadSensor(request);
                _config.UpdateSensor(alias, sensor);
                WriteJson(response, 200, JsonSerializer.Serialize(new { alias = sensor.Alias }));
                return;
            }

            if (collection == "controls" && method == "PUT" && alias != null)
            {
                var control = ReadControl(request);
                var (_, controls) = _config.SnapshotEntries();
                var existing = controls.FirstOrDefault(item => item.Alias == alias);
                var platform = existing?.Platform ?? control.Platform;
                if (!string.IsNullOrEmpty(control.RPMSensor) && _hardwareCatalog.Rows.Count > 0)
                    HardwareSelection.EnsureFanChannel(_hardwareCatalog.Rows, platform, control.RPMSensor);
                _config.UpdateControl(alias, control);
                WriteJson(response, 200, JsonSerializer.Serialize(new { alias = control.Alias }));
                return;
            }

            if (collection == "sensors" && method == "DELETE" && alias != null)
            {
                _config.DeleteSensor(alias);
                WriteJson(response, 200, JsonSerializer.Serialize(new { alias }));
                return;
            }

            if (collection == "controls" && method == "DELETE" && alias != null)
            {
                _config.DeleteControl(alias);
                WriteJson(response, 200, JsonSerializer.Serialize(new { alias }));
                return;
            }

            response.StatusCode = 405;
            response.Close();
        }
        catch (ArgumentException)
        {
            response.StatusCode = 400;
            response.Close();
        }
        catch (JsonException)
        {
            response.StatusCode = 400;
            response.Close();
        }
    }

    private void HandleReorder(HttpListenerContext context, string collection)
    {
        var request = context.Request;
        var response = context.Response;
        if (!request.IsLocal)
        {
            response.StatusCode = 403;
            response.Close();
            return;
        }

        if (!string.Equals(request.HttpMethod, "PUT", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 405;
            response.Close();
            return;
        }

        try
        {
            var aliases = ReadAliases(request);
            if (collection == "sensors")
                _config.ReorderSensors(aliases);
            else
                _config.ReorderControls(aliases);
            WriteJson(response, 200, JsonSerializer.Serialize(new { aliases }));
        }
        catch (ArgumentException)
        {
            response.StatusCode = 400;
            response.Close();
        }
        catch (JsonException)
        {
            response.StatusCode = 400;
            response.Close();
        }
    }

    private static List<string> ReadAliases(HttpListenerRequest request)
    {
        using var doc = ReadJson(request);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (!property.Name.Equals("aliases", StringComparison.OrdinalIgnoreCase)
                || property.Value.ValueKind != JsonValueKind.Array)
                continue;

            var aliases = new List<string>();
            foreach (var item in property.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    throw new JsonException("Alias must be a string.");
                aliases.Add(item.GetString() ?? "");
            }
            return aliases;
        }

        throw new JsonException("Missing aliases.");
    }

    private static bool TryOrderRoute(string path, out string collection)
    {
        if (path.Equals("/api/sensors/order", StringComparison.OrdinalIgnoreCase))
        {
            collection = "sensors";
            return true;
        }

        if (path.Equals("/api/controls/order", StringComparison.OrdinalIgnoreCase))
        {
            collection = "controls";
            return true;
        }

        collection = "";
        return false;
    }

    private static bool TryHardwareRoute(string path, out string collection, out string? alias)
    {
        collection = "";
        alias = null;
        foreach (var name in new[] { "sensors", "controls" })
        {
            var root = "/api/" + name;
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                collection = name;
                return true;
            }

            var prefix = root + "/";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            alias = Uri.UnescapeDataString(path[prefix.Length..]);
            if (alias.Length == 0)
                return false;
            collection = name;
            return true;
        }

        return false;
    }

    private static SensorConfig ReadSensor(HttpListenerRequest request)
    {
        using var doc = ReadJson(request);
        var root = doc.RootElement;
        return new SensorConfig
        {
            Platform = RequiredString(root, "platform"),
            Identifier = RequiredString(root, "identifier"),
            Alias = RequiredString(root, "alias"),
            AlertMax = OptionalFloat(root, "alertMax")
        };
    }

    private static ControlConfig ReadControl(HttpListenerRequest request)
    {
        using var doc = ReadJson(request);
        var root = doc.RootElement;
        return new ControlConfig
        {
            Platform = RequiredString(root, "platform"),
            Identifier = RequiredString(root, "identifier"),
            Alias = RequiredString(root, "alias"),
            StepUp = OptionalFloat(root, "stepUp") ?? 8f,
            StepDown = OptionalFloat(root, "stepDown") ?? 8f,
            MinStart = OptionalFloat(root, "minStart") ?? 20f,
            MinStop = OptionalFloat(root, "minStop") ?? 20f,
            ZeroRPM = OptionalBool(root, "zeroRpm"),
            RPMSensor = OptionalString(root, "rpmSensor") ?? "",
            BeatDetune = OptionalBool(root, "beatDetune")
        };
    }

    private static JsonDocument ReadJson(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        return JsonDocument.Parse(reader.ReadToEnd());
    }

    private static string RequiredString(JsonElement root, string name)
    {
        var value = OptionalString(root, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new JsonException($"Missing {name}.");
        return value;
    }

    private static string? OptionalString(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
        }

        return null;
    }

    private static float? OptionalFloat(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetSingle(out var number))
                return number;
            if (property.Value.ValueKind == JsonValueKind.Null)
                return null;
        }

        return null;
    }

    private static bool OptionalBool(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (property.Value.ValueKind == JsonValueKind.True)
                return true;
            if (property.Value.ValueKind == JsonValueKind.False)
                return false;
        }

        return false;
    }

    private static void WriteJson(HttpListenerResponse response, int statusCode, string json)
    {
        var buffer = Encoding.UTF8.GetBytes(json);
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Flush();
        response.Close();
    }

    private const string HardwarePageHtml = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>CoolingControl Hardware config page</title>
            <style>
                body { font-family: 'Segoe UI', sans-serif; background: #f0f2f5; margin: 0; color: #333; }
                .container { max-width: 1680px; margin: 0 auto; padding: 20px; }
                a { color: #4c51bf; }
                h1 { margin-bottom: 4px; }
                .note { color: #666; }
                .card { background: white; border-radius: 8px; padding: 16px; margin: 16px 0; box-shadow: 0 1px 4px rgba(0,0,0,0.08); overflow-x: auto; }
                table { width: 100%; border-collapse: collapse; }
                th, td { text-align: left; padding: 8px; border-bottom: 1px solid #eee; vertical-align: top; white-space: nowrap; }
                button { margin-right: 6px; }
                #add-sensor, #add-control { margin-top: 16px; }
                label { display: block; margin: 8px 0 2px; font-size: 13px; color: #555; }
                input, select { padding: 4px 6px; }
                .message { margin-top: 12px; font-weight: 600; }
            </style>
        </head>
        <body>
            <div class="container">
                <p><a href="/">Status</a></p>
                <h1>Hardware config</h1>
                <p class="note">Changes are saved immediately. Restart the service before a new entry is read or driven.</p>
                <div id="message" class="message"></div>
                <div class="card">
                    <h2>Sensors</h2>
                    <div id="sensors"></div>
                    <button id="add-sensor" type="button">Add sensor</button>
                </div>
                <div id="sensor-picker"></div>
                <div class="card">
                    <h2>Controls</h2>
                    <div id="controls"></div>
                    <button id="add-control" type="button">Add control</button>
                </div>
                <div id="control-picker"></div>
            </div>
            <script>
                let config = { sensors: [], controls: [] };
                let catalog = null;

                function text(value) {
                    return value === null || value === undefined || value === '' ? '' : String(value);
                }

                function escapeHtml(value) {
                    return text(value).replace(/[&<>"']/g, function (ch) {
                        return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[ch];
                    });
                }

                async function loadConfig() {
                    const response = await fetch('/api/config');
                    config = await response.json();
                    render();
                }

                function setForm(kind, html) {
                    document.getElementById('sensor-picker').innerHTML = '';
                    document.getElementById('control-picker').innerHTML = '';
                    document.getElementById(kind === 'controls' ? 'control-picker' : 'sensor-picker').innerHTML = html;
                }

                function clearForms() {
                    document.getElementById('sensor-picker').innerHTML = '';
                    document.getElementById('control-picker').innerHTML = '';
                }

                function render() {
                    document.getElementById('sensors').innerHTML = sensorTable(config.sensors || []);
                    document.getElementById('controls').innerHTML = controlTable(config.controls || []);
                }

                function moveButtons(kind, alias, index, count) {
                    const up = index > 0 ? '' : ' disabled';
                    const down = index < count - 1 ? '' : ' disabled';
                    return '<button type="button" data-move-' + kind + '="up" data-alias="' + escapeHtml(alias) + '" aria-label="Move up"' + up + '>↑</button>' +
                        '<button type="button" data-move-' + kind + '="down" data-alias="' + escapeHtml(alias) + '" aria-label="Move down"' + down + '>↓</button>';
                }

                function sensorTable(sensors) {
                    if (sensors.length === 0) return '<p>No sensors configured.</p>';
                    const rows = sensors.map(function (sensor, index) {
                        return '<tr><td>' + escapeHtml(sensor.alias) + '</td><td>' + escapeHtml(sensor.platform) +
                            '</td><td>' + escapeHtml(sensor.identifier) + '</td><td>' + escapeHtml(sensor.alertMax) +
                            '</td><td>' + moveButtons('sensor', sensor.alias, index, sensors.length) +
                            '<button type="button" data-edit-sensor="' + escapeHtml(sensor.alias) +
                            '">Edit</button><button type="button" data-delete-sensor="' + escapeHtml(sensor.alias) +
                            '">Delete</button></td></tr>';
                    }).join('');
                    return '<table><tr><th>Alias</th><th>Platform</th><th>Identifier</th><th>Alert max</th><th></th></tr>' + rows + '</table>';
                }

                function controlTable(controls) {
                    if (controls.length === 0) return '<p>No controls configured.</p>';
                    const rows = controls.map(function (control, index) {
                        return '<tr><td>' + escapeHtml(control.alias) + '</td><td>' + escapeHtml(control.platform) +
                            '</td><td>' + escapeHtml(control.identifier) + '</td><td>' + escapeHtml(control.stepUp) +
                            '</td><td>' + escapeHtml(control.stepDown) + '</td><td>' + escapeHtml(control.minStart) +
                            '</td><td>' + escapeHtml(control.minStop) + '</td><td>' + (control.zeroRpm ? 'yes' : 'no') +
                            '</td><td>' + escapeHtml(control.rpmSensor) + '</td><td>' + (control.beatDetune ? 'yes' : 'no') +
                            '</td><td>' + moveButtons('control', control.alias, index, controls.length) +
                            '<button type="button" data-edit-control="' + escapeHtml(control.alias) +
                            '">Edit</button><button type="button" data-delete-control="' + escapeHtml(control.alias) +
                            '">Delete</button></td></tr>';
                    }).join('');
                    return '<table><tr><th>Alias</th><th>Platform</th><th>Identifier</th><th>Step up</th><th>Step down</th><th>Min start</th><th>Min stop</th><th>Zero RPM</th><th>RPM sensor</th><th>Beat detune</th><th></th></tr>' + rows + '</table>';
                }

                async function ensureCatalog() {
                    if (catalog && catalog.length > 0) return catalog;
                    const response = await fetch('/api/hardware');
                    const data = await response.json();
                    const hardware = data.hardware || [];
                    if (hardware.length === 0) return [];
                    catalog = hardware;
                    return catalog;
                }

                function taken(list, alias) {
                    var suffix = 2;
                    var candidate = alias;
                    const names = list.map(function (item) { return item.alias; });
                    while (names.indexOf(candidate) >= 0) {
                        candidate = alias + ' ' + suffix;
                        suffix++;
                    }
                    return candidate;
                }

                async function showAdd(kind) {
                    const channels = await ensureCatalog();
                    const wantControl = kind === 'controls';
                    const existing = (wantControl ? config.controls : config.sensors).map(function (item) {
                        return item.platform + '\n' + item.identifier;
                    });
                    const choices = channels.filter(function (channel) {
                        return channel.isControl === wantControl && existing.indexOf(channel.platform + '\n' + channel.identifier) < 0;
                    });
                    if (choices.length === 0) {
                        document.getElementById('message').textContent = !catalog || catalog.length === 0
                            ? 'Hardware list is not ready yet.'
                            : 'No unused channels of that kind.';
                        return;
                    }
                    const options = choices.map(function (channel, index) {
                        return '<option value="' + index + '">' + escapeHtml(channel.hardware + ' / ' + channel.name + ' (' + channel.type + ')') + '</option>';
                    }).join('');
                    setForm(kind,
                        '<div class="card"><h2>Add ' + (wantControl ? 'control' : 'sensor') + '</h2>' +
                        '<label>Channel</label><select id="channel">' + options + '</select>' +
                        '<div id="add-fields"></div><button type="button" id="save-add" style="display:block;margin-top:16px">Save</button></div>');
                    const select = document.getElementById('channel');
                    function drawFields() {
                        const channel = choices[Number(select.value)];
                        const alias = taken(wantControl ? config.controls : config.sensors, channel.name);
                        const fans = channels.filter(function (item) {
                            return item.platform === channel.platform && !item.isControl && String(item.type).toLowerCase() === 'fan';
                        });
                        const fanOptions = '<option value="">none</option>' + fans.map(function (fan) {
                            return '<option value="' + escapeHtml(fan.identifier) + '">' + escapeHtml(fan.name) + '</option>';
                        }).join('');
                        let fields = '<label>Alias</label><input id="alias" value="' + escapeHtml(alias) + '">';
                        if (!wantControl) {
                            fields += '<label>Alert max</label><input id="alert-max" placeholder="optional">';
                        } else {
                            fields += '<label>Step up</label><input id="step-up" value="8">' +
                                '<label>Step down</label><input id="step-down" value="8">' +
                                '<label>Min start</label><input id="min-start" value="20">' +
                                '<label>Min stop</label><input id="min-stop" value="20">' +
                                '<label><input id="zero-rpm" type="checkbox"> Zero RPM</label>' +
                                '<label>RPM sensor</label><select id="rpm-sensor">' + fanOptions + '</select>' +
                                '<label><input id="beat-detune" type="checkbox"> Beat detune</label>';
                        }
                        document.getElementById('add-fields').innerHTML = fields;
                    }
                    select.addEventListener('change', drawFields);
                    drawFields();
                    document.getElementById('save-add').addEventListener('click', async function () {
                        const channel = choices[Number(select.value)];
                        const body = {
                            platform: channel.platform,
                            identifier: channel.identifier,
                            alias: document.getElementById('alias').value
                        };
                        if (!wantControl) {
                            const alertMax = document.getElementById('alert-max').value;
                            body.alertMax = alertMax === '' ? null : Number(alertMax);
                        } else {
                            body.stepUp = Number(document.getElementById('step-up').value);
                            body.stepDown = Number(document.getElementById('step-down').value);
                            body.minStart = Number(document.getElementById('min-start').value);
                            body.minStop = Number(document.getElementById('min-stop').value);
                            body.zeroRpm = document.getElementById('zero-rpm').checked;
                            body.rpmSensor = document.getElementById('rpm-sensor').value;
                            body.beatDetune = document.getElementById('beat-detune').checked;
                        }
                        const response = await fetch('/api/' + kind, {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify(body)
                        });
                        clearForms();
                        document.getElementById('message').textContent = response.ok
                            ? 'Saved. Restart the service before this entry is read or driven.'
                            : 'Save failed.';
                        if (response.ok) await loadConfig();
                    });
                }

                function findSensor(alias) {
                    return (config.sensors || []).find(function (item) { return item.alias === alias; });
                }

                function findControl(alias) {
                    return (config.controls || []).find(function (item) { return item.alias === alias; });
                }

                async function editSensor(alias) {
                    const sensor = findSensor(alias);
                    if (!sensor) return;
                    setForm('sensors',
                        '<div class="card"><h2>Edit sensor</h2><p>' + escapeHtml(sensor.platform) + ' ' + escapeHtml(sensor.identifier) +
                        '</p><label>Alias</label><input id="alias" value="' + escapeHtml(sensor.alias) +
                        '"><label>Alert max</label><input id="alert-max" value="' + escapeHtml(sensor.alertMax) +
                        '"><button type="button" id="save-edit" style="display:block;margin-top:12px">Save</button></div>');
                    document.getElementById('save-edit').addEventListener('click', async function () {
                        const alertMax = document.getElementById('alert-max').value;
                        const response = await fetch('/api/sensors/' + encodeURIComponent(alias), {
                            method: 'PUT',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({
                                platform: sensor.platform,
                                identifier: sensor.identifier,
                                alias: document.getElementById('alias').value,
                                alertMax: alertMax === '' ? null : Number(alertMax)
                            })
                        });
                        clearForms();
                        document.getElementById('message').textContent = response.ok
                            ? 'Saved. Restart the service before this entry is read or driven.'
                            : 'Save failed.';
                        if (response.ok) await loadConfig();
                    });
                }

                async function editControl(alias) {
                    const control = findControl(alias);
                    if (!control) return;
                    await ensureCatalog();
                    const fans = (catalog || []).filter(function (item) {
                        return item.platform === control.platform && !item.isControl && String(item.type).toLowerCase() === 'fan';
                    });
                    const fanOptions = '<option value="">none</option>' + fans.map(function (fan) {
                        const selected = fan.identifier === control.rpmSensor ? ' selected' : '';
                        return '<option value="' + escapeHtml(fan.identifier) + '"' + selected + '>' + escapeHtml(fan.name) + '</option>';
                    }).join('');
                    setForm('controls',
                        '<div class="card"><h2>Edit control</h2><p>' + escapeHtml(control.platform) + ' ' + escapeHtml(control.identifier) +
                        '</p><label>Alias</label><input id="alias" value="' + escapeHtml(control.alias) +
                        '"><label>Step up</label><input id="step-up" value="' + escapeHtml(control.stepUp) +
                        '"><label>Step down</label><input id="step-down" value="' + escapeHtml(control.stepDown) +
                        '"><label>Min start</label><input id="min-start" value="' + escapeHtml(control.minStart) +
                        '"><label>Min stop</label><input id="min-stop" value="' + escapeHtml(control.minStop) +
                        '"><label><input id="zero-rpm" type="checkbox"' + (control.zeroRpm ? ' checked' : '') + '> Zero RPM</label>' +
                        '<label>RPM sensor</label><select id="rpm-sensor">' + fanOptions + '</select>' +
                        '<label><input id="beat-detune" type="checkbox"' + (control.beatDetune ? ' checked' : '') + '> Beat detune</label>' +
                        '<button type="button" id="save-edit" style="display:block;margin-top:16px">Save</button></div>');
                    document.getElementById('save-edit').addEventListener('click', async function () {
                        const response = await fetch('/api/controls/' + encodeURIComponent(alias), {
                            method: 'PUT',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({
                                platform: control.platform,
                                identifier: control.identifier,
                                alias: document.getElementById('alias').value,
                                stepUp: Number(document.getElementById('step-up').value),
                                stepDown: Number(document.getElementById('step-down').value),
                                minStart: Number(document.getElementById('min-start').value),
                                minStop: Number(document.getElementById('min-stop').value),
                                zeroRpm: document.getElementById('zero-rpm').checked,
                                rpmSensor: document.getElementById('rpm-sensor').value,
                                beatDetune: document.getElementById('beat-detune').checked
                            })
                        });
                        clearForms();
                        document.getElementById('message').textContent = response.ok
                            ? 'Saved. Restart the service before this entry is read or driven.'
                            : 'Save failed.';
                        if (response.ok) await loadConfig();
                    });
                }

                document.getElementById('add-sensor').addEventListener('click', function () { showAdd('sensors'); });
                document.getElementById('add-control').addEventListener('click', function () { showAdd('controls'); });
                document.body.addEventListener('click', function (event) {
                    const target = event.target;
                    if (target.dataset.moveSensor) moveEntry('sensors', target.dataset.alias, target.dataset.moveSensor);
                    if (target.dataset.editSensor) editSensor(target.dataset.editSensor);
                    if (target.dataset.deleteSensor) removeEntry('sensors', target.dataset.deleteSensor);
                    if (target.dataset.moveControl) moveEntry('controls', target.dataset.alias, target.dataset.moveControl);
                    if (target.dataset.editControl) editControl(target.dataset.editControl);
                    if (target.dataset.deleteControl) removeEntry('controls', target.dataset.deleteControl);
                });

                async function moveEntry(kind, alias, direction) {
                    const items = kind === 'sensors' ? config.sensors : config.controls;
                    const aliases = items.map(function (item) { return item.alias; });
                    const index = aliases.indexOf(alias);
                    const swap = direction === 'up' ? index - 1 : index + 1;
                    if (index < 0 || swap < 0 || swap >= aliases.length) return;
                    const moved = aliases[index];
                    aliases[index] = aliases[swap];
                    aliases[swap] = moved;
                    const response = await fetch('/api/' + kind + '/order', {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ aliases: aliases })
                    });
                    document.getElementById('message').textContent = response.ok
                        ? 'Saved. Restart the service before this order is used.'
                        : 'Reorder failed.';
                    if (response.ok) await loadConfig();
                }

                async function removeEntry(kind, alias) {
                    const response = await fetch('/api/' + kind + '/' + encodeURIComponent(alias), { method: 'DELETE' });
                    document.getElementById('message').textContent = response.ok
                        ? 'Deleted. Restart the service before this change takes effect.'
                        : 'Delete failed.';
                    if (response.ok) await loadConfig();
                }

                loadConfig();
            </script>
        </body>
        </html>
        """;
}

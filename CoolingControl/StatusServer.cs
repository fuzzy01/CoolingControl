namespace CoolingControl;

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Serilog;

public class StatusServer : IHostedService
{
    private readonly ConfigHelper _config;
    private readonly IStatusSnapshot _statusSnapshot;
    private HttpListener? _httpListener;
    private Task? _listenerTask;
    private CancellationTokenSource? _cts;

    public StatusServer(ConfigHelper config, IStatusSnapshot statusSnapshot)
    {
        _config = config;
        _statusSnapshot = statusSnapshot;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_config.Config.StatusServerEnabled)
        {
            Log.Information("Status server is disabled");
            return Task.CompletedTask;
        }

        _cts = new CancellationTokenSource();
        _httpListener = new HttpListener();
        var prefix = $"http://localhost:{_config.Config.StatusServerPort}/";
        _httpListener.Prefixes.Add(prefix);

        try
        {
            _httpListener.Start();
            Log.Information("Status server started on {Prefix}", prefix);
        }
        catch (HttpListenerException ex)
        {
            Log.Error(ex, "Failed to start status server on {Prefix}", prefix);
            return Task.CompletedTask;
        }

        _listenerTask = Task.Run(() => ListenLoop(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_httpListener == null) return Task.CompletedTask;

        _cts?.Cancel();
        _httpListener.Stop();
        _httpListener.Close();

        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch { }

        Log.Information("Status server stopped");
        return Task.CompletedTask;
    }

    private void ListenLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _httpListener?.IsListening == true)
        {
            try
            {
                var context = _httpListener.GetContext();
                _ = Task.Run(() => HandleRequest(context, cancellationToken), cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in status server listener loop");
            }
        }
    }

    private void HandleRequest(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var path = context.Request.Url?.LocalPath ?? "/";
            var response = context.Response;

            if (path == "/api/status")
            {
                HandleApiStatus(response);
            }
            else if (path == "/" || path == "")
            {
                HandleHtmlDashboard(response);
            }
            else
            {
                response.StatusCode = 404;
                response.Close();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling status server request");
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch { }
        }
    }

    private void HandleApiStatus(HttpListenerResponse response)
    {
        var (sensors, controls, lastUpdate) = _statusSnapshot.GetSnapshot();
        var uptime = DateTime.UtcNow - _statusSnapshot.StartTime;

        var data = new
        {
            uptime = FormatTimeSpan(uptime),
            lastUpdate = lastUpdate.ToString("O"),
            port = _config.Config.StatusServerPort,
            updateInterval = _config.Config.UpdateIntervalMs,
            scriptPath = _config.Config.ScriptPath,
            logLevel = _config.Config.LogLevel,
            sensors,
            controls
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        var buffer = Encoding.UTF8.GetBytes(json);

        response.ContentType = "application/json";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.Close();
    }

    private void HandleHtmlDashboard(HttpListenerResponse response)
    {
        var html = GenerateHtml();
        var buffer = Encoding.UTF8.GetBytes(html);

        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.Close();
    }

    private string GenerateHtml()
    {
        return """
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <title>CoolingControl Status</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: #f5f5f5; padding: 20px; }
        .container { max-width: 1200px; margin: 0 auto; background: white; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1); overflow: hidden; }
        .header { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px; }
        .header h1 { margin-bottom: 5px; font-size: 28px; }
        .header p { opacity: 0.9; font-size: 14px; }
        .content { display: grid; grid-template-columns: 1fr 1fr; gap: 30px; padding: 30px; }
        .section h2 { font-size: 18px; margin-bottom: 15px; color: #333; border-bottom: 2px solid #667eea; padding-bottom: 10px; }
        .metric { margin-bottom: 15px; padding: 12px; background: #f9f9f9; border-radius: 6px; border-left: 4px solid #667eea; }
        .metric-label { font-size: 12px; color: #666; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 4px; }
        .metric-value { font-size: 22px; font-weight: 600; color: #333; font-variant-numeric: tabular-nums; }
        .metric-unit { font-size: 14px; color: #999; margin-left: 4px; }
        .info { grid-column: 1 / -1; padding: 15px; background: #f0f4ff; border-radius: 6px; border-left: 4px solid #667eea; margin-top: 10px; }
        .info-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 15px; }
        .info-item { font-size: 13px; }
        .info-item-label { color: #666; }
        .info-item-value { color: #333; font-weight: 600; margin-top: 4px; }
        @media (max-width: 768px) { .content { grid-template-columns: 1fr; } }
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>CoolingControl Status</h1>
            <p id='connection-status'>Connecting...</p>
        </div>
        <div class='content' id='content'>
            <div><div class='section'><h2>Sensors</h2><p style='color:#999;'>Loading...</p></div></div>
            <div><div class='section'><h2>Controls</h2><p style='color:#999;'>Loading...</p></div></div>
        </div>
    </div>

    <script>
        async function updateStatus() {
            try {
                const resp = await fetch('/api/status');
                if (!resp.ok) throw new Error('Failed to fetch status');
                const data = await resp.json();
                renderDashboard(data);
                document.getElementById('connection-status').textContent = 'Connected';
                document.getElementById('connection-status').style.color = '#4ade80';
            } catch (err) {
                document.getElementById('connection-status').textContent = 'Connection failed';
                document.getElementById('connection-status').style.color = '#f87171';
            }
        }

        function renderDashboard(data) {
            const sensorsHtml = Object.entries(data.sensors).map(([alias, value]) => {
                const valueStr = value === null ? '—' : value.toFixed(2);
                const lowerAlias = alias.toLowerCase();
                let unit = '°C';
                if (lowerAlias.includes('fan') || lowerAlias.includes('rpm')) unit = 'RPM';
                else if (lowerAlias.includes('power')) unit = 'W';
                else if (lowerAlias.includes('load')) unit = '%';
                return '<div class="metric"><div class="metric-label">' + escapeHtml(alias) + '</div><div class="metric-value">' + valueStr + '<span class="metric-unit">' + unit + '</span></div></div>';
            }).join('');

            const controlsHtml = Object.entries(data.controls).map(([alias, value]) => {
                const valueStr = value.toFixed(2);
                return '<div class="metric"><div class="metric-label">' + escapeHtml(alias) + '</div><div class="metric-value">' + valueStr + '<span class="metric-unit">%</span></div></div>';
            }).join('');

            const contentHtml = '<div><div class="section"><h2>Sensors</h2>' + (sensorsHtml || '<p style="color:#999;">No sensors configured</p>') + '</div></div>' +
                                '<div><div class="section"><h2>Controls</h2>' + (controlsHtml || '<p style="color:#999;">No controls configured</p>') + '</div></div>' +
                                '<div class="info"><div class="info-grid">' +
                                '<div class="info-item"><div class="info-item-label">Uptime</div><div class="info-item-value">' + data.uptime + '</div></div>' +
                                '<div class="info-item"><div class="info-item-label">Last Update</div><div class="info-item-value">' + new Date(data.lastUpdate).toLocaleTimeString() + '</div></div>' +
                                '<div class="info-item"><div class="info-item-label">Update Interval</div><div class="info-item-value">' + data.updateInterval + ' ms</div></div>' +
                                '<div class="info-item"><div class="info-item-label">Script</div><div class="info-item-value" style="font-size:12px;">' + escapeHtml(data.scriptPath) + '</div></div>' +
                                '</div></div>';
            document.getElementById('content').innerHTML = contentHtml;
        }

        function escapeHtml(text) {
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }

        updateStatus();
        setInterval(updateStatus, 1000);
    </script>
</body>
</html>
""";
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
        return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
    }
}

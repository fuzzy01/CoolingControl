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

public class StatusServer : IHostedService, IDisposable
{
    private readonly ConfigHelper _config;
    private readonly IStatusSnapshot _statusSnapshot;
    private HttpListener? _httpListener;
    private Task? _listenerTask;
    private CancellationTokenSource? _cts;
    private bool _disposed;

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
        var prefix = $"http://{_config.Config.StatusServerBindAddress}:{_config.Config.StatusServerPort}/";
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

    public void Dispose()
    {
        if (_disposed) return;
        _cts?.Dispose();
        _disposed = true;
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
        var (sensorHistory, controlHistory) = _statusSnapshot.GetHistory();
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
            controls,
            history = new { sensors = sensorHistory, controls = controlHistory }
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        var buffer = Encoding.UTF8.GetBytes(json);

        response.ContentType = "application/json";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Flush();
        response.Close();
    }

    private void HandleHtmlDashboard(HttpListenerResponse response)
    {
        var html = GenerateHtml();
        var buffer = Encoding.UTF8.GetBytes(html);

        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Flush();
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
    <script src='https://cdn.jsdelivr.net/npm/chart.js'></script>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: #f5f5f5; padding: 20px; }
        .container { max-width: 1400px; margin: 0 auto; background: white; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1); overflow: hidden; }
        .header { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px; }
        .header h1 { margin-bottom: 5px; font-size: 28px; }
        .header p { opacity: 0.9; font-size: 14px; }
        .content { padding: 30px; }
        .section { margin-bottom: 40px; }
        .section h2 { font-size: 20px; margin-bottom: 20px; color: #333; border-bottom: 2px solid #667eea; padding-bottom: 10px; }
        .metrics-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 15px; margin-bottom: 20px; }
        .metric { padding: 12px; background: #f9f9f9; border-radius: 6px; border-left: 4px solid #667eea; }
        .metric-label { font-size: 12px; color: #666; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 4px; }
        .metric-value { font-size: 18px; font-weight: 600; color: #333; font-variant-numeric: tabular-nums; }
        .metric-unit { font-size: 13px; color: #999; margin-left: 2px; }
        .charts-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(400px, 1fr)); gap: 20px; }
        .chart-container { background: #fafafa; border-radius: 6px; padding: 15px; border: 1px solid #eee; position: relative; height: 250px; }
        .chart-title { font-size: 14px; font-weight: 600; color: #333; margin-bottom: 10px; }
        .info { padding: 15px; background: #f0f4ff; border-radius: 6px; border-left: 4px solid #667eea; margin-top: 20px; }
        .info-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 15px; }
        .info-item-label { font-size: 12px; color: #666; }
        .info-item-value { font-size: 13px; color: #333; font-weight: 600; margin-top: 4px; }
        @media (max-width: 768px) { .charts-grid { grid-template-columns: 1fr; } .chart-container { height: 200px; } }
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>CoolingControl Status</h1>
            <p id='connection-status'>Connecting...</p>
        </div>
        <div class='content' id='content'>
            <p style='color:#999;'>Loading...</p>
        </div>
    </div>

    <script>
        const charts = {};
        const chartColors = ['#667eea', '#764ba2', '#f093fb', '#4facfe', '#43e97b', '#fa7231', '#ff6b6b', '#ffd93d'];
        let initialized = false;
        let colorIndex = 0;

        function sanitizeId(text) {
            return text.replace(/[^a-z0-9-]/gi, '-');
        }

        async function updateStatus() {
            try {
                const resp = await fetch('/api/status');
                if (!resp.ok) throw new Error('Failed to fetch status');
                const data = await resp.json();
                if (!initialized) {
                    buildDashboard(data);
                    initialized = true;
                }
                updateMetrics(data);
                updateCharts(data);
                document.getElementById('connection-status').textContent = 'Connected';
                document.getElementById('connection-status').style.color = '#4ade80';
            } catch (err) {
                document.getElementById('connection-status').textContent = 'Connection failed';
                document.getElementById('connection-status').style.color = '#f87171';
            }
        }

        function getUnit(alias) {
            const lowerAlias = alias.toLowerCase();
            if (lowerAlias.includes('fan') || lowerAlias.includes('rpm')) return 'RPM';
            if (lowerAlias.includes('power')) return 'W';
            if (lowerAlias.includes('load')) return '%';
            return '°C';
        }

        function buildDashboard(data) {
            const chartsHtml = '<div class="charts-grid">' +
                Object.entries(data.history.sensors).map(([alias, _]) =>
                    '<div class="chart-container"><div class="chart-title">' + escapeHtml(alias) + ' (' + getUnit(alias) + ')</div><canvas id="chart-sensor-' + sanitizeId(alias) + '"></canvas></div>'
                ).join('') +
                Object.entries(data.history.controls).map(([alias, _]) =>
                    '<div class="chart-container"><div class="chart-title">' + escapeHtml(alias) + ' (%)</div><canvas id="chart-control-' + sanitizeId(alias) + '"></canvas></div>'
                ).join('') +
                '</div>';

            const contentHtml = '<div class="section"><h2>Current Values</h2><div class="metrics-grid" id="metrics-grid"></div></div>' +
                '<div class="section"><h2>Trends (5 Minutes)</h2>' + chartsHtml + '</div>' +
                '<div class="info"><div class="info-grid"><div class="info-item-label">Uptime</div><div class="info-item-value" id="uptime"></div>' +
                '<div class="info-item-label">Last Update</div><div class="info-item-value" id="last-update"></div>' +
                '<div class="info-item-label">Update Interval</div><div class="info-item-value" id="update-interval"></div>' +
                '<div class="info-item-label">Script</div><div class="info-item-value" id="script-path"></div></div></div>';

            document.getElementById('content').innerHTML = contentHtml;
        }

        function updateMetrics(data) {
            const metricsGrid = document.getElementById('metrics-grid');
            if (!metricsGrid) return;

            const sensorsHtml = Object.entries(data.sensors).map(([alias, value]) => {
                const valueStr = value === null ? '—' : value.toFixed(1);
                const unit = getUnit(alias);
                return '<div class="metric"><div class="metric-label">' + escapeHtml(alias) + '</div><div class="metric-value">' + valueStr + '<span class="metric-unit">' + unit + '</span></div></div>';
            }).join('');

            const controlsHtml = Object.entries(data.controls).map(([alias, value]) => {
                const valueStr = value.toFixed(1);
                return '<div class="metric"><div class="metric-label">' + escapeHtml(alias) + '</div><div class="metric-value">' + valueStr + '<span class="metric-unit">%</span></div></div>';
            }).join('');

            metricsGrid.innerHTML = (sensorsHtml || '<p style="color:#999;">No sensors</p>') + (controlsHtml || '<p style="color:#999;">No controls</p>');

            const lastUpdateEl = document.getElementById('last-update');
            if (lastUpdateEl) lastUpdateEl.textContent = new Date(data.lastUpdate).toLocaleTimeString();
            const uptimeEl = document.getElementById('uptime');
            if (uptimeEl) uptimeEl.textContent = data.uptime;
            const intervalEl = document.getElementById('update-interval');
            if (intervalEl) intervalEl.textContent = data.updateInterval + ' ms';
            const scriptEl = document.getElementById('script-path');
            if (scriptEl) scriptEl.textContent = data.scriptPath;
        }

        function updateCharts(data) {
            Object.entries(data.history.sensors).forEach(([alias, history]) => {
                const canvasId = 'chart-sensor-' + sanitizeId(alias);
                const canvas = document.getElementById(canvasId);
                if (!canvas) return;

                const chartId = 'sensor-' + alias;
                const labels = Array.from({length: history.length}, (_, i) => i - history.length + 1);

                if (charts[chartId]) {
                    charts[chartId].data.labels = labels;
                    charts[chartId].data.datasets[0].data = history;
                    charts[chartId].update('none');
                } else {
                    charts[chartId] = new Chart(canvas.getContext('2d'), {
                        type: 'line',
                        data: {
                            labels: labels,
                            datasets: [{
                                label: alias,
                                data: history,
                                borderColor: chartColors[colorIndex % chartColors.length],
                                backgroundColor: chartColors[colorIndex % chartColors.length] + '15',
                                borderWidth: 2,
                                tension: 0.4,
                                pointRadius: 0,
                                pointHoverRadius: 4,
                                fill: true
                            }]
                        },
                        options: {
                            responsive: true,
                            maintainAspectRatio: false,
                            plugins: { legend: { display: false } },
                            scales: { x: { display: false }, y: { beginAtZero: false } }
                        }
                    });
                    colorIndex++;
                }
            });

            Object.entries(data.history.controls).forEach(([alias, history]) => {
                const canvasId = 'chart-control-' + sanitizeId(alias);
                const canvas = document.getElementById(canvasId);
                if (!canvas) return;

                const chartId = 'control-' + alias;
                const labels = Array.from({length: history.length}, (_, i) => i - history.length + 1);

                if (charts[chartId]) {
                    charts[chartId].data.labels = labels;
                    charts[chartId].data.datasets[0].data = history;
                    charts[chartId].update('none');
                } else {
                    charts[chartId] = new Chart(canvas.getContext('2d'), {
                        type: 'line',
                        data: {
                            labels: labels,
                            datasets: [{
                                label: alias,
                                data: history,
                                borderColor: chartColors[colorIndex % chartColors.length],
                                backgroundColor: chartColors[colorIndex % chartColors.length] + '15',
                                borderWidth: 2,
                                tension: 0.4,
                                pointRadius: 0,
                                pointHoverRadius: 4,
                                fill: true
                            }]
                        },
                        options: {
                            responsive: true,
                            maintainAspectRatio: false,
                            plugins: { legend: { display: false } },
                            scales: { x: { display: false }, y: { min: 0, max: 100 } }
                        }
                    });
                    colorIndex++;
                }
            });
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

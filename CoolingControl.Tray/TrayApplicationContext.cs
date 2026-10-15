using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CoolingControl.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int UpdateFirstDelayMs = 15_000;
    private const int UpdateIntervalMs = 6 * 60 * 60 * 1000;

    private readonly NotifyIcon _icon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly HttpClient _updateHttp = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly StatusEndpoint _endpoint;
    private readonly ShutdownWindow _shutdownWindow;
    private int _pollInFlight;
    private int _updateCheckInFlight;
    private int _downloadInFlight;
    private int _exiting;
    private bool _downloading;
    private bool _restarting;
    private int _restartGeneration;
    private bool _updateMenuPending;
    private UpdateOffer? _updateOffer;
    private string? _notifiedVersion;
    private TrayMenuModel? _displayed;
    private TrayMenuModel? _pending;

    public TrayApplicationContext()
    {
        _updateHttp.DefaultRequestHeaders.UserAgent.ParseAdd("CoolingControl");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config", "config.json");
        _endpoint = StatusEndpointConfig.Read(configPath);
        _shutdownWindow = new ShutdownWindow();
        _shutdownWindow.ExitRequested += (_, _) => ExitTray();
        _ = _shutdownWindow.Handle;

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = "CoolingControl"
        };
        _icon.DoubleClick += (_, _) => OpenDashboard();

        var initial = _endpoint.Enabled
            ? TrayMenuBuilder.ServiceNotRunning()
            : TrayMenuBuilder.StatusServerDisabled();
        Apply(initial);

        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        if (_endpoint.Enabled)
        {
            _timer.Tick += async (_, _) => await PollAsync();
            _timer.Start();
        }

        _updateTimer = new System.Windows.Forms.Timer { Interval = UpdateFirstDelayMs };
        _updateTimer.Tick += OnUpdateTick;
        _updateTimer.Start();
    }

    private async Task PollAsync()
    {
        if (Interlocked.CompareExchange(ref _pollInFlight, 1, 0) != 0)
            return;

        try
        {
            var json = await _http.GetStringAsync(_endpoint.BaseUrl + "api/status");
            ClearRestarting();
            Apply(TrayMenuBuilder.FromJson(json));
        }
        catch (Exception)
        {
            Apply(TrayMenuBuilder.ServiceNotRunning());
        }
        finally
        {
            Interlocked.Exchange(ref _pollInFlight, 0);
        }
    }

    private void Apply(TrayMenuModel model)
    {
        WriteTooltip(model);
        NotifyAlerts(model.AlertKeys, model.AlertLines);

        if (_icon.ContextMenuStrip is { Visible: true } menu)
        {
            if (_displayed != null && TrayMenuBuilder.SameShape(_displayed, model))
            {
                UpdateOpenMenu(menu, model);
                _displayed = model;
                _pending = null;
            }
            else
            {
                _pending = model;
            }
            return;
        }

        Rebuild(model);
    }

    private void WriteTooltip(TrayMenuModel model)
    {
        var text = model.Tooltip;
        if (text.Length > TrayMenuBuilder.MaxTooltipLength)
            text = text[..TrayMenuBuilder.MaxTooltipLength];
        if (text.Length == 0)
            text = "CoolingControl";
        _icon.Text = text;
    }

    private void Rebuild(TrayMenuModel model)
    {
        var menu = BuildMenu(model);
        menu.Closed += OnMenuClosed;
        var previous = _icon.ContextMenuStrip;
        _displayed = model;
        _pending = null;
        _updateMenuPending = false;
        _icon.ContextMenuStrip = menu;
        previous?.Dispose();
    }

    private void OnMenuClosed(object? sender, ToolStripDropDownClosedEventArgs e)
    {
        if (_exiting != 0)
            return;

        if (_pending != null)
        {
            var pending = _pending;
            _pending = null;
            _updateMenuPending = false;
            _shutdownWindow.BeginInvoke(new Action(() => Rebuild(pending)));
            return;
        }

        if (!_updateMenuPending || _displayed == null)
            return;

        _updateMenuPending = false;
        var displayed = _displayed;
        _shutdownWindow.BeginInvoke(new Action(() => Rebuild(displayed)));
    }

    private static void UpdateOpenMenu(ContextMenuStrip menu, TrayMenuModel model)
    {
        var alerts = menu.Items.OfType<ToolStripMenuItem>().Where(item => Equals(item.Tag, AlertTag)).ToList();
        var health = menu.Items.OfType<ToolStripMenuItem>().Where(item => Equals(item.Tag, HealthTag)).ToList();
        var sensors = menu.Items.OfType<ToolStripMenuItem>().Where(item => Equals(item.Tag, SensorTag)).ToList();
        var controls = menu.Items.OfType<ToolStripMenuItem>().Where(item => Equals(item.Tag, ControlTag)).ToList();
        for (var i = 0; i < model.AlertLines.Count; i++)
            alerts[i].Text = EscapeAmpersand(model.AlertLines[i]);
        for (var i = 0; i < model.HealthLines.Count; i++)
            health[i].Text = EscapeAmpersand(model.HealthLines[i]);
        for (var i = 0; i < model.SensorLines.Count; i++)
            sensors[i].Text = EscapeAmpersand(model.SensorLines[i]);
        for (var i = 0; i < model.ControlLines.Count; i++)
            controls[i].Text = EscapeAmpersand(model.ControlLines[i]);

        var profile = menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(item => item.Text == "Profile");
        if (profile != null)
        {
            var choices = profile.DropDownItems.OfType<ToolStripMenuItem>().ToList();
            for (var i = 0; i < model.Profiles.Count; i++)
                choices[i].Checked = model.Profiles[i].IsChecked;
        }

        menu.PerformLayout();
    }

    private ContextMenuStrip BuildMenu(TrayMenuModel model)
    {
        var menu = new ContextMenuStrip();
        foreach (var line in model.AlertLines)
            menu.Items.Add(DisabledItem(line, AlertTag));
        if (model.AlertLines.Count > 0)
            menu.Items.Add(new ToolStripSeparator());
        foreach (var line in model.HealthLines)
            menu.Items.Add(DisabledItem(line, HealthTag));
        if (model.HealthLines.Count > 0 && (model.SensorLines.Count > 0 || model.ControlLines.Count > 0))
            menu.Items.Add(new ToolStripSeparator());
        foreach (var line in model.SensorLines)
            menu.Items.Add(DisabledItem(line, SensorTag));
        foreach (var line in model.ControlLines)
            menu.Items.Add(DisabledItem(line, ControlTag));

        if (model.Profiles.Count > 0)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(new ToolStripSeparator());

            var profile = new ToolStripMenuItem("Profile");
            foreach (var choice in model.Profiles)
            {
                var item = new ToolStripMenuItem(EscapeAmpersand(choice.Name))
                {
                    Checked = choice.IsChecked,
                    CheckOnClick = false
                };
                var name = choice.Name;
                item.Click += async (_, _) => await SelectProfileAsync(name);
                profile.DropDownItems.Add(item);
            }
            menu.Items.Add(profile);
        }

        if (menu.Items.Count > 0)
            menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open dashboard", null, (_, _) => OpenDashboard()));
        AppendRestartItem(menu);
        AppendUpdateItem(menu);
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitTray()));
        return menu;
    }

    private void AppendRestartItem(ContextMenuStrip menu)
    {
        var text = _restarting ? ServiceRestart.RestartingText : ServiceRestart.MenuText;
        var item = new ToolStripMenuItem(EscapeAmpersand(text)) { Enabled = !_restarting };
        if (!_restarting)
            item.Click += (_, _) => RestartService();
        menu.Items.Add(item);
    }

    private void RestartService()
    {
        if (_restarting || _exiting != 0)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(ServiceRestart.FileName)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = ServiceRestart.Arguments,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return;
        }
        catch (Exception ex)
        {
            Trace.TraceError("Service restart failed: {0}", ex);
            ShowRestartToast(ServiceRestart.FailureMessage);
            return;
        }

        _restarting = true;
        var generation = ++_restartGeneration;
        if (_exiting == 0)
            _shutdownWindow.BeginInvoke(new Action(RefreshUpdateMenu));
        _ = ExpireRestartAsync(generation);
    }

    private async Task ExpireRestartAsync(int generation)
    {
        await Task.Delay(TimeSpan.FromSeconds(60));
        if (_exiting != 0)
            return;

        try
        {
            _shutdownWindow.BeginInvoke(new Action(() =>
            {
                if (_exiting != 0 || generation != _restartGeneration || !_restarting)
                    return;
                _restarting = false;
                RefreshUpdateMenu();
            }));
        }
        catch (Exception ex)
        {
            Trace.TraceError("Service restart timer failed: {0}", ex);
        }
    }

    private void ClearRestarting()
    {
        if (!_restarting)
            return;
        _restarting = false;
        _restartGeneration++;
        if (_icon.ContextMenuStrip is { Visible: true })
            _updateMenuPending = true;
    }

    private static void ShowRestartToast(string message)
    {
        try
        {
            AlertToast.ShowRestart(message);
        }
        catch (Exception ex)
        {
            Trace.TraceError("Restart toast failed: {0}", ex);
        }
    }

    private void AppendUpdateItem(ContextMenuStrip menu)
    {
        var offer = _updateOffer;
        if (offer == null)
            return;

        var text = _downloading
            ? $"Downloading {offer.Version}…"
            : $"Update to {offer.Version}";
        var item = new ToolStripMenuItem(EscapeAmpersand(text)) { Enabled = !_downloading };
        if (!_downloading)
            item.Click += (_, _) => _ = DownloadAndInstallAsync();
        menu.Items.Add(item);
    }

    private async Task SelectProfileAsync(string name)
    {
        try
        {
            var json = JsonSerializer.Serialize(new { name });
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(_endpoint.BaseUrl + "api/profile", content);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception)
        {
            // The next poll shows the profile the service still has.
        }

        await PollAsync();
    }

    private void OpenDashboard()
    {
        Process.Start(new ProcessStartInfo(_endpoint.BaseUrl) { UseShellExecute = true });
    }

    private void ExitTray()
    {
        if (Interlocked.Exchange(ref _exiting, 1) != 0)
            return;

        _timer.Stop();
        _updateTimer.Stop();
        _timer.Dispose();
        _updateTimer.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _http.Dispose();
        _updateHttp.Dispose();
        ExitThread();
    }

    private sealed class ShutdownWindow : Form
    {
        public event EventHandler? ExitRequested;

        public ShutdownWindow()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
        }

        protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

        protected override void WndProc(ref Message m)
        {
            const int WM_QUERYENDSESSION = 0x0011;
            const int WM_ENDSESSION = 0x0016;
            if (m.Msg == WM_QUERYENDSESSION)
            {
                m.Result = (IntPtr)1;
                return;
            }

            if (m.Msg == WM_ENDSESSION && m.WParam != IntPtr.Zero)
            {
                ExitRequested?.Invoke(this, EventArgs.Empty);
                m.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref m);
        }
    }

    private const string HealthTag = "health";
    private const string AlertTag = "alert";
    private HashSet<string>? _previousAlerts;

    private void NotifyAlerts(IReadOnlyList<string> keys, IReadOnlyList<string> messages)
    {
        var current = new HashSet<string>(keys, StringComparer.Ordinal);
        var fresh = new List<string>();
        if (_previousAlerts != null)
        {
            for (var i = 0; i < keys.Count; i++)
            {
                if (!_previousAlerts.Contains(keys[i]))
                    fresh.Add(messages[i]);
            }
        }
        else
        {
            fresh.AddRange(messages);
        }

        if (fresh.Count > 0)
            ShowAlertToast(string.Join("\n", fresh));

        _previousAlerts = current;
    }

    private static void ShowAlertToast(string message)
    {
        try
        {
            AlertToast.Show(message);
        }
        catch (Exception ex)
        {
            Trace.TraceError("Alert toast failed: {0}", ex);
        }
    }

    private async void OnUpdateTick(object? sender, EventArgs e)
    {
        try
        {
            if (_updateTimer.Interval != UpdateIntervalMs)
                _updateTimer.Interval = UpdateIntervalMs;
            await CheckForUpdateAsync();
        }
        catch (Exception ex)
        {
            Trace.TraceError("Update check failed: {0}", ex);
        }
    }

    private async Task CheckForUpdateAsync()
    {
        if (_exiting != 0 || _downloading)
            return;
        if (Interlocked.CompareExchange(ref _updateCheckInFlight, 1, 0) != 0)
            return;

        try
        {
            var installed = InstalledApp.ReadVersion(Path.Combine(AppContext.BaseDirectory, "CoolingControl.exe"));
            var suffix = InstalledRuntime.ReadAssetSuffix(
                Path.Combine(AppContext.BaseDirectory, "CoolingControlTray.runtimeconfig.json"));
            if (installed == null || suffix == null)
                return;

            string json;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                json = await _updateHttp.GetStringAsync(UpdateSource.LatestReleaseUrl, timeout.Token);
            }
            catch (Exception ex)
            {
                Trace.TraceError("Update check failed: {0}", ex);
                return;
            }

            if (!UpdateRelease.TryParse(json, suffix, out var offer))
                return;

            if (offer == null || !UpdateVersions.IsNewer(installed.ToString(), offer.Version))
            {
                SetOffer(null);
                return;
            }

            SetOffer(offer);
        }
        finally
        {
            Interlocked.Exchange(ref _updateCheckInFlight, 0);
        }
    }

    private void SetOffer(UpdateOffer? offer)
    {
        var previous = _updateOffer;
        _updateOffer = offer;
        if (offer != null && !string.Equals(_notifiedVersion, offer.Version, StringComparison.Ordinal))
        {
            _notifiedVersion = offer.Version;
            ShowUpdateToast(
                $"CoolingControl {offer.Version} is available. Choose Update on the tray menu to install it.");
        }

        if (previous == offer || _exiting != 0)
            return;

        RefreshUpdateMenu();
    }

    private void RefreshUpdateMenu()
    {
        if (_exiting != 0 || _displayed == null)
            return;

        if (_icon.ContextMenuStrip is { Visible: true })
        {
            _updateMenuPending = true;
            return;
        }

        Rebuild(_displayed);
    }

    private async Task DownloadAndInstallAsync()
    {
        if (Interlocked.CompareExchange(ref _downloadInFlight, 1, 0) != 0)
            return;

        var offer = _updateOffer;
        if (offer == null)
        {
            Interlocked.Exchange(ref _downloadInFlight, 0);
            return;
        }

        _downloading = true;
        if (_exiting == 0)
            _shutdownWindow.BeginInvoke(new Action(RefreshUpdateMenu));
        try
        {
            var partPath = await DownloadInstallerAsync(offer);
            if (_exiting != 0)
                return;
            if (partPath == null)
            {
                ShowUpdateToast("Could not download the CoolingControl update.");
                return;
            }

            var finalPath = Path.Combine(Path.GetTempPath(), "CoolingControl", offer.FileName);
            var committed = TryCommitDownload(partPath, finalPath, offer.Sha256);
            if (_exiting != 0)
                return;
            if (committed == UpdateFileResult.HashMismatch)
            {
                ShowUpdateToast("The CoolingControl update file failed verification.");
                return;
            }

            if (committed != UpdateFileResult.Ready)
            {
                ShowUpdateToast("Could not download the CoolingControl update.");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(finalPath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = UpdateLaunch.Arguments
                });
            }
            catch (Win32Exception)
            {
                return;
            }

            ExitTray();
        }
        catch (Exception ex)
        {
            if (_exiting != 0)
                return;
            Trace.TraceError("Update install failed: {0}", ex);
            ShowUpdateToast("Could not download the CoolingControl update.");
        }
        finally
        {
            _downloading = false;
            Interlocked.Exchange(ref _downloadInFlight, 0);
            if (_exiting == 0)
                RefreshUpdateMenu();
        }
    }

    private async Task<string?> DownloadInstallerAsync(UpdateOffer offer)
    {
        var directory = Path.Combine(Path.GetTempPath(), "CoolingControl");
        Directory.CreateDirectory(directory);
        var partPath = Path.Combine(directory, offer.FileName + ".part");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var response = await _updateHttp.GetAsync(
                offer.Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                Trace.TraceError("Update download failed: {0}", (int)response.StatusCode);
                return null;
            }

            var finalUri = response.RequestMessage?.RequestUri?.AbsoluteUri;
            if (!UpdateRelease.IsAllowedDownloadUrl(finalUri))
            {
                Trace.TraceError("Update download failed: unexpected host");
                return null;
            }

            await using (var httpStream = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var file = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await httpStream.CopyToAsync(file, timeout.Token);
            }

            return partPath;
        }
        catch (Exception ex)
        {
            Trace.TraceError("Update download failed: {0}", ex);
            TryDelete(partPath);
            return null;
        }
    }

    private static UpdateFileResult TryCommitDownload(string partPath, string finalPath, string sha256)
    {
        try
        {
            using var stream = File.OpenRead(partPath);
            if (!UpdateHash.Matches(stream, sha256))
            {
                TryDelete(partPath);
                return UpdateFileResult.HashMismatch;
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError("Update file check failed: {0}", ex);
            TryDelete(partPath);
            return UpdateFileResult.IoFailure;
        }

        try
        {
            File.Move(partPath, finalPath, overwrite: true);
            return UpdateFileResult.Ready;
        }
        catch (Exception ex)
        {
            Trace.TraceError("Update file check failed: {0}", ex);
            TryDelete(partPath);
            return UpdateFileResult.IoFailure;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Trace.TraceError("Update file cleanup failed: {0}", ex);
        }
    }

    private static void ShowUpdateToast(string message)
    {
        try
        {
            AlertToast.ShowUpdate(message);
        }
        catch (Exception ex)
        {
            Trace.TraceError("Update toast failed: {0}", ex);
        }
    }

    private enum UpdateFileResult
    {
        Ready,
        HashMismatch,
        IoFailure
    }
    private const string SensorTag = "sensor";
    private const string ControlTag = "control";

    private static ToolStripMenuItem DisabledItem(string text, string tag)
    {
        return new ToolStripMenuItem(EscapeAmpersand(text)) { Enabled = false, Tag = tag };
    }

    private static string EscapeAmpersand(string text) => text.Replace("&", "&&");

    private static Icon LoadIcon()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path))
        {
            var extracted = Icon.ExtractAssociatedIcon(path);
            if (extracted != null)
                return extracted;
        }

        return SystemIcons.Application;
    }
}

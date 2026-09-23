using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CoolingControl.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly StatusEndpoint _endpoint;
    private readonly ShutdownWindow _shutdownWindow;
    private int _pollInFlight;
    private int _exiting;
    private TrayMenuModel? _displayed;
    private TrayMenuModel? _pending;

    public TrayApplicationContext()
    {
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
    }

    private async Task PollAsync()
    {
        if (Interlocked.CompareExchange(ref _pollInFlight, 1, 0) != 0)
            return;

        try
        {
            var json = await _http.GetStringAsync(_endpoint.BaseUrl + "api/status");
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
        _icon.ContextMenuStrip = menu;
        previous?.Dispose();
    }

    private void OnMenuClosed(object? sender, ToolStripDropDownClosedEventArgs e)
    {
        if (_pending == null || _exiting != 0)
            return;

        var pending = _pending;
        _pending = null;
        _shutdownWindow.BeginInvoke(new Action(() => Rebuild(pending)));
    }

    private static void UpdateOpenMenu(ContextMenuStrip menu, TrayMenuModel model)
    {
        var health = menu.Items.OfType<ToolStripMenuItem>().Where(item => Equals(item.Tag, HealthTag)).ToList();
        var sensors = menu.Items.OfType<ToolStripMenuItem>().Where(item => Equals(item.Tag, SensorTag)).ToList();
        var controls = menu.Items.OfType<ToolStripMenuItem>().Where(item => Equals(item.Tag, ControlTag)).ToList();
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
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitTray()));
        return menu;
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
        _timer.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _http.Dispose();
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

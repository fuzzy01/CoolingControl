using System.Text.Json;
using CoolingControl;
using CoolingControl.Platform;
using Xunit;

namespace CoolingControl.Tests;

public class DefaultMonitorPlatformTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _scriptPath;

    public DefaultMonitorPlatformTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _scriptPath = Path.Combine(_tempDir, "script.lua");
        File.WriteAllText(_scriptPath, "");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private (ConfigHelper config, FakeAdapter adapter, DefaultMonitorPlatform platform) BuildPlatform(
        float initialValue, ControlConfig ctrl)
    {
        var config = new Config
        {
            ScriptPath = _scriptPath,
            Controls = [ctrl],
            Sensors = []
        };
        var configPath = Path.Combine(_tempDir, $"{Path.GetRandomFileName()}.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(config));
        var configHelper = new ConfigHelper(configPath);

        var adapter = new FakeAdapter();
        adapter.ControlValues[ctrl.Identifier] = initialValue;
        var platform = new DefaultMonitorPlatform(configHelper, adapter);

        return (configHelper, adapter, platform);
    }

    private static ControlConfig MakeCtrl(
        float stepUp = 8, float stepDown = 8,
        float minStop = 20, float minStart = 20,
        bool zeroRPM = false) => new()
    {
        Alias = "Fan",
        Identifier = "/fan/0",
        StepUp = stepUp,
        StepDown = stepDown,
        MinStop = minStop,
        MinStart = minStart,
        ZeroRPM = zeroRPM
    };

    [Fact]
    public void SetControls_StepUp_ClampsIncrease()
    {
        var (_, adapter, platform) = BuildPlatform(50f, MakeCtrl(stepUp: 8));

        platform.SetControls(new() { ["Fan"] = 70f });

        Assert.Equal(58f, adapter.LastSetValue("/fan/0"));
    }

    [Fact]
    public void SetControls_StepUp_WithinLimit_NotClamped()
    {
        var (_, adapter, platform) = BuildPlatform(50f, MakeCtrl(stepUp: 8));

        platform.SetControls(new() { ["Fan"] = 55f });

        Assert.Equal(55f, adapter.LastSetValue("/fan/0"));
    }

    [Fact]
    public void SetControls_StepDown_ClampsDecrease()
    {
        var (_, adapter, platform) = BuildPlatform(50f, MakeCtrl(stepDown: 8));

        platform.SetControls(new() { ["Fan"] = 30f });

        Assert.Equal(42f, adapter.LastSetValue("/fan/0"));
    }

    [Fact]
    public void SetControls_MinStop_EnforcedWhenRunning()
    {
        // prev=30 (running), stepDown=100 so ramp doesn't interfere, request=10 < MinStop=20
        var (_, adapter, platform) = BuildPlatform(30f, MakeCtrl(stepDown: 100, minStop: 20));

        platform.SetControls(new() { ["Fan"] = 10f });

        Assert.Equal(20f, adapter.LastSetValue("/fan/0"));
    }

    [Fact]
    public void SetControls_MinStart_EnforcedWhenStopped()
    {
        // prev=0 (stopped), stepUp=100 so ramp doesn't interfere, request=5 < MinStart=20
        var (_, adapter, platform) = BuildPlatform(0f, MakeCtrl(stepUp: 100, minStart: 20));

        platform.SetControls(new() { ["Fan"] = 5f });

        Assert.Equal(20f, adapter.LastSetValue("/fan/0"));
    }

    [Fact]
    public void SetControls_ZeroRequest_WhenStopped_NotSentToAdapter()
    {
        // Requesting 0 when already stopped — no transition, adapter should not receive the identifier
        var (_, adapter, platform) = BuildPlatform(0f, MakeCtrl(minStart: 20));

        platform.SetControls(new() { ["Fan"] = 0f });

        Assert.Null(adapter.LastSetValue("/fan/0"));
    }

    [Fact]
    public void SetControls_ZeroRPM_SendsDefaultControlValue()
    {
        // stepDown=100 so fan reaches 0 in one step; ZeroRPM should release to hardware
        var (_, adapter, platform) = BuildPlatform(50f, MakeCtrl(stepDown: 100, zeroRPM: true));

        platform.SetControls(new() { ["Fan"] = 0f });

        Assert.Equal(IPlatformAdapter.DefaultControlValue, adapter.LastSetValue("/fan/0"));
    }

    [Fact]
    public void SetControls_UnchangedValue_NotSentToAdapter()
    {
        var (_, adapter, platform) = BuildPlatform(50f, MakeCtrl());

        platform.SetControls(new() { ["Fan"] = 50f });

        Assert.Null(adapter.LastSetValue("/fan/0"));
    }

    private sealed class FakeAdapter : IPlatformAdapter
    {
        public Dictionary<string, float?> ControlValues { get; } = new();
        private readonly List<Dictionary<string, float>> _setCalls = [];

        public float? LastSetValue(string identifier) =>
            _setCalls.LastOrDefault()?.TryGetValue(identifier, out var v) == true ? v : null;

        public Dictionary<string, float?> GetControlValues(HashSet<string> ids) =>
            ids.ToDictionary(id => id, id => ControlValues.GetValueOrDefault(id));

        public Dictionary<string, float?> GetSensorValues(HashSet<string> ids) =>
            ids.ToDictionary(id => id, _ => (float?)null);

        public Dictionary<string, bool> SetControls(Dictionary<string, float> values)
        {
            _setCalls.Add(new Dictionary<string, float>(values));
            return values.ToDictionary(kvp => kvp.Key, _ => true);
        }

        public Dictionary<string, bool> ReleaseControls(HashSet<string> ids) =>
            ids.ToDictionary(id => id, _ => true);

        public void ListAllSensors() { }
        public void Suspend() { }
        public void Resume() { }
        public void Dispose() { }
    }
}

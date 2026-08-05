using CoolingControl.Platform;
using Xunit;

namespace CoolingControl.Tests;

public class CompositePlatformAdapterTests
{
    [Fact]
    public void GetValues_RoutesRequestsToMappedAdaptersAndMergesResults()
    {
        var first = new FakeAdapter
        {
            SensorValues = { ["sensor-1"] = 40f },
            ControlValues = { ["control-1"] = 25f }
        };
        var second = new FakeAdapter
        {
            SensorValues = { ["sensor-2"] = 50f },
            ControlValues = { ["control-2"] = 75f }
        };
        var adapter = CreateAdapter(first, second);

        var sensors = adapter.GetSensorValues(["sensor-1", "sensor-2", "unmapped"]);
        var controls = adapter.GetControlValues(["control-1", "control-2", "unmapped"]);

        Assert.True(Assert.Single(first.SensorRequests).SetEquals(["sensor-1"]));
        Assert.True(Assert.Single(second.SensorRequests).SetEquals(["sensor-2"]));
        Assert.Equal(40f, sensors["sensor-1"]);
        Assert.Equal(50f, sensors["sensor-2"]);
        Assert.DoesNotContain("unmapped", sensors.Keys);

        Assert.True(Assert.Single(first.ControlRequests).SetEquals(["control-1"]));
        Assert.True(Assert.Single(second.ControlRequests).SetEquals(["control-2"]));
        Assert.Equal(25f, controls["control-1"]);
        Assert.Equal(75f, controls["control-2"]);
        Assert.DoesNotContain("unmapped", controls.Keys);
    }

    [Fact]
    public void SetAndReleaseControls_RouteSubsetsAndMergeResults()
    {
        var first = new FakeAdapter { SetResults = { ["control-1"] = true }, ReleaseResults = { ["control-1"] = false } };
        var second = new FakeAdapter { SetResults = { ["control-2"] = false }, ReleaseResults = { ["control-2"] = true } };
        var adapter = CreateAdapter(first, second);

        var setResults = adapter.SetControls(new()
        {
            ["control-1"] = 25f,
            ["control-2"] = 75f,
            ["unmapped"] = 50f
        });
        var releaseResults = adapter.ReleaseControls(["control-1", "control-2", "unmapped"]);

        AssertControlValues(Assert.Single(first.SetRequests), ("control-1", 25f));
        AssertControlValues(Assert.Single(second.SetRequests), ("control-2", 75f));
        AssertControlResults(setResults, ("control-1", true), ("control-2", false));

        Assert.True(Assert.Single(first.ReleaseRequests).SetEquals(["control-1"]));
        Assert.True(Assert.Single(second.ReleaseRequests).SetEquals(["control-2"]));
        AssertControlResults(releaseResults, ("control-1", false), ("control-2", true));
    }

    [Fact]
    public void RequestsForUnregisteredPlatform_AreNotDispatched()
    {
        var first = new FakeAdapter();
        var adapter = new CompositePlatformAdapter(
            new Dictionary<string, IPlatformAdapter> { ["First"] = first },
            new Dictionary<string, string> { ["sensor-1"] = "Missing" },
            new Dictionary<string, string> { ["control-1"] = "Missing" });

        Assert.Empty(adapter.GetSensorValues(["sensor-1"]));
        Assert.Empty(adapter.GetControlValues(["control-1"]));
        Assert.Empty(adapter.SetControls(new() { ["control-1"] = 25f }));
        Assert.Empty(adapter.ReleaseControls(["control-1"]));
        Assert.Empty(first.SensorRequests);
        Assert.Empty(first.ControlRequests);
        Assert.Empty(first.SetRequests);
        Assert.Empty(first.ReleaseRequests);
    }

    [Fact]
    public void FanOutOperations_ReachEveryAdapterAndDisposeOnlyOnce()
    {
        var first = new FakeAdapter();
        var second = new FakeAdapter();
        var adapter = CreateAdapter(first, second);

        adapter.ListAllSensors();
        adapter.Suspend();
        adapter.Resume();
        adapter.Dispose();
        adapter.Dispose();

        foreach (var fake in new[] { first, second })
        {
            Assert.Equal(1, fake.ListAllSensorsCallCount);
            Assert.Equal(1, fake.SuspendCallCount);
            Assert.Equal(1, fake.ResumeCallCount);
            Assert.Equal(1, fake.DisposeCallCount);
        }
    }

    private static CompositePlatformAdapter CreateAdapter(FakeAdapter first, FakeAdapter second) =>
        new(
            new Dictionary<string, IPlatformAdapter>
            {
                ["First"] = first,
                ["Second"] = second
            },
            new Dictionary<string, string>
            {
                ["sensor-1"] = "First",
                ["sensor-2"] = "Second"
            },
            new Dictionary<string, string>
            {
                ["control-1"] = "First",
                ["control-2"] = "Second"
            });

    private static void AssertControlValues(
        Dictionary<string, float> actual,
        params (string Identifier, float Value)[] expected)
    {
        Assert.Equal(expected.Length, actual.Count);
        foreach (var (identifier, value) in expected)
            Assert.Equal(value, actual[identifier]);
    }

    private static void AssertControlResults(
        Dictionary<string, bool> actual,
        params (string Identifier, bool Value)[] expected)
    {
        Assert.Equal(expected.Length, actual.Count);
        foreach (var (identifier, value) in expected)
            Assert.Equal(value, actual[identifier]);
    }

    private sealed class FakeAdapter : IPlatformAdapter
    {
        public Dictionary<string, float?> SensorValues { get; } = [];
        public Dictionary<string, float?> ControlValues { get; } = [];
        public Dictionary<string, bool> SetResults { get; } = [];
        public Dictionary<string, bool> ReleaseResults { get; } = [];
        public List<HashSet<string>> SensorRequests { get; } = [];
        public List<HashSet<string>> ControlRequests { get; } = [];
        public List<Dictionary<string, float>> SetRequests { get; } = [];
        public List<HashSet<string>> ReleaseRequests { get; } = [];
        public int ListAllSensorsCallCount { get; private set; }
        public int SuspendCallCount { get; private set; }
        public int ResumeCallCount { get; private set; }
        public int DisposeCallCount { get; private set; }

        public Dictionary<string, float?> GetSensorValues(HashSet<string> sensorIdentifiers)
        {
            SensorRequests.Add([.. sensorIdentifiers]);
            return sensorIdentifiers.ToDictionary(id => id, id => SensorValues.GetValueOrDefault(id));
        }

        public Dictionary<string, float?> GetControlValues(HashSet<string> controlIdentifiers)
        {
            ControlRequests.Add([.. controlIdentifiers]);
            return controlIdentifiers.ToDictionary(id => id, id => ControlValues.GetValueOrDefault(id));
        }

        public Dictionary<string, bool> SetControls(Dictionary<string, float> controlValues)
        {
            SetRequests.Add(new Dictionary<string, float>(controlValues));
            return controlValues.Keys.ToDictionary(id => id, id => SetResults.GetValueOrDefault(id));
        }

        public Dictionary<string, bool> ReleaseControls(HashSet<string> controlIdentifiers)
        {
            ReleaseRequests.Add([.. controlIdentifiers]);
            return controlIdentifiers.ToDictionary(id => id, id => ReleaseResults.GetValueOrDefault(id));
        }

        public void ListAllSensors() => ListAllSensorsCallCount++;
        public void Suspend() => SuspendCallCount++;
        public void Resume() => ResumeCallCount++;
        public void Dispose() => DisposeCallCount++;
    }
}

using CoolingControl;
using Xunit;

namespace CoolingControl.Tests;

public class AlertEvaluatorTests
{
    [Fact]
    public void Evaluate_Temperature_AlertsAtMaxAndIgnoresNull()
    {
        var limits = new List<SensorConfig>
        {
            new() { Alias = "CPU Package", Identifier = "/cpu/0", AlertMax = 90f },
            new() { Alias = "GPU Core", Identifier = "/gpu/0" }
        };

        var under = AlertEvaluator.Evaluate(
            new Dictionary<string, float?> { ["CPU Package"] = 89.9f },
            recentErrorCount: 0,
            maxControlLoopErrors: 10,
            limits);
        var atMax = AlertEvaluator.Evaluate(
            new Dictionary<string, float?> { ["CPU Package"] = 90f },
            recentErrorCount: 0,
            maxControlLoopErrors: 10,
            limits);
        var missing = AlertEvaluator.Evaluate(
            new Dictionary<string, float?> { ["CPU Package"] = null },
            recentErrorCount: 0,
            maxControlLoopErrors: 10,
            limits);

        Assert.Empty(under);
        Assert.Equal("temp:CPU Package", Assert.Single(atMax).Key);
        Assert.Contains("90.0", atMax[0].Message);
        Assert.Empty(missing);
    }

    [Fact]
    public void Evaluate_Temperature_HoldsUntilFivePercentBelowLimit()
    {
        var limits = new List<SensorConfig>
        {
            new() { Alias = "CPU Package", Identifier = "/cpu/0", AlertMax = 60f }
        };
        var active = new[] { "temp:CPU Package" };

        var held = AlertEvaluator.Evaluate(
            new Dictionary<string, float?> { ["CPU Package"] = 58f },
            recentErrorCount: 0,
            maxControlLoopErrors: 10,
            limits,
            active);
        var released = AlertEvaluator.Evaluate(
            new Dictionary<string, float?> { ["CPU Package"] = 50f },
            recentErrorCount: 0,
            maxControlLoopErrors: 10,
            limits,
            active);

        Assert.Equal(57f, AlertEvaluator.ReleaseBelow(60f));
        Assert.Single(held);
        Assert.Empty(released);
    }

    [Fact]
    public void Evaluate_ErrorBudget_AlertsAtThreeQuarters()
    {
        Assert.Equal(8, AlertEvaluator.ErrorBudgetThreshold(10));

        var sensors = new Dictionary<string, float?>();
        var below = AlertEvaluator.Evaluate(sensors, recentErrorCount: 7, maxControlLoopErrors: 10, sensorConfigs: null);
        var atLine = AlertEvaluator.Evaluate(sensors, recentErrorCount: 8, maxControlLoopErrors: 10, sensorConfigs: null);

        Assert.Empty(below);
        Assert.Equal("errors", Assert.Single(atLine).Key);
    }

    [Fact]
    public void Diff_SameKeyRaisesOnceThenClears()
    {
        var alert = new Alert("temp:CPU Package", "CPU Package is 90.0, limit 90.0");
        var previous = new Dictionary<string, string>();

        var first = AlertEvaluator.Diff(previous, [alert]);
        Assert.Single(first.Raised);
        Assert.Empty(first.Cleared);

        previous[alert.Key] = alert.Message;
        var second = AlertEvaluator.Diff(previous, [alert]);
        Assert.Empty(second.Raised);
        Assert.Empty(second.Cleared);

        var third = AlertEvaluator.Diff(previous, []);
        Assert.Empty(third.Raised);
        Assert.Equal([alert.Message], third.Cleared);
    }
}

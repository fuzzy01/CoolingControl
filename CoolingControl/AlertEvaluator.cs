using System.Globalization;

namespace CoolingControl;

public sealed record Alert(string Key, string Message);

public readonly record struct AlertDiff(IReadOnlyList<Alert> Raised, IReadOnlyList<string> Cleared);

public static class AlertEvaluator
{
    public static int ErrorBudgetThreshold(int maxControlLoopErrors) =>
        (int)Math.Ceiling(maxControlLoopErrors * 0.75);

    public static float ReleaseBelow(float max) => max - Math.Max(max * 0.05f, 1f);

    public static IReadOnlyList<Alert> Evaluate(
        IReadOnlyDictionary<string, float?> sensors,
        int recentErrorCount,
        int maxControlLoopErrors,
        IEnumerable<SensorConfig>? sensorConfigs,
        IEnumerable<string>? activeKeys = null)
    {
        var active = activeKeys == null ? null : new HashSet<string>(activeKeys, StringComparer.Ordinal);
        var alerts = new List<Alert>();
        if (sensorConfigs != null)
        {
            foreach (var sensor in sensorConfigs)
            {
                if (sensor.AlertMax is not float max || string.IsNullOrEmpty(sensor.Alias))
                    continue;
                if (!sensors.TryGetValue(sensor.Alias, out var value) || value is not float reading)
                    continue;

                var key = "temp:" + sensor.Alias;
                var limit = active != null && active.Contains(key) ? ReleaseBelow(max) : max;
                if (reading < limit)
                    continue;

                alerts.Add(new Alert(
                    key,
                    string.Create(CultureInfo.InvariantCulture, $"{sensor.Alias} is {reading:0.0}, limit {max:0.0}")));
            }
        }

        var threshold = ErrorBudgetThreshold(maxControlLoopErrors);
        if (recentErrorCount >= threshold)
        {
            alerts.Add(new Alert(
                "errors",
                $"Control loop errors {recentErrorCount} of {maxControlLoopErrors} in the last 60 seconds"));
        }

        return alerts;
    }

    public static AlertDiff Diff(IReadOnlyDictionary<string, string> previous, IReadOnlyList<Alert> current)
    {
        var currentKeys = new HashSet<string>(current.Select(alert => alert.Key));
        var raised = current.Where(alert => !previous.ContainsKey(alert.Key)).ToList();
        var cleared = previous
            .Where(entry => !currentKeys.Contains(entry.Key))
            .Select(entry => entry.Value)
            .ToList();
        return new AlertDiff(raised, cleared);
    }
}

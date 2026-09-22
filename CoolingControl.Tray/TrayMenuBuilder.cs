using System.Globalization;
using System.Text.Json;

namespace CoolingControl.Tray;

internal sealed record ProfileChoice(string Name, bool IsChecked);

internal sealed record TrayMenuModel(
    IReadOnlyList<string> SensorLines,
    IReadOnlyList<string> ControlLines,
    IReadOnlyList<ProfileChoice> Profiles,
    string Tooltip);

internal static class TrayMenuBuilder
{
    public const int MaxTooltipLength = 127;

    public static TrayMenuModel ServiceNotRunning()
    {
        IReadOnlyList<string> lines = ["Service not running"];
        return new TrayMenuModel(lines, [], [], BuildTooltip(null, lines));
    }

    public static TrayMenuModel StatusServerDisabled()
    {
        IReadOnlyList<string> lines = ["Status server is disabled"];
        return new TrayMenuModel(lines, [], [], BuildTooltip(null, lines));
    }

    public static TrayMenuModel FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var activeProfile = ReadString(root, "activeProfile");
        var sensorLines = ReadSensors(root);
        var controlLines = ReadControls(root);
        var profiles = ReadProfiles(root, activeProfile);

        return new TrayMenuModel(sensorLines, controlLines, profiles, BuildTooltip(activeProfile, sensorLines));
    }

    public static bool SameShape(TrayMenuModel current, TrayMenuModel next)
    {
        if (current.SensorLines.Count != next.SensorLines.Count)
            return false;
        if (current.ControlLines.Count != next.ControlLines.Count)
            return false;
        if (current.Profiles.Count != next.Profiles.Count)
            return false;

        for (var i = 0; i < current.Profiles.Count; i++)
        {
            if (current.Profiles[i].Name != next.Profiles[i].Name)
                return false;
        }

        return true;
    }

    public static string BuildTooltip(string? activeProfile, IReadOnlyList<string> sensorLines)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(activeProfile))
            parts.Add(activeProfile);
        parts.AddRange(sensorLines.Take(3));

        var text = parts.Count == 0 ? "CoolingControl" : string.Join("\n", parts);
        return text.Length <= MaxTooltipLength ? text : text[..MaxTooltipLength];
    }

    public static string UnitFor(string alias)
    {
        var lower = alias.ToLowerInvariant();
        if (lower.Contains("fan") || lower.Contains("rpm"))
            return "RPM";
        if (lower.Contains("power"))
            return "W";
        if (lower.Contains("load"))
            return "%";
        return "°C";
    }

    private static List<string> ReadSensors(JsonElement root)
    {
        var lines = new List<string>();
        if (!root.TryGetProperty("sensors", out var sensors) || sensors.ValueKind != JsonValueKind.Object)
            return lines;

        foreach (var property in sensors.EnumerateObject())
            lines.Add(FormatSensor(property.Name, ReadNumber(property.Value)));
        return lines;
    }

    private static List<string> ReadControls(JsonElement root)
    {
        var lines = new List<string>();
        if (!root.TryGetProperty("controls", out var controls) || controls.ValueKind != JsonValueKind.Object)
            return lines;

        root.TryGetProperty("controlRpm", out var controlRpm);
        foreach (var property in controls.EnumerateObject())
        {
            if (ReadNumber(property.Value) is not double value)
                continue;
            lines.Add(FormatControl(property.Name, value, TryGetRpm(controlRpm, property.Name)));
        }
        return lines;
    }

    private static List<ProfileChoice> ReadProfiles(JsonElement root, string? activeProfile)
    {
        var choices = new List<ProfileChoice>();
        if (!root.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Array)
            return choices;

        foreach (var item in profiles.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;
            var name = item.GetString();
            if (string.IsNullOrEmpty(name))
                continue;
            choices.Add(new ProfileChoice(name, name == activeProfile));
        }
        return choices;
    }

    private static string FormatSensor(string alias, double? value)
    {
        var unit = UnitFor(alias);
        var number = value?.ToString("0.0", CultureInfo.InvariantCulture) ?? "—";
        return $"{alias} {number} {unit}";
    }

    private static string FormatControl(string alias, double value, double? rpm)
    {
        var percent = value.ToString("0.0", CultureInfo.InvariantCulture);
        if (rpm is not double measured)
            return $"{alias} {percent}%";
        var rounded = Math.Round(measured, MidpointRounding.AwayFromZero);
        return $"{alias} {percent}% · {rounded.ToString(CultureInfo.InvariantCulture)} RPM";
    }

    private static double? TryGetRpm(JsonElement controlRpm, string alias)
    {
        if (controlRpm.ValueKind != JsonValueKind.Object)
            return null;
        if (!controlRpm.TryGetProperty(alias, out var value))
            return null;
        return ReadNumber(value);
    }

    private static double? ReadNumber(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString();
    }
}

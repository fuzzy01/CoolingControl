namespace CoolingControl.DS18B20Plugin;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Parses lines pushed by the DS18B20 USB adapter, e.g. <c>t1=+28.70</c>.
/// </summary>
public static partial class LineParser
{
    [GeneratedRegex(@"^\s*t(?<tag>\d+)\s*=\s*(?<value>[+-]?\d+(\.\d+)?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReadingRegex();

    /// <summary>
    /// Attempts to parse a single line of adapter output into a sensor tag (e.g. <c>"t1"</c>)
    /// and its temperature value.
    /// </summary>
    /// <param name="line">The raw line read from the serial port.</param>
    /// <param name="tag">The parsed sensor tag (e.g. <c>"t1"</c>), lower-cased.</param>
    /// <param name="value">The parsed temperature value.</param>
    /// <returns><c>true</c> if the line matched the expected format; otherwise <c>false</c>.</returns>
    public static bool TryParse(string? line, out string tag, out float value)
    {
        tag = string.Empty;
        value = 0f;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        var match = ReadingRegex().Match(line.TrimEnd('\r', '\n'));
        if (!match.Success)
            return false;

        if (!float.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return false;

        tag = "t" + match.Groups["tag"].Value;
        return true;
    }
}

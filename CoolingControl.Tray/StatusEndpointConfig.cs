using System.Text.Json;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CoolingControl.Tests")]

namespace CoolingControl.Tray;

internal readonly record struct StatusEndpoint(int Port, bool Enabled)
{
    public string BaseUrl => $"http://localhost:{Port}/";
}

internal static class StatusEndpointConfig
{
    public const int DefaultPort = 19999;

    public static StatusEndpoint Read(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            return new StatusEndpoint(DefaultPort, true);

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = doc.RootElement;
            var port = DefaultPort;
            var enabled = true;

            if (root.TryGetProperty("StatusServerPort", out var portElement)
                && portElement.TryGetInt32(out var parsed)
                && parsed is > 0 and <= 65535)
            {
                port = parsed;
            }

            if (root.TryGetProperty("StatusServerEnabled", out var enabledElement))
            {
                if (enabledElement.ValueKind == JsonValueKind.False)
                    enabled = false;
                else if (enabledElement.ValueKind == JsonValueKind.True)
                    enabled = true;
            }

            return new StatusEndpoint(port, enabled);
        }
        catch (JsonException)
        {
            return new StatusEndpoint(DefaultPort, true);
        }
    }
}

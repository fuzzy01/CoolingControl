using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace CoolingControl.Tray;

internal static class UpdateSource
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/fuzzy01/CoolingControl/releases/latest";
}

internal static class UpdateVersions
{
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];

        if (!Version.TryParse(text, out var parsed))
            return false;

        version = new Version(
            parsed.Major,
            parsed.Minor,
            parsed.Build < 0 ? 0 : parsed.Build,
            parsed.Revision < 0 ? 0 : parsed.Revision);
        return true;
    }

    public static bool IsNewer(string? installed, string? remote)
    {
        return TryParse(remote, out var next)
            && TryParse(installed, out var current)
            && next > current;
    }
}

internal static class InstalledApp
{
    public static Version? ReadVersion(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return null;

        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var version = new Version(
                Math.Max(info.FileMajorPart, 0),
                Math.Max(info.FileMinorPart, 0),
                Math.Max(info.FileBuildPart, 0),
                Math.Max(info.FilePrivatePart, 0));
            return version == new Version(0, 0, 0, 0) ? null : version;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

internal static class InstalledRuntime
{
    public static string? ReadAssetSuffix(string runtimeConfigPath)
    {
        if (string.IsNullOrWhiteSpace(runtimeConfigPath) || !File.Exists(runtimeConfigPath))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(runtimeConfigPath));
            if (!doc.RootElement.TryGetProperty("runtimeOptions", out var options)
                || !options.TryGetProperty("tfm", out var tfmElement)
                || tfmElement.ValueKind != JsonValueKind.String)
                return null;

            return tfmElement.GetString() switch
            {
                "net8.0" => "net8",
                "net10.0" => "net10",
                _ => null
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}

internal sealed record UpdateOffer(string Version, string Url, string Sha256, string FileName);

internal static class UpdateRelease
{
    public static bool TryParse(string json, string assetSuffix, out UpdateOffer? offer)
    {
        offer = null;
        if (assetSuffix is not ("net8" or "net10"))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagElement)
                || tagElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("assets", out var assets)
                || assets.ValueKind != JsonValueKind.Array)
                return false;

            var tag = tagElement.GetString();
            if (!UpdateVersions.TryParse(tag, out _) || string.IsNullOrWhiteSpace(tag))
                return false;

            var display = tag.Trim();
            if (display.StartsWith('v') || display.StartsWith('V'))
                display = display[1..];

            var fileName = "CoolingControlSetup-" + assetSuffix + ".exe";
            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var nameElement)
                    || nameElement.ValueKind != JsonValueKind.String
                    || !string.Equals(nameElement.GetString(), fileName, StringComparison.Ordinal))
                    continue;

                if (!asset.TryGetProperty("browser_download_url", out var urlElement)
                    || urlElement.ValueKind != JsonValueKind.String
                    || !IsAllowedDownloadUrl(urlElement.GetString()))
                    return true;

                if (!asset.TryGetProperty("digest", out var digestElement)
                    || digestElement.ValueKind != JsonValueKind.String
                    || !TryReadSha256(digestElement.GetString(), out var sha256))
                    return true;

                offer = new UpdateOffer(display, urlElement.GetString()!, sha256, fileName);
                return true;
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static bool IsAllowedDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("github-releases.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadSha256(string? digest, out string hex)
    {
        hex = "";
        if (string.IsNullOrWhiteSpace(digest)
            || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            return false;

        hex = digest["sha256:".Length..].Trim().ToLowerInvariant();
        if (hex.Length != 64)
            return false;

        foreach (var character in hex)
        {
            var isHex = character is >= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F';
            if (!isHex)
                return false;
        }

        return true;
    }
}

internal static class UpdateHash
{
    public static bool Matches(Stream stream, string expectedHex)
    {
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        return actual.Equals(expectedHex, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class UpdateLaunch
{
    public const string Arguments = "/SILENT /NORESTART /SUPPRESSMSGBOXES /SP- /RESTARTRAY=1";
}

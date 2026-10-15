using System.Text;
using CoolingControl.Tray;
using Xunit;

namespace CoolingControl.Tests;

public class UpdateCheckTests : IDisposable
{
    private const string Net8Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Net10Sha = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
    private const string ReleaseJson =
        """
        {
          "tag_name": "1.1.11",
          "assets": [
            {
              "name": "CoolingControlSetup-net8.exe",
              "browser_download_url": "https://github.com/fuzzy01/CoolingControl/releases/download/1.1.11/CoolingControlSetup-net8.exe",
              "digest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
            },
            {
              "name": "CoolingControlSetup-net10.exe",
              "browser_download_url": "https://github.com/fuzzy01/CoolingControl/releases/download/1.1.11/CoolingControlSetup-net10.exe",
              "digest": "sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789"
            }
          ]
        }
        """;

    private readonly string _tempDir;

    public UpdateCheckTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Versions_TreatMissingPartsAsZero()
    {
        Assert.True(UpdateVersions.TryParse("1.1.9", out var shortVersion));
        Assert.True(UpdateVersions.TryParse("1.1.9.0", out var fullVersion));
        Assert.Equal(fullVersion, shortVersion);
        Assert.Equal(new Version(1, 1, 9, 0), shortVersion);

        Assert.False(UpdateVersions.IsNewer("1.1.9.0", "1.1.9"));
        Assert.False(UpdateVersions.IsNewer("1.1.9", "1.1.9.0"));
        Assert.True(UpdateVersions.IsNewer("1.1.9.0", "1.1.10"));
        Assert.True(UpdateVersions.IsNewer("1.1.9.0", "1.1.10.0"));
        Assert.False(UpdateVersions.IsNewer("1.1.10.0", "1.1.9"));

        Assert.True(UpdateVersions.TryParse("v1.1.9", out var prefixed));
        Assert.Equal(shortVersion, prefixed);
        Assert.False(UpdateVersions.TryParse("", out _));
        Assert.False(UpdateVersions.TryParse("   ", out _));
        Assert.False(UpdateVersions.TryParse("latest", out _));
        Assert.False(UpdateVersions.TryParse(null, out _));
    }

    [Fact]
    public void TryParse_SelectsTheMatchingInstaller()
    {
        Assert.True(UpdateRelease.TryParse(ReleaseJson, "net8", out var net8));
        Assert.NotNull(net8);
        Assert.Equal("1.1.11", net8.Version);
        Assert.Equal(
            "https://github.com/fuzzy01/CoolingControl/releases/download/1.1.11/CoolingControlSetup-net8.exe",
            net8.Url);
        Assert.Equal(Net8Sha, net8.Sha256);
        Assert.Equal("CoolingControlSetup-net8.exe", net8.FileName);

        Assert.True(UpdateRelease.TryParse(ReleaseJson, "net10", out var net10));
        Assert.NotNull(net10);
        Assert.Equal(Net10Sha, net10.Sha256);
        Assert.Equal("CoolingControlSetup-net10.exe", net10.FileName);
    }

    [Theory]
    [InlineData("net9")]
    [InlineData("net8.0")]
    [InlineData("")]
    public void TryParse_UnknownSuffix_FailsTheDocument(string suffix)
    {
        Assert.False(UpdateRelease.TryParse(ReleaseJson, suffix, out var offer));
        Assert.Null(offer);
    }

    [Fact]
    public void TryParse_MissingAssetOrUnusableInstaller_ReturnsNullOffer()
    {
        const string missing =
            """
            {
              "tag_name": "1.1.11",
              "assets": [
                {
                  "name": "CoolingControlSetup-net10.exe",
                  "browser_download_url": "https://github.com/fuzzy01/CoolingControl/releases/download/1.1.11/CoolingControlSetup-net10.exe",
                  "digest": "sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789"
                }
              ]
            }
            """;
        Assert.True(UpdateRelease.TryParse(missing, "net8", out var missingOffer));
        Assert.Null(missingOffer);

        const string httpUrl =
            """
            {
              "tag_name": "1.1.11",
              "assets": [
                {
                  "name": "CoolingControlSetup-net8.exe",
                  "browser_download_url": "http://github.com/fuzzy01/CoolingControl/releases/download/1.1.11/CoolingControlSetup-net8.exe",
                  "digest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                }
              ]
            }
            """;
        Assert.True(UpdateRelease.TryParse(httpUrl, "net8", out var httpOffer));
        Assert.Null(httpOffer);

        const string badDigest =
            """
            {
              "tag_name": "1.1.11",
              "assets": [
                {
                  "name": "CoolingControlSetup-net8.exe",
                  "browser_download_url": "https://github.com/fuzzy01/CoolingControl/releases/download/1.1.11/CoolingControlSetup-net8.exe",
                  "digest": "md5:0123456789abcdef0123456789abcdef"
                }
              ]
            }
            """;
        Assert.True(UpdateRelease.TryParse(badDigest, "net8", out var digestOffer));
        Assert.Null(digestOffer);

        Assert.False(UpdateRelease.TryParse("{", "net8", out var broken));
        Assert.Null(broken);
        Assert.False(UpdateRelease.TryParse("""{ "message": "Not Found" }""", "net8", out var error));
        Assert.Null(error);
    }

    [Fact]
    public void TryParse_StripsALeadingVFromTheDisplayVersion()
    {
        const string json =
            """
            {
              "tag_name": "v1.2.0",
              "assets": [
                {
                  "name": "CoolingControlSetup-net8.exe",
                  "browser_download_url": "https://objects.githubusercontent.com/CoolingControlSetup-net8.exe",
                  "digest": "SHA256:0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF"
                }
              ]
            }
            """;

        Assert.True(UpdateRelease.TryParse(json, "net8", out var offer));
        Assert.NotNull(offer);
        Assert.Equal("1.2.0", offer.Version);
        Assert.Equal(Net8Sha, offer.Sha256);
        Assert.True(UpdateVersions.IsNewer("1.1.10.0", offer.Version));
    }

    [Theory]
    [InlineData("https://github.com/fuzzy01/CoolingControl/releases/download/1.1.11/CoolingControlSetup-net8.exe", true)]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/setup.exe", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/setup.exe", true)]
    [InlineData("https://github-releases.githubusercontent.com/setup.exe", true)]
    [InlineData("http://github.com/fuzzy01/CoolingControl/releases/download/1.1.11/CoolingControlSetup-net8.exe", false)]
    [InlineData("https://evil.example/CoolingControlSetup-net8.exe", false)]
    [InlineData("https://github.com.evil.com/CoolingControlSetup-net8.exe", false)]
    [InlineData("https://user@github.com/fuzzy01/CoolingControl/releases/download/1.1.11/file.exe", false)]
    [InlineData("", false)]
    public void IsAllowedDownloadUrl_AllowsOnlyTheReleaseHosts(string url, bool allowed)
    {
        Assert.Equal(allowed, UpdateRelease.IsAllowedDownloadUrl(url));
    }

    [Fact]
    public void Hash_MatchesSha256IgnoringCase()
    {
        var bytes = Encoding.ASCII.GetBytes("abc");
        const string expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

        Assert.True(UpdateHash.Matches(new MemoryStream(bytes), expected));
        Assert.True(UpdateHash.Matches(new MemoryStream(bytes), expected.ToUpperInvariant()));
        Assert.False(UpdateHash.Matches(new MemoryStream(bytes), new string('0', 64)));
    }

    [Fact]
    public void LaunchArguments_AreSilentAndRestartTheTray()
    {
        Assert.Contains("/SILENT", UpdateLaunch.Arguments);
        Assert.Contains("/RESTARTRAY=1", UpdateLaunch.Arguments);
        Assert.DoesNotContain("/TASKS", UpdateLaunch.Arguments);
    }

    [Fact]
    public void ReadAssetSuffix_MapsTheTrayRuntime()
    {
        var net8 = Path.Combine(_tempDir, "net8.runtimeconfig.json");
        var net10 = Path.Combine(_tempDir, "net10.runtimeconfig.json");
        var other = Path.Combine(_tempDir, "other.runtimeconfig.json");
        File.WriteAllText(net8, """{ "runtimeOptions": { "tfm": "net8.0" } }""");
        File.WriteAllText(net10, """{ "runtimeOptions": { "tfm": "net10.0" } }""");
        File.WriteAllText(other, """{ "runtimeOptions": { "tfm": "net9.0" } }""");

        Assert.Equal("net8", InstalledRuntime.ReadAssetSuffix(net8));
        Assert.Equal("net10", InstalledRuntime.ReadAssetSuffix(net10));
        Assert.Null(InstalledRuntime.ReadAssetSuffix(other));
        Assert.Null(InstalledRuntime.ReadAssetSuffix(Path.Combine(_tempDir, "missing.json")));
    }

    [Fact]
    public void ReadVersion_IgnoresAMissingOrUnversionedFile()
    {
        Assert.Null(InstalledApp.ReadVersion(Path.Combine(_tempDir, "CoolingControl.exe")));

        var text = Path.Combine(_tempDir, "notes.exe");
        File.WriteAllText(text, "not a pe file");
        Assert.Null(InstalledApp.ReadVersion(text));
    }

    [Fact]
    public void CreateUpdate_UsesItsOwnTag()
    {
        var toast = AlertToast.CreateUpdate("CoolingControl 1.1.11 is available.");

        Assert.Equal(AlertToast.UpdateTag, toast.Tag);
        Assert.Equal("cc-update", toast.Tag);
        Assert.Equal(AlertToast.Group, toast.Group);
        Assert.NotEqual(AlertToast.Tag, toast.Tag);
    }
}

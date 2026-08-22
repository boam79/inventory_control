using Inventory.Infrastructure;
using System.Net;
using System.Net.Http;
using System.Text;
using Velopack;
using Velopack.Sources;

namespace Inventory.Tests;

public class UpdateApplyTests
{
    [Fact]
    public void Latest_release_json_captures_download_url_and_version()
    {
        const string json = """
            {
              "tag_name": "v1.0.3",
              "assets": [
                {
                  "name": "SpringClinic.Inventory-win-Setup.exe",
                  "browser_download_url": "https://github.com/boam79/inventory_control/releases/download/v1.0.3/SpringClinic.Inventory-win-Setup.exe"
                },
                {
                  "name": "SpringClinic.Inventory-win-Setup.sha256",
                  "browser_download_url": "https://github.com/boam79/inventory_control/releases/download/v1.0.3/SpringClinic.Inventory-win-Setup.sha256"
                }
              ]
            }
            """;

        var offer = UpdateChecker.ParseLatestRelease(json);
        Assert.True(offer.Found);
        Assert.Equal("v1.0.3", offer.VersionTag);
        Assert.Equal(
            "https://github.com/boam79/inventory_control/releases/download/v1.0.3/SpringClinic.Inventory-win-Setup.exe",
            offer.PackageUrl);
        Assert.True(offer.HashRequired);
        Assert.Contains("v1.0.3", offer.Message);
        Assert.Contains("Setup.exe", offer.Message);
        Assert.Equal(
            "https://github.com/boam79/inventory_control/releases/download/v1.0.3/SpringClinic.Inventory-win-Setup.exe",
            UpdateChecker.SetupDownloadUrl("v1.0.3"));
    }

    [Fact]
    public void Remote_tag_is_newer_than_installed_version()
    {
        Assert.True(UpdateChecker.IsRemoteNewer("v1.0.4", "1.0.3"));
        Assert.True(UpdateChecker.IsRemoteNewer("1.0.5", "1.0.4"));
        Assert.False(UpdateChecker.IsRemoteNewer("v1.0.4", "1.0.4"));
        Assert.False(UpdateChecker.IsRemoteNewer("v1.0.3", "1.0.4"));
        Assert.False(UpdateChecker.IsRemoteNewer("", "1.0.4"));
    }

    [Fact]
    public void Velopack_null_update_does_not_claim_latest_when_github_is_newer()
    {
        var offer = new LatestReleaseOffer(
            true,
            "v1.0.4",
            "https://github.com/boam79/inventory_control/releases/download/v1.0.4/SpringClinic.Inventory-win-Setup.exe",
            null,
            null,
            false,
            "최신 v1.0.4");
        var message = UpdateChecker.AfterVelopackNoUpdate(offer, "1.0.3");
        Assert.DoesNotContain("새 설치 버전이 없습니다", message);
        Assert.Contains("v1.0.4", message);
        Assert.Contains("Setup.exe", message);
        Assert.Contains("피드", message);
    }

    [Fact]
    public void Status_message_asks_to_update_when_github_is_newer()
    {
        var offer = new LatestReleaseOffer(
            true,
            "v1.0.4",
            "https://example.invalid/Setup.exe",
            null,
            null,
            false,
            "최신 v1.0.4");
        var status = UpdateChecker.StatusMessage(offer, "1.0.3");
        Assert.Contains("새 버전", status);
        Assert.Contains("v1.0.4", status);
        Assert.Contains("1.0.3", status);
        Assert.DoesNotContain("새 설치 버전이 없습니다", status);
    }

    [Fact]
    public void Velopack_win_feed_json_captures_version_and_setup_url()
    {
        const string json = """
            {"Assets":[{"PackageId":"SpringClinic.Inventory","Version":"1.0.23","Type":"Full","FileName":"SpringClinic.Inventory-1.0.23-full.nupkg","SHA256":"ABC"}]}
            """;
        var offer = UpdateChecker.ParseVelopackWinFeed(json);
        Assert.True(offer.Found);
        Assert.Equal("v1.0.23", offer.VersionTag);
        Assert.Equal(UpdateChecker.SetupDownloadUrl("v1.0.23"), offer.PackageUrl);
        Assert.Equal(UpdateChecker.LatestSetupSha256Url, offer.Sha256Url);
        Assert.Contains(UpdateChecker.LatestDownloadBase, UpdateChecker.LatestFeedUrl);
    }

    [Fact]
    public void Rate_limit_message_is_korean_and_points_to_manual_setup()
    {
        Assert.True(UpdateChecker.LooksLikeRateLimit("Response status code does not indicate success: 403 (rate limit exceeded)."));
        Assert.True(UpdateChecker.LooksLikeRateLimit(null, HttpStatusCode.Forbidden));
        Assert.Contains("한도", UpdateChecker.RateLimitUserMessage);
        Assert.Contains("잠시 후", UpdateChecker.RateLimitUserMessage);
        Assert.Contains(UpdateChecker.LatestSetupUrl, UpdateChecker.RateLimitUserMessage);
        Assert.Contains(UpdateChecker.ReleasesUrl, UpdateChecker.RateLimitUserMessage);
    }

    [Fact]
    public async Task Inspect_latest_prefers_static_feed_over_api()
    {
        var package = "pkg"u8.ToArray();
        var hex = UpdateChecker.Sha256Hex(package);
        using var handler = new MapHandler(new Dictionary<string, (HttpStatusCode Code, string Body, byte[]? Bytes)>(StringComparer.OrdinalIgnoreCase)
        {
            [UpdateChecker.LatestFeedUrl] = (HttpStatusCode.OK, """
                {"Assets":[{"PackageId":"SpringClinic.Inventory","Version":"9.9.9","Type":"Full","FileName":"x.nupkg"}]}
                """, null),
            [UpdateChecker.LatestSetupSha256Url] = (HttpStatusCode.OK, $"{hex}  SpringClinic.Inventory-win-Setup.exe\n", null),
            [UpdateChecker.LatestApi] = (HttpStatusCode.Forbidden, "rate limit exceeded", null)
        });
        using var client = new HttpClient(handler);
        var offer = await UpdateChecker.InspectLatestAsync(client);
        Assert.True(offer.Found);
        Assert.Equal("v9.9.9", offer.VersionTag);
        Assert.True(UpdateChecker.HashesMatch(hex, offer.Sha256Hex));
    }

    [Fact]
    public async Task Inspect_latest_returns_korean_rate_limit_when_feed_forbidden()
    {
        using var handler = new MapHandler(new Dictionary<string, (HttpStatusCode Code, string Body, byte[]? Bytes)>(StringComparer.OrdinalIgnoreCase)
        {
            [UpdateChecker.LatestFeedUrl] = (HttpStatusCode.Forbidden, "rate limit exceeded", null)
        });
        using var client = new HttpClient(handler);
        var offer = await UpdateChecker.InspectLatestAsync(client);
        Assert.False(offer.Found);
        Assert.Contains("한도", offer.Message);
        Assert.Contains("403", offer.Message);
    }

    [Fact]
    public async Task Inspect_latest_fills_sha256_hex_when_hash_asset_exists()
    {
        var package = "pkg"u8.ToArray();
        var hex = UpdateChecker.Sha256Hex(package);
        using var handler = new MapHandler(new Dictionary<string, (HttpStatusCode Code, string Body, byte[]? Bytes)>(StringComparer.OrdinalIgnoreCase)
        {
            [UpdateChecker.LatestApi] = (HttpStatusCode.OK, """
                {
                  "tag_name": "v9.9.9",
                  "assets": [
                    {
                      "name": "SpringClinic.Inventory-win-Setup.exe",
                      "browser_download_url": "https://example.invalid/SpringClinic.Inventory-win-Setup.exe"
                    },
                    {
                      "name": "SpringClinic.Inventory-win-Setup.sha256",
                      "browser_download_url": "https://example.invalid/SpringClinic.Inventory-win-Setup.sha256"
                    }
                  ]
                }
                """, null),
            ["SpringClinic.Inventory-win-Setup.sha256"] = (HttpStatusCode.OK, $"{hex}  SpringClinic.Inventory-win-Setup.exe\n", null)
        });
        using var client = new HttpClient(handler);
        var offer = await UpdateChecker.InspectLatestAsync(client);
        Assert.True(offer.Found);
        Assert.Equal("v9.9.9", offer.VersionTag);
        Assert.Equal("https://example.invalid/SpringClinic.Inventory-win-Setup.exe", offer.PackageUrl);
        Assert.True(UpdateChecker.HashesMatch(hex, offer.Sha256Hex));
    }

    [Fact]
    public async Task Hash_mismatch_does_not_apply()
    {
        var work = Path.Combine(Path.GetTempPath(), "sci-upd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var db = Path.Combine(work, "inventory.db");
            File.WriteAllText(db, "db");
            var marker = Path.Combine(work, "app.marker");
            File.WriteAllText(marker, "RUNNING");
            using var handler = new BytesHandler("package-bytes"u8.ToArray());
            using var client = new HttpClient(handler);
            var result = await UpdateChecker.DownloadVerifyAndStageAsync(
                client,
                new Uri("https://example.invalid/app.exe"),
                expectedSha256Hex: "DEADBEEF",
                dbPath: db,
                stagingFolder: Path.Combine(work, "stage"),
                currentMarkerPath: marker);
            Assert.False(result.Applied);
            Assert.True(result.KeptCurrent);
            Assert.Contains("해시", result.Message);
            Assert.Equal("RUNNING", File.ReadAllText(marker));
            Assert.False(File.Exists(Path.Combine(work, "stage", "update.bin")));
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public void Velopack_not_installed_does_not_kill_process()
    {
        Exception? beforeInit = null;
        try
        {
            var source = new SimpleWebSource(UpdateChecker.LatestDownloadBase);
            _ = new UpdateManager(source);
        }
        catch (Exception ex)
        {
            beforeInit = ex;
        }

        Assert.NotNull(beforeInit);
        Assert.Contains("VelopackLocator", beforeInit!.Message, StringComparison.OrdinalIgnoreCase);

        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        Exception? afterInit = null;
        var installed = true;
        try
        {
            var source = new SimpleWebSource(UpdateChecker.LatestDownloadBase);
            var mgr = new UpdateManager(source);
            installed = mgr.IsInstalled;
        }
        catch (Exception ex)
        {
            afterInit = ex;
        }

        Assert.Null(afterInit);
        Assert.False(installed);
    }

    private sealed class BytesHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;
        public BytesHandler(byte[] bytes) => _bytes = bytes;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_bytes)
            });
    }

    private sealed class MapHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Code, string Body, byte[]? Bytes)> _map;
        public MapHandler(Dictionary<string, (HttpStatusCode Code, string Body, byte[]? Bytes)> map) => _map = map;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            foreach (var pair in _map)
            {
                if (url.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
                {
                    var content = pair.Value.Bytes is null
                        ? new StringContent(pair.Value.Body, Encoding.UTF8, "application/json")
                        : new ByteArrayContent(pair.Value.Bytes);
                    return Task.FromResult(new HttpResponseMessage(pair.Value.Code) { Content = content });
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

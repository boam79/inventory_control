using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Inventory.Infrastructure;

public sealed record UpdateCheckResult(bool Checked, string Message, string FeedUrl);

public sealed record LatestReleaseOffer(
    bool Found,
    string VersionTag,
    string? PackageUrl,
    string? Sha256Url,
    string? Sha256Hex,
    bool HashRequired,
    string Message);

public sealed record PackageApplyResult(
    bool Applied,
    bool KeptCurrent,
    bool BackupTaken,
    string Message,
    string? StagedPath);

public static class UpdateChecker
{
    public const string ReleasesUrl = "https://github.com/boam79/inventory_control/releases";
    public const string LatestApi = "https://api.github.com/repos/boam79/inventory_control/releases/latest";
    /// <summary>Velopack SimpleWebSource base — GitHub CDN asset URLs, not api.github.com (avoids API rate limits).</summary>
    public const string LatestDownloadBase = "https://github.com/boam79/inventory_control/releases/latest/download";
    public const string LatestFeedUrl = LatestDownloadBase + "/releases.win.json";
    public const string SetupFileName = "SpringClinic.Inventory-win-Setup.exe";
    public const string LatestSetupUrl = LatestDownloadBase + "/" + SetupFileName;
    public const string LatestSetupSha256Url = LatestDownloadBase + "/SpringClinic.Inventory-win-Setup.sha256";

    public static string RateLimitUserMessage =>
        "원인: GitHub 업데이트 확인 한도를 잠시 초과했습니다(403).\n"
        + "조치: 잠시 후 다시 시도하세요. 급하면 Releases에서 Setup.exe를 직접 받아 설치하세요.\n"
        + "다운로드: " + LatestSetupUrl + "\n목록: " + ReleasesUrl;

    public static bool LooksLikeRateLimit(string? message, System.Net.HttpStatusCode? status = null) =>
        status == System.Net.HttpStatusCode.Forbidden
        || (!string.IsNullOrWhiteSpace(message)
            && (message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                || message.Contains("403", StringComparison.Ordinal)));

    public static async Task<UpdateCheckResult> CheckAsync(HttpClient? client = null)
    {
        var owns = client is null;
        client ??= new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        try
        {
            EnsureUserAgent(client);
            using var response = await client.GetAsync(LatestFeedUrl);
            if (LooksLikeRateLimit(null, response.StatusCode))
            {
                return new UpdateCheckResult(false, RateLimitUserMessage, ReleasesUrl);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(false, "업데이트를 확인하지 못했습니다. 재고 업무는 계속할 수 있습니다.", ReleasesUrl);
            }

            var json = await response.Content.ReadAsStringAsync();
            var offer = ParseVelopackWinFeed(json);
            var tag = offer.Found ? offer.VersionTag : "unknown";
            return new UpdateCheckResult(true, $"최신 배포: {tag}", ReleasesUrl);
        }
        catch (Exception ex) when (LooksLikeRateLimit(ex.Message))
        {
            return new UpdateCheckResult(false, RateLimitUserMessage, ReleasesUrl);
        }
        catch
        {
            return new UpdateCheckResult(false, "업데이트 서버에 연결하지 못했습니다. 오프라인에서도 핵심 재고 기능은 동작합니다.", ReleasesUrl);
        }
        finally
        {
            if (owns)
            {
                client.Dispose();
            }
        }
    }

    public static string SetupDownloadUrl(string? tagName) =>
        string.IsNullOrWhiteSpace(tagName)
            ? LatestSetupUrl
            : $"https://github.com/boam79/inventory_control/releases/download/{tagName.Trim()}/{SetupFileName}";
    public static bool HashesMatch(string? expectedHex, string? actualHex) =>
        !string.IsNullOrWhiteSpace(expectedHex)
        && !string.IsNullOrWhiteSpace(actualHex)
        && expectedHex.Trim().Equals(actualHex.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool IsRemoteNewer(string? remoteTag, string currentVersion)
    {
        var remote = TryParseVersion(remoteTag);
        var current = TryParseVersion(currentVersion);
        return remote is not null && current is not null && remote > current;
    }

    public static string StatusMessage(LatestReleaseOffer offer, string currentVersion)
    {
        if (!offer.Found)
        {
            return offer.Message;
        }

        if (IsRemoteNewer(offer.VersionTag, currentVersion))
        {
            return $"새 버전 {offer.VersionTag}가 있습니다. 업데이트를 누르세요. 지금 버전 {currentVersion}.";
        }

        return $"최신입니다 ({offer.VersionTag}). 재고 업무를 계속하세요.";
    }

    /// <summary>True when a successful check found a remote release newer than the installed version.</summary>
    public static bool ShouldShowUpdatePrompt(LatestReleaseOffer offer, string currentVersion) =>
        offer.Found && IsRemoteNewer(offer.VersionTag, currentVersion);

    public static string UpdatePromptMessage(LatestReleaseOffer offer, string currentVersion) =>
        $"새 버전 {offer.VersionTag}이(가) 배포되었습니다.\n\n"
        + $"현재 버전: {currentVersion}\n\n"
        + "지금 업데이트하면 프로그램이 다시 시작됩니다.\n"
        + "나중에 하려면 상단 「업데이트」 버튼을 이용하세요.";

    public static string AfterVelopackNoUpdate(LatestReleaseOffer? offer, string currentVersion)
    {
        if (offer is { Found: true } && IsRemoteNewer(offer.VersionTag, currentVersion))
        {
            return $"원인: 설치본 피드(releases.win.json)를 찾지 못했습니다. GitHub에는 {offer.VersionTag}가 있습니다.\n조치: Setup.exe를 받아 설치하세요.\n다운로드: {offer.PackageUrl ?? SetupDownloadUrl(offer.VersionTag)}";
        }

        if (offer is { Found: true })
        {
            return $"최신 설치본입니다. GitHub: {offer.VersionTag}\n재고 업무를 계속하세요.";
        }

        return "새 설치 버전이 없습니다. 재고 업무를 계속하세요.";
    }

    private static Version? TryParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().TrimStart('v', 'V');
        return Version.TryParse(trimmed, out var parsed) ? parsed : null;
    }

    public static LatestReleaseOffer ParseVelopackWinFeed(string json, string? sha256Hex = null)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Assets", out var assets)
            || assets.ValueKind != JsonValueKind.Array
            || assets.GetArrayLength() == 0)
        {
            return new LatestReleaseOffer(false, "", null, null, null, false, "새 설치 버전이 없습니다. 재고 업무를 계속하세요.");
        }

        var asset = assets[0];
        var version = asset.TryGetProperty("Version", out var v) ? v.GetString() : null;
        if (string.IsNullOrWhiteSpace(version))
        {
            return new LatestReleaseOffer(false, "", null, null, null, false, "새 설치 버전이 없습니다. 재고 업무를 계속하세요.");
        }

        var tag = version.StartsWith('v') || version.StartsWith('V') ? version : "v" + version;
        var packageUrl = SetupDownloadUrl(tag);
        var message = $"최신 {tag}\n다운로드: {packageUrl}";
        return new LatestReleaseOffer(true, tag, packageUrl, LatestSetupSha256Url, sha256Hex, HashRequired: true, message);
    }

    public static LatestReleaseOffer ParseLatestRelease(string json, string? sha256Hex = null)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseLatestRelease(doc.RootElement, sha256Hex);
    }

    public static LatestReleaseOffer ParseLatestRelease(JsonElement root, string? sha256Hex = null)
    {
        var tag = root.TryGetProperty("tag_name", out var name) ? name.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(tag) || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return new LatestReleaseOffer(false, tag, null, null, null, false, "새 설치 버전이 없습니다. 재고 업무를 계속하세요.");
        }

        string? setupUrl = null;
        string? nupkgUrl = null;
        string? zipUrl = null;
        string? otherExe = null;
        string? shaUrl = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var file = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (file.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
            {
                shaUrl = url;
            }
            else if (file.Equals(SetupFileName, StringComparison.OrdinalIgnoreCase)
                     || (file.Contains("Setup", StringComparison.OrdinalIgnoreCase) && file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            {
                setupUrl = url;
            }
            else if (file.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            {
                nupkgUrl = url;
            }
            else if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                zipUrl = url;
            }
            else if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                otherExe ??= url;
            }
        }

        var packageUrl = setupUrl ?? nupkgUrl ?? zipUrl ?? otherExe;
        if (packageUrl is null)
        {
            return new LatestReleaseOffer(false, tag, null, shaUrl, sha256Hex, shaUrl is not null, "새 설치 파일이 없습니다. 재고 업무를 계속하세요.");
        }

        var hashRequired = shaUrl is not null;
        var message = $"최신 {tag}\n다운로드: {packageUrl}";
        return new LatestReleaseOffer(true, tag, packageUrl, shaUrl, sha256Hex, hashRequired, message);
    }

    public static async Task<LatestReleaseOffer> InspectLatestAsync(HttpClient? client = null)
    {
        var owns = client is null;
        client ??= new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        try
        {
            EnsureUserAgent(client);
            // Prefer static release asset URL (no api.github.com quota).
            using var feedResponse = await client.GetAsync(LatestFeedUrl);
            if (LooksLikeRateLimit(null, feedResponse.StatusCode))
            {
                return new LatestReleaseOffer(false, "", null, null, null, false, RateLimitUserMessage);
            }

            if (feedResponse.IsSuccessStatusCode)
            {
                var feedJson = await feedResponse.Content.ReadAsStringAsync();
                var fromFeed = ParseVelopackWinFeed(feedJson);
                if (fromFeed.Found)
                {
                    return await AttachSetupSha256Async(client, fromFeed);
                }
            }

            // Fallback: GitHub Releases API (may 403 when unauthenticated quota is exhausted).
            using var response = await client.GetAsync(LatestApi);
            if (LooksLikeRateLimit(null, response.StatusCode))
            {
                return new LatestReleaseOffer(false, "", null, null, null, false, RateLimitUserMessage);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new LatestReleaseOffer(false, "", null, null, null, false, "업데이트를 확인하지 못했습니다. 재고 업무는 계속할 수 있습니다.");
            }

            var json = await response.Content.ReadAsStringAsync();
            var offer = ParseLatestRelease(json);
            if (!offer.Found || string.IsNullOrWhiteSpace(offer.Sha256Url))
            {
                return offer;
            }

            return await AttachSetupSha256Async(client, offer);
        }
        catch (Exception ex) when (LooksLikeRateLimit(ex.Message))
        {
            return new LatestReleaseOffer(false, "", null, null, null, false, RateLimitUserMessage);
        }
        catch
        {
            return new LatestReleaseOffer(false, "", null, null, null, false, "업데이트 서버에 연결하지 못했습니다. 오프라인에서도 핵심 재고 기능은 동작합니다.");
        }
        finally
        {
            if (owns)
            {
                client.Dispose();
            }
        }
    }

    private static async Task<LatestReleaseOffer> AttachSetupSha256Async(HttpClient client, LatestReleaseOffer offer)
    {
        if (string.IsNullOrWhiteSpace(offer.Sha256Url))
        {
            return offer;
        }

        try
        {
            using var hashResponse = await client.GetAsync(offer.Sha256Url);
            if (!hashResponse.IsSuccessStatusCode)
            {
                // Setup.sha256 may be missing on older releases; still allow Setup download without hard fail.
                return offer with { HashRequired = false, Sha256Url = null };
            }

            var hashText = (await hashResponse.Content.ReadAsStringAsync()).Trim();
            var hex = hashText.Split(' ', '\n', '\t', '\r')[0];
            return offer with
            {
                Sha256Hex = hex,
                HashRequired = true,
                Message = $"{offer.Message}\nSHA256: {hex}"
            };
        }
        catch (Exception ex)
        {
            return offer with
            {
                HashRequired = false,
                Message = $"원인: 해시 파일을 읽지 못했습니다. {AppLog.Sanitize(ex.Message)}\n조치: 지금 버전을 유지합니다. 입고·출고는 계속하세요."
            };
        }
    }

    public static string Sha256Hex(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash);
    }

    public static async Task<PackageApplyResult> DownloadVerifyAndStageAsync(
        HttpClient client,
        Uri packageUrl,
        string expectedSha256Hex,
        string dbPath,
        string stagingFolder,
        string currentMarkerPath)
    {
        Directory.CreateDirectory(stagingFolder);
        var backupPath = Path.Combine(stagingFolder, "pre-update.db");
        var stagedPath = Path.Combine(stagingFolder, "update.bin");
        var currentBefore = File.Exists(currentMarkerPath) ? File.ReadAllBytes(currentMarkerPath) : "current"u8.ToArray();
        if (!File.Exists(currentMarkerPath))
        {
            File.WriteAllBytes(currentMarkerPath, currentBefore);
        }

        var backupTaken = false;
        try
        {
            if (File.Exists(dbPath))
            {
                BackupService.Backup(dbPath, backupPath);
                backupTaken = true;
            }

            EnsureUserAgent(client);
            using var response = await client.GetAsync(packageUrl);
            if (!response.IsSuccessStatusCode)
            {
                return Keep("패키지를 받지 못했습니다. 지금 실행 중인 프로그램을 그대로 씁니다.", backupTaken, currentMarkerPath, currentBefore);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            var actual = Sha256Hex(bytes);
            if (!actual.Equals(expectedSha256Hex.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(stagedPath))
                {
                    File.Delete(stagedPath);
                }

                return Keep("원인: 패키지 해시가 다릅니다.\n조치: 지금 버전을 유지합니다. 재고 업무는 계속하세요.", backupTaken, currentMarkerPath, currentBefore);
            }

            await File.WriteAllBytesAsync(stagedPath, bytes);
            File.WriteAllBytes(currentMarkerPath, currentBefore);
            return new PackageApplyResult(
                Applied: true,
                KeptCurrent: true,
                BackupTaken: backupTaken,
                Message: "검증된 패키지를 대기 폴더에 두었습니다. 실행 중인 프로그램은 바꾸지 않았습니다.",
                StagedPath: stagedPath);
        }
        catch (Exception ex)
        {
            return Keep($"원인: {AppLog.Sanitize(ex.Message)}\n조치: 지금 버전으로 계속 사용하세요.", backupTaken, currentMarkerPath, currentBefore);
        }
    }

    public static async Task<PackageApplyResult?> TryStageLatestIfPresentAsync(
        HttpClient client,
        string dbPath,
        string stagingFolder,
        string currentMarkerPath)
    {
        try
        {
            var offer = await InspectLatestAsync(client);
            if (!offer.Found || string.IsNullOrWhiteSpace(offer.PackageUrl) || string.IsNullOrWhiteSpace(offer.Sha256Hex))
            {
                return null;
            }

            return await DownloadVerifyAndStageAsync(
                client,
                new Uri(offer.PackageUrl),
                offer.Sha256Hex,
                dbPath,
                stagingFolder,
                currentMarkerPath);
        }
        catch
        {
            return null;
        }
    }

    private static PackageApplyResult Keep(string message, bool backupTaken, string currentMarkerPath, byte[] original)
    {
        File.WriteAllBytes(currentMarkerPath, original);
        return new PackageApplyResult(false, true, backupTaken, message, null);
    }

    private static void EnsureUserAgent(HttpClient client)
    {
        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SpringClinicInventory", "1.0"));
        }
    }
}

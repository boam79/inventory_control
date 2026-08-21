using Inventory.Core;
using Inventory.Infrastructure;
using System.Diagnostics;
using System.Net.Http;
using Velopack;
using Velopack.Sources;

namespace Inventory.App;

internal static class VelopackUpdater
{
    public const string GitHubRepoUrl = "https://github.com/boam79/inventory_control";

    public static Task<string> CheckAndDownloadAsync() =>
        ApplyFromButtonAsync(progress: null, applyAndRestart: true);

    public static async Task<string> CheckStatusAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var offer = await UpdateChecker.InspectLatestAsync(client);
            return UpdateChecker.StatusMessage(offer, ProductInfo.Version);
        }
        catch (Exception ex)
        {
            return $"원인: {AppLog.Sanitize(ex.Message)}\n조치: 입고·출고는 계속하세요. {UpdateChecker.ReleasesUrl}";
        }
    }

    public static async Task<string> ApplyFromButtonAsync(IProgress<string>? progress = null, bool applyAndRestart = true)
    {
        LatestReleaseOffer? offer = null;
        try
        {
            progress?.Report("최신 배포를 확인하는 중...");
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
            {
                offer = await UpdateChecker.InspectLatestAsync(client);
            }

            if (offer.Found)
            {
                progress?.Report(offer.Message);
            }

            UpdateManager mgr;
            try
            {
                var source = new GithubSource(GitHubRepoUrl, string.Empty, prerelease: false);
                mgr = new UpdateManager(source);
            }
            catch (Exception ex)
            {
                return NotInstalledOrInitFailed(ex, offer);
            }

            if (!mgr.IsInstalled)
            {
                return NotInstalledMessage(offer);
            }

            progress?.Report("설치본 업데이트를 확인하는 중...");
            UpdateInfo? info;
            try
            {
                info = await mgr.CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                return $"원인: 설치본 업데이트를 확인하지 못했습니다. {AppLog.Sanitize(ex.Message)}\n조치: 입고·출고는 계속하세요. {(offer?.PackageUrl ?? UpdateChecker.ReleasesUrl)}";
            }

            if (info is null)
            {
                if (offer is { Found: true }
                    && UpdateChecker.IsRemoteNewer(offer.VersionTag, ProductInfo.Version)
                    && !string.IsNullOrWhiteSpace(offer.PackageUrl))
                {
                    return await LaunchVerifiedSetupAsync(offer, progress);
                }

                return UpdateChecker.AfterVelopackNoUpdate(offer, ProductInfo.Version);
            }

            progress?.Report("업데이트 전 데이터베이스를 백업하는 중...");
            BackupBeforeUpdate();

            if (offer is { HashRequired: true } && string.IsNullOrWhiteSpace(offer.Sha256Hex))
            {
                return "원인: 해시 파일을 확인하지 못했습니다.\n조치: 패키지를 적용하지 않습니다. 입고·출고는 계속하세요.";
            }

            if (offer is { HashRequired: true, PackageUrl: not null } && !string.IsNullOrWhiteSpace(offer.Sha256Hex))
            {
                progress?.Report("설치 파일을 받아 SHA256을 확인하는 중...");
                var stage = StagingFolder();
                var marker = global::System.IO.Path.Combine(stage, "current.marker");
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
                var verified = await UpdateChecker.DownloadVerifyAndStageAsync(
                    client,
                    new Uri(offer.PackageUrl),
                    offer.Sha256Hex,
                    AppHost.DatabasePath,
                    stage,
                    marker);
                if (!verified.Applied)
                {
                    return verified.Message;
                }
            }

            var target = info.TargetFullRelease?.Version?.ToString() ?? offer?.VersionTag ?? ProductInfo.Version;
            var expectedNupkgHash = info.TargetFullRelease?.SHA256;
            progress?.Report($"버전 {target}을 받는 중...");
            await mgr.DownloadUpdatesAsync(info, percent =>
            {
                progress?.Report($"다운로드 중 {percent}% (버전 {target})");
            });

            if (!string.IsNullOrWhiteSpace(expectedNupkgHash))
            {
                var nupkg = FindDownloadedPackage(info.TargetFullRelease?.FileName);
                if (nupkg is not null)
                {
                    var actual = UpdateChecker.Sha256Hex(await global::System.IO.File.ReadAllBytesAsync(nupkg));
                    if (!UpdateChecker.HashesMatch(expectedNupkgHash, actual))
                    {
                        return "원인: 패키지 해시가 다릅니다.\n조치: 지금 버전을 유지합니다. 재고 업무는 계속하세요.";
                    }
                }
            }

            if (!applyAndRestart)
            {
                return $"버전 {target}을 받아 두었습니다. 적용하려면 업데이트를 다시 누르세요. 지금은 재고 업무를 계속할 수 있습니다.";
            }

            progress?.Report("업데이트를 적용하고 다시 시작합니다...");
            mgr.ApplyUpdatesAndRestart(info);
            return $"버전 {target}을 적용하고 다시 시작합니다.";
        }
        catch (Exception ex)
        {
            return $"원인: {AppLog.Sanitize(ex.Message)}\n조치: 지금 프로그램을 그대로 씁니다. 입고·출고는 계속하세요. {(offer?.PackageUrl ?? UpdateChecker.ReleasesUrl)}";
        }
    }

    public static string NotInstalledMessage(LatestReleaseOffer? offer)
    {
        var download = offer?.PackageUrl ?? UpdateChecker.SetupDownloadUrl(offer?.VersionTag);
        var version = string.IsNullOrWhiteSpace(offer?.VersionTag) ? "" : $" 최신 {offer!.VersionTag}.";
        return $"원인: 지금 실행은 설치본이 아닙니다(개발 실행 포함).{version}\n조치: 자동 업데이트는 설치본에서만 됩니다. Setup.exe를 설치한 뒤 그 프로그램에서 업데이트를 누르세요.\n다운로드: {download}";
    }

    private static string NotInstalledOrInitFailed(Exception ex, LatestReleaseOffer? offer) =>
        $"원인: 업데이트 구성을 시작하지 못했습니다. {AppLog.Sanitize(ex.Message)}\n조치: 입고·출고는 계속하세요. 설치본에서만 자동 업데이트됩니다.\n다운로드: {offer?.PackageUrl ?? UpdateChecker.ReleasesUrl}";

    private static async Task<string> LaunchVerifiedSetupAsync(LatestReleaseOffer offer, IProgress<string>? progress)
    {
        if (offer is { HashRequired: true } && string.IsNullOrWhiteSpace(offer.Sha256Hex))
        {
            return "원인: 해시 파일을 확인하지 못했습니다.\n조치: 패키지를 적용하지 않습니다. 입고·출고는 계속하세요.\n"
                   + UpdateChecker.AfterVelopackNoUpdate(offer, ProductInfo.Version);
        }

        BackupBeforeUpdate();
        progress?.Report($"설치 파일 {offer.VersionTag}을 받는 중...");
        var stage = StagingFolder();
        var marker = global::System.IO.Path.Combine(stage, "current.marker");
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        var expectedHash = offer.Sha256Hex ?? "";
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return UpdateChecker.AfterVelopackNoUpdate(offer, ProductInfo.Version);
        }

        var verified = await UpdateChecker.DownloadVerifyAndStageAsync(
            client,
            new Uri(offer.PackageUrl!),
            expectedHash,
            AppHost.DatabasePath,
            stage,
            marker);
        if (!verified.Applied || string.IsNullOrWhiteSpace(verified.StagedPath))
        {
            return verified.Message + "\n" + UpdateChecker.AfterVelopackNoUpdate(offer, ProductInfo.Version);
        }

        var setupPath = global::System.IO.Path.Combine(stage, UpdateChecker.SetupFileName);
        global::System.IO.File.Copy(verified.StagedPath, setupPath, overwrite: true);
        progress?.Report("설치 프로그램을 실행합니다...");
        Process.Start(new ProcessStartInfo
        {
            FileName = setupPath,
            UseShellExecute = true
        });
        return $"{offer.VersionTag} 설치 프로그램을 실행했습니다. 안내가 끝나면 프로그램을 다시 여세요.";
    }

    private static void BackupBeforeUpdate()
    {
        var folder = global::System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpringClinicInventory",
            "backups");
        BackupService.RunDailyBackupIfNeeded(AppHost.DatabasePath, folder, DateTime.Today);
        if (global::System.IO.File.Exists(AppHost.DatabasePath))
        {
            BackupService.Backup(
                AppHost.DatabasePath,
                global::System.IO.Path.Combine(folder, $"pre-update-{DateTime.Now:yyyyMMdd-HHmmss}.db"));
        }
    }

    private static string StagingFolder() =>
        global::System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpringClinicInventory",
            "updates");

    private static string? FindDownloadedPackage(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new[]
        {
            global::System.IO.Path.Combine(local, "SpringClinic.Inventory", "packages"),
            global::System.IO.Path.Combine(local, ProductInfo.PackId, "packages"),
            StagingFolder()
        };
        foreach (var root in roots)
        {
            if (!global::System.IO.Directory.Exists(root))
            {
                continue;
            }

            var match = global::System.IO.Directory.GetFiles(root, fileName, global::System.IO.SearchOption.AllDirectories).FirstOrDefault();
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}

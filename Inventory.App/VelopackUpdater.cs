using Inventory.Core;
using Inventory.Infrastructure;
using Velopack;
using Velopack.Sources;

namespace Inventory.App;

internal static class VelopackUpdater
{
    public static async Task<string> CheckAndDownloadAsync()
    {
        try
        {
            var source = new GithubSource("https://github.com/boam79/inventory_control", string.Empty, prerelease: false);
            var mgr = new UpdateManager(source);
            if (!mgr.IsInstalled)
            {
                return "설치본이 아니면 Velopack 자동적용은 건너뜁니다. GitHub Releases에서 Setup.exe를 받으세요.";
            }

            var folder = global::System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpringClinicInventory",
                "backups");
            BackupService.RunDailyBackupIfNeeded(AppHost.DatabasePath, folder, DateTime.Today);
            if (System.IO.File.Exists(AppHost.DatabasePath))
            {
                BackupService.Backup(
                    AppHost.DatabasePath,
                    global::System.IO.Path.Combine(folder, $"pre-update-{DateTime.Today:yyyyMMdd}.db"));
            }

            var info = await mgr.CheckForUpdatesAsync();
            if (info is null)
            {
                return "새 설치 버전이 없습니다. 재고 업무를 계속하세요.";
            }

            await mgr.DownloadUpdatesAsync(info);
            return "업데이트를 받아 두었습니다. 프로그램을 다시 시작하면 적용됩니다. 지금은 재고 업무를 계속할 수 있습니다.";
        }
        catch (Exception ex)
        {
            return $"원인: {AppLog.Sanitize(ex.Message)}\n조치: 설치본은 그대로 둡니다. {UpdateChecker.ReleasesUrl}";
        }
    }
}

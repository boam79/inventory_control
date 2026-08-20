using Microsoft.Data.Sqlite;

namespace Inventory.Infrastructure;

public static class BackupService
{
    public static void Backup(string dbPath, string destinationFile)
    {
        SqliteConnection.ClearAllPools();
        var folder = Path.GetDirectoryName(destinationFile);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.Copy(dbPath, destinationFile, overwrite: true);
    }

    public static void Restore(string backupFile, string dbPath)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath))
        {
            File.Copy(dbPath, dbPath + ".pre-restore", overwrite: true);
        }

        File.Copy(backupFile, dbPath, overwrite: true);
        InventoryDatabase.Initialize(dbPath);
    }

    public static string? RunDailyBackupIfNeeded(string dbPath, string backupFolder, DateTime today, int keepCount = 14)
    {
        Directory.CreateDirectory(backupFolder);
        var stamp = Path.Combine(backupFolder, $"inventory-{today:yyyyMMdd}.db");
        string? created = null;
        if (!File.Exists(stamp) && File.Exists(dbPath))
        {
            Backup(dbPath, stamp);
            created = stamp;
        }

        var old = Directory.GetFiles(backupFolder, "inventory-*.db")
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .Skip(Math.Max(1, keepCount));
        foreach (var path in old)
        {
            File.Delete(path);
        }

        return created;
    }
}

public static class IntegrityCheck
{
    public static string Run(string dbPath)
    {
        try
        {
            if (!File.Exists(dbPath))
            {
                return "원인: 데이터베이스 파일이 없습니다.\n조치: 백업에서 복원하거나 프로그램을 다시 시작하세요.";
            }

            using var db = InventoryDatabase.CreateContext(dbPath);
            if (!db.Database.CanConnect())
            {
                return "원인: 데이터베이스에 연결할 수 없습니다.\n조치: 백업에서 복원을 시도하세요.";
            }

            if (!db.AppMeta.Any())
            {
                return "원인: 메타 정보가 없습니다.\n조치: 프로그램을 다시 시작한 뒤에도 같으면 복원하세요.";
            }

            return "정상";
        }
        catch (Exception ex)
        {
            return $"원인: {AppLog.Sanitize(ex.Message)}\n조치: 백업 파일을 확인한 뒤 복원하세요.";
        }
    }
}

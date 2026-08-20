using Inventory.Infrastructure;

namespace Inventory.Tests;

public sealed class AuditSettingsLogTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"springclinic-audit-{Guid.NewGuid():N}.db");
    private readonly string _logDir = Path.Combine(
        Path.GetTempPath(),
        $"springclinic-log-{Guid.NewGuid():N}");

    public AuditSettingsLogTests() => InventoryDatabase.Initialize(_dbPath);

    [Fact]
    public void Item_name_change_writes_before_and_after()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        new AuditService(db).Write(
            "admin",
            "Item.Rename",
            "Item",
            "M001",
            "주사기",
            "일회용 주사기",
            "명칭 정리");

        var row = db.AuditLogs.Single();
        Assert.Equal("주사기", row.BeforeValue);
        Assert.Equal("일회용 주사기", row.AfterValue);
        Assert.Equal("admin", row.UserName);
        Assert.Equal("M001", row.EntityId);
        Assert.False(string.IsNullOrWhiteSpace(row.AppVersion));
    }

    [Fact]
    public void Settings_survive_new_context()
    {
        using (var db = InventoryDatabase.CreateContext(_dbPath))
        {
            var store = new SettingsStore(db);
            store.Set(SettingsStore.ExpiryWarningDays, "90");
            store.Set(SettingsStore.BackupFolder, @"D:\backup");
            store.Set(SettingsStore.PeriodYear, "2026");
            store.Set(SettingsStore.PeriodMonth, "8");
        }

        using (var db = InventoryDatabase.CreateContext(_dbPath))
        {
            var store = new SettingsStore(db);
            Assert.Equal("90", store.Get(SettingsStore.ExpiryWarningDays));
            Assert.Equal(@"D:\backup", store.Get(SettingsStore.BackupFolder));
            Assert.Equal(2026, store.GetInt(SettingsStore.PeriodYear, 0));
            Assert.Equal(8, store.GetInt(SettingsStore.PeriodMonth, 0));
            Assert.True(db.Settings.Single(s => s.Key == SettingsStore.ExpiryWarningDays).Version >= 1);
        }
    }

    [Fact]
    public void Exception_is_logged_and_does_not_escape_tryrun()
    {
        using var log = AppLog.CreateFileLogger(_logDir);
        var survived = AppLog.TryRun(() => throw new InvalidOperationException("boom-stack"), log);
        Assert.False(survived);
        log.Dispose();

        var files = Directory.GetFiles(_logDir, "*.log");
        Assert.NotEmpty(files);
        var text = File.ReadAllText(files[0]);
        Assert.Contains("boom-stack", text);
        Assert.Contains("InvalidOperationException", text);
    }

    [Fact]
    public void Sanitize_strips_identifier_like_text()
    {
        var cleaned = AppLog.Sanitize("환자 홍길동 900101-1234567");
        Assert.DoesNotContain("홍길동", cleaned);
        Assert.DoesNotContain("900101-1234567", cleaned);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm", _dbPath + "-journal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        if (Directory.Exists(_logDir))
        {
            Directory.Delete(_logDir, true);
        }
    }
}

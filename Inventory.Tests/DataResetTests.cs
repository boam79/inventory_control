using Inventory.Core;
using Inventory.Infrastructure;

namespace Inventory.Tests;

public sealed class DataResetTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"springclinic-reset-{Guid.NewGuid():N}.db");

    public DataResetTests() => InventoryDatabase.Initialize(_dbPath);

    [Fact]
    public void Empty_reset_clears_inventory_but_preserves_settings_and_users()
    {
        using (var db = InventoryDatabase.CreateContext(_dbPath))
        {
            var svc = new InventoryService(db, "admin");
            svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
            svc.SaveOpeningDraft("M001", "OPEN", 20, new DateTime(2026, 1, 1), new DateTime(2027, 1, 1));
            svc.ConfirmOpening("M001");
            svc.Receive(
                new DateTime(2026, 2, 1),
                null,
                "R1",
                [new ReceiptLineRequest { ItemCode = "M001", Quantity = 5, UnitPrice = 80, LotNumber = "L1", ExpiryDate = new DateTime(2027, 6, 1) }]);
            db.Departments.Add(new Department { Name = "외래" });
            db.Suppliers.Add(new Supplier { Name = "공급사" });
            new SettingsStore(db).Set(SettingsStore.FontScale, "large");
            new AuthenticationService(db).CreateUser("nurse", "nurse-pass", UserRole.DepartmentUser);
            db.SaveChanges();
        }

        DataResetResult result;
        using (var db = InventoryDatabase.CreateContext(_dbPath))
        {
            result = DataResetService.Reset(db, DataResetMode.Empty, new DateTime(2026, 8, 20), "admin");
        }

        Assert.True(result.Applied, result.Message);
        Assert.Equal(0, result.ItemCount);
        Assert.Equal(0, result.DocumentCount);

        using var after = InventoryDatabase.CreateContext(_dbPath);
        Assert.True(DataResetService.IsInventoryEmpty(after));
        Assert.Equal("large", new SettingsStore(after).Get(SettingsStore.FontScale));
        Assert.True(after.Users.Any(u => u.UserName == "admin" || u.UserName == "nurse"));
        Assert.Contains(after.AuditLogs, log => log.Action == "DataReset.Empty");
    }

    [Fact]
    public void Sample_seed_reset_replaces_inventory_with_demo_data()
    {
        using (var db = InventoryDatabase.CreateContext(_dbPath))
        {
            var svc = new InventoryService(db, "admin");
            svc.CreateItem("OLD1", "구품목", "소모품", "개", "개", 1);
            svc.SaveOpeningDraft("OLD1", "OPEN", 1, new DateTime(2026, 1, 1), new DateTime(2027, 1, 1));
            svc.ConfirmOpening("OLD1");
        }

        DataResetResult result;
        using (var db = InventoryDatabase.CreateContext(_dbPath))
        {
            result = DataResetService.Reset(db, DataResetMode.SampleSeed, new DateTime(2026, 8, 20), "admin", sampleItemCount: 40);
        }

        Assert.True(result.Applied, result.Message);
        Assert.Equal(40, result.ItemCount);
        Assert.True(result.DocumentCount > 0);

        using var after = InventoryDatabase.CreateContext(_dbPath);
        Assert.False(after.Items.Any(i => i.Code == "OLD1"));
        Assert.Equal(40, after.Items.Count());
        Assert.Contains(after.AuditLogs, log => log.Action == "DataReset.SampleSeed");
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
    }
}

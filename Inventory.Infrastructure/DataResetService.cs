using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure;

public enum DataResetMode
{
    Empty,
    SampleSeed
}

public sealed record DataResetResult(
    bool Applied,
    int ItemCount,
    int DocumentCount,
    string Message);

public static class DataResetService
{
    public const string ConfirmPhrase = "초기화";

    public static DataResetResult Reset(
        InventoryDbContext db,
        DataResetMode mode,
        DateTime today,
        string actor = "system",
        int? sampleItemCount = null)
    {
        var beforeItems = db.Items.Count();
        var beforeDocs = DemoSeedService.CountBusinessDocuments(db);

        if (mode == DataResetMode.SampleSeed)
        {
            var seeded = DemoSeedService.ReplaceSample(db, today, actor, sampleItemCount);
            if (!seeded.Applied)
            {
                return new DataResetResult(false, seeded.ItemCount, seeded.DocumentCount, seeded.Message);
            }

            new AuditService(db).Write(
                actor,
                "DataReset.SampleSeed",
                "Database",
                "inventory",
                $"items={beforeItems},docs={beforeDocs}",
                $"items={seeded.ItemCount},docs={seeded.DocumentCount}",
                "설정에서 샘플 데이터로 재설정");

            return new DataResetResult(
                true,
                seeded.ItemCount,
                seeded.DocumentCount,
                $"샘플 데이터로 재설정했습니다. 품목 {seeded.ItemCount:N0}개, 거래 {seeded.DocumentCount:N0}건입니다.");
        }

        ClearInventoryData(db);
        // Prevent MainWindow.TryAutoSeedIfEmpty from re-filling demo data after reboot.
        DemoSeedService.SetAutoSeedSuppressed(db, suppressed: true);
        new AuditService(db).Write(
            actor,
            "DataReset.Empty",
            "Database",
            "inventory",
            $"items={beforeItems},docs={beforeDocs}",
            "items=0,docs=0",
            "설정에서 완전 초기화");

        return new DataResetResult(
            true,
            0,
            0,
            "모든 재고·품목·거래 데이터를 삭제했습니다. 품목을 새로 등록하거나 Excel로 가져올 수 있습니다.");
    }

    public static void ClearInventoryData(InventoryDbContext db)
    {
        db.ChangeTracker.Clear();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF");
        db.Database.ExecuteSqlRaw("DELETE FROM StockLines");
        db.Database.ExecuteSqlRaw("DELETE FROM Documents");
        db.Database.ExecuteSqlRaw("DELETE FROM Lots");
        db.Database.ExecuteSqlRaw("DELETE FROM MonthCloses");
        db.Database.ExecuteSqlRaw("DELETE FROM Items");
        db.Database.ExecuteSqlRaw("DELETE FROM Departments");
        db.Database.ExecuteSqlRaw("DELETE FROM Suppliers");
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON");
        // Flush WAL so Empty reset is durable before process exit / reboot.
        db.Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE)");
        db.SaveChanges();
        db.ChangeTracker.Clear();
    }

    public static bool IsInventoryEmpty(InventoryDbContext db) =>
        !db.Items.Any()
        && !db.Documents.Any()
        && !db.Lots.Any()
        && !db.StockLines.Any()
        && !db.MonthCloses.Any();
}

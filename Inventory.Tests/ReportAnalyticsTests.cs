using Inventory.Infrastructure;

namespace Inventory.Tests;

public sealed class ReportAnalyticsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"springclinic-report-{Guid.NewGuid():N}.db");

    public ReportAnalyticsTests() => InventoryDatabase.Initialize(_dbPath);

    [Fact]
    public void Year_month_day_filters_do_not_merge_different_years()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "주사", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 100, new DateTime(2025, 1, 1), new DateTime(2027, 1, 1));
        svc.ConfirmOpening("M001");
        svc.Receive(new DateTime(2025, 3, 10), null, "R25",
            [new ReceiptLineRequest { ItemCode = "M001", Quantity = 10, UnitPrice = 80, LotNumber = "A", ExpiryDate = new DateTime(2027, 1, 1) }]);
        svc.Receive(new DateTime(2026, 3, 10), null, "R26",
            [new ReceiptLineRequest { ItemCode = "M001", Quantity = 4, UnitPrice = 80, LotNumber = "B", ExpiryDate = new DateTime(2027, 1, 1) }]);
        svc.Issue(new DateTime(2025, 3, 11), null, [new IssueLineRequest { ItemCode = "M001", Quantity = 3, LotNumber = "A" }]);
        svc.Issue(new DateTime(2026, 3, 11), null, [new IssueLineRequest { ItemCode = "M001", Quantity = 1, LotNumber = "B" }]);

        var mar2025 = ReportAnalytics.Query(db, ReportPeriodKind.Month, new DateTime(2025, 3, 15), ReportDimension.Item);
        var mar2026 = ReportAnalytics.Query(db, ReportPeriodKind.Month, new DateTime(2026, 3, 15), ReportDimension.Item);
        Assert.Equal("2025-03", mar2025.Single().PeriodLabel);
        Assert.Equal("2026-03", mar2026.Single().PeriodLabel);
        Assert.Equal(3m, mar2025.Single().IssueQty);
        Assert.Equal(1m, mar2026.Single().IssueQty);

        var day = ReportAnalytics.Query(db, ReportPeriodKind.Day, new DateTime(2026, 3, 11), ReportDimension.Item);
        Assert.Equal("2026-03-11", day.Single().PeriodLabel);
        Assert.Equal(1m, day.Single().IssueQty);

        var year = ReportAnalytics.Query(db, ReportPeriodKind.Year, new DateTime(2025, 12, 1), ReportDimension.Category);
        Assert.Equal("2025", year.Single().PeriodLabel);
        Assert.Equal(3m, year.Single().IssueQty);

        var custom = ReportAnalytics.Query(
            db,
            ReportPeriodKind.Custom,
            DateTime.Today,
            ReportDimension.Item,
            new DateTime(2025, 3, 1),
            new DateTime(2025, 3, 31));
        Assert.Equal(3m, custom.Single().IssueQty);
        Assert.Contains("2025-03-01", custom.Single().PeriodLabel);
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

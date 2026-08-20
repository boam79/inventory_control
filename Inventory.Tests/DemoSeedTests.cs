using Inventory.Infrastructure;

namespace Inventory.Tests;

public sealed class DemoSeedTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"springclinic-seed-{Guid.NewGuid():N}.db");

    public DemoSeedTests() => InventoryDatabase.Initialize(_dbPath);

    [Fact]
    public void Default_item_count_is_ten_thousand()
    {
        Assert.Equal(10_000, DemoSeedService.DefaultItemCount);
        Assert.True(DemoSeedService.SeasonalFactor("소독", 1) > DemoSeedService.SeasonalFactor("소독", 7));
        Assert.True(DemoSeedService.SeasonalFactor("수액", 7) > DemoSeedService.SeasonalFactor("수액", 1));
    }

    [Fact]
    public void Auto_seed_runs_only_when_business_documents_are_zero()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        Assert.True(DemoSeedService.ShouldAutoSeed(db));
        var first = DemoSeedService.TryAutoSeed(db, new DateTime(2026, 8, 20), "admin", 40);
        Assert.True(first.Applied, first.Message);
        Assert.False(DemoSeedService.ShouldAutoSeed(db));

        var again = DemoSeedService.TryAutoSeed(db, new DateTime(2026, 8, 20), "admin");
        Assert.False(again.Applied);
        Assert.Contains("거래", again.Message);
    }

    [Fact]
    public void Auto_seed_skips_when_a_real_receipt_exists()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 20, new DateTime(2026, 1, 1), new DateTime(2027, 1, 1));
        svc.ConfirmOpening("M001");
        svc.Receive(
            new DateTime(2026, 2, 1),
            null,
            "REAL-1",
            [new ReceiptLineRequest { ItemCode = "M001", Quantity = 1, UnitPrice = 80, LotNumber = "LIVE", ExpiryDate = new DateTime(2027, 6, 1) }]);
        Assert.False(DemoSeedService.ShouldAutoSeed(db));
        var result = DemoSeedService.TryAutoSeed(db, new DateTime(2026, 8, 20));
        Assert.False(result.Applied);
        Assert.Equal(1, db.Documents.Count(d => d.Type == DocumentType.Receipt));
    }

    [Fact]
    public void Refuses_when_business_documents_already_exist_without_force()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 100, new DateTime(2026, 1, 1), new DateTime(2027, 1, 1));
        svc.ConfirmOpening("M001");
        for (var i = 0; i < DemoSeedService.BusyThreshold; i++)
        {
            svc.Receive(
                new DateTime(2026, 2, 1),
                null,
                $"R{i}",
                [new ReceiptLineRequest { ItemCode = "M001", Quantity = 1, UnitPrice = 80, LotNumber = $"L{i}", ExpiryDate = new DateTime(2027, 6, 1) }]);
        }

        var result = DemoSeedService.Generate(db, new DateTime(2026, 8, 20), force: false, itemCount: 40);
        Assert.False(result.Applied);
        Assert.Contains("이미 거래", result.Message);
        Assert.Equal(DemoSeedService.BusyThreshold, db.Documents.Count(d => d.Type == DocumentType.Receipt));
    }

    [Fact]
    public void Reduced_seed_has_seasonal_months_non_negative_stock_and_separate_years()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var today = new DateTime(2026, 8, 20);
        var result = DemoSeedService.Generate(db, today, force: false, itemCount: 40);
        Assert.True(result.Applied, result.Message);
        Assert.Equal(40, result.ItemCount);
        Assert.True(result.DocumentCount > 40);

        var svc = new InventoryService(db, "t");
        foreach (var item in db.Items.ToList())
        {
            var onHand = svc.GetOnHand(item.Code);
            Assert.True(onHand is null or >= 0, $"{item.Code} 재고가 음수입니다.");
            Assert.All(svc.LotsForItem(item.Code), lot => Assert.True(lot.Quantity >= 0));
        }

        Assert.Contains(db.StockLines, l => l.UnitPrice > 0);
        var series = DashboardMetrics.TrailingMonthlyIssues(db, today, 13);
        Assert.Equal(13, series.Count);
        var aug2025 = series.Single(s => s.Year == 2025 && s.Month == 8);
        var aug2026 = series.Single(s => s.Year == 2026 && s.Month == 8);
        Assert.True(aug2025.Qty > 0);
        Assert.True(aug2026.Qty > 0);
        var jan = series.Single(s => s.Year == 2026 && s.Month == 1);
        var jul = series.Single(s => s.Year == 2026 && s.Month == 7);
        Assert.NotEqual(jan.Qty, jul.Qty);
        var forecast = UsageForecast.Predict(series.Select(s => s.Qty).ToList());
        Assert.True(forecast.Available, forecast.Warning);
        var kpi = DashboardMetrics.Build(db, svc, today);
        Assert.True(kpi.ActiveItems >= 5);
        Assert.True(kpi.MonthIssueQty > 0);
        Assert.True(kpi.MonthPurchaseAmount > 0);
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

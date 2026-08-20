using ClosedXML.Excel;
using Inventory.Infrastructure;

namespace Inventory.Tests;

public sealed class OperationsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"springclinic-ops-{Guid.NewGuid():N}.db");
    private readonly string _work = Path.Combine(Path.GetTempPath(), $"springclinic-ops-work-{Guid.NewGuid():N}");

    public OperationsTests()
    {
        Directory.CreateDirectory(_work);
        InventoryDatabase.Initialize(_dbPath);
    }

    [Fact]
    public void Excel_preview_does_not_change_database()
    {
        var sample = CreateSampleWorkbook();
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var before = db.Items.Count();
        var preview = ExcelCatalog.PreviewMaster(sample);
        Assert.Equal(5, preview.ItemCodes.Count);
        Assert.Equal(before, db.Items.Count());
        Assert.DoesNotContain("거래행", preview.ItemCodes);
    }

    [Fact]
    public void Excel_master_import_loads_five_items_and_zero_transactions()
    {
        var sample = CreateSampleWorkbook();
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var result = ExcelCatalog.ImportMaster(db, sample, includeOpening: false);
        Assert.Equal(5, result.ImportedItems);
        Assert.Equal(0, result.TransactionRows);
        Assert.Equal(5, db.Items.Count());
        Assert.Null(new InventoryService(db, "t").GetOnHand("M001"));
        Assert.Equal(0, db.Documents.Count());
    }

    [Fact]
    public void Desktop_sample_workbook_imports_five_masters_and_no_transactions()
    {
        const string sample = @"c:\Users\tttt\Desktop\스프링의원_월별_구매사용_재고관리_프로그램.xlsx";
        if (!File.Exists(sample))
        {
            return;
        }

        var preview = ExcelCatalog.PreviewMaster(sample);
        using var db = InventoryDatabase.CreateContext(_dbPath);
        Assert.Equal(0, db.Items.Count());
        Assert.Equal(5, preview.ItemCodes.Count);
        var result = ExcelCatalog.ImportMaster(db, sample, includeOpening: false);
        Assert.Equal(5, result.ImportedItems);
        Assert.Equal(0, result.TransactionRows);
        Assert.Equal(5, db.Items.Count());
        Assert.Null(new InventoryService(db, "t").GetOnHand("M001"));
    }

    [Fact]
    public void Excel_master_plus_opening_keeps_blank_opening_unset()
    {
        var sample = CreateSampleWorkbook(openingForM001: null);
        using var db = InventoryDatabase.CreateContext(_dbPath);
        ExcelCatalog.ImportMaster(db, sample, includeOpening: true);
        var svc = new InventoryService(db, "t");
        Assert.Null(svc.GetOnHand("M001"));
        Assert.Equal(OpeningStatus.Unset, svc.GetOpeningStatus("M001"));
    }

    [Fact]
    public void Excel_export_has_headers_and_numeric_rows()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 7, DateTime.Today.AddDays(-1), DateTime.Today.AddDays(20));
        svc.ConfirmOpening("M001");
        var path = Path.Combine(_work, "export.xlsx");
        ExcelCatalog.ExportStock(db, path);
        using var wb = new XLWorkbook(path);
        var sheet = wb.Worksheet("재고현황");
        Assert.Equal("품목코드", sheet.Cell(1, 1).GetString());
        Assert.Equal("M001", sheet.Cell(2, 1).GetString());
        Assert.Equal(7m, sheet.Cell(2, 3).GetValue<decimal>());
    }

    [Fact]
    public void Backup_restore_keeps_item_count_and_on_hand()
    {
        using (var db = InventoryDatabase.CreateContext(_dbPath))
        {
            var svc = new InventoryService(db, "admin");
            svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
            svc.SaveOpeningDraft("M001", "OPEN", 4, DateTime.Today.AddDays(-1), DateTime.Today.AddDays(20));
            svc.ConfirmOpening("M001");
        }

        var backup = Path.Combine(_work, "bak.db");
        BackupService.Backup(_dbPath, backup);
        using (var db = InventoryDatabase.CreateContext(_dbPath))
        {
            new InventoryService(db, "admin").CreateItem("M002", "후추가", "소모품", "개", "개", 1);
        }

        BackupService.Restore(backup, _dbPath);
        using var restored = InventoryDatabase.CreateContext(_dbPath);
        var svc2 = new InventoryService(restored, "admin");
        Assert.Equal(1, restored.Items.Count());
        Assert.Equal(4m, svc2.GetOnHand("M001"));
    }

    [Fact]
    public void Daily_backup_runs_once_per_day()
    {
        var folder = Path.Combine(_work, "auto");
        var first = BackupService.RunDailyBackupIfNeeded(_dbPath, folder, new DateTime(2026, 8, 20), keepCount: 3);
        var second = BackupService.RunDailyBackupIfNeeded(_dbPath, folder, new DateTime(2026, 8, 20), keepCount: 3);
        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Single(Directory.GetFiles(folder, "inventory-*.db"));
    }

    [Fact]
    public void Forecast_marks_short_history_and_returns_sma_otherwise()
    {
        var shortResult = UsageForecast.Predict(new decimal[] { 1, 2 });
        Assert.False(shortResult.Available);
        Assert.Contains("데이터 부족", shortResult.Warning);
        var ok = UsageForecast.Predict(new decimal[] { 10, 12, 11 });
        Assert.True(ok.Available);
        Assert.Equal("SMA3", ok.ModelName);
        Assert.Equal(3, ok.Future.Count);
        var ssa = UsageForecast.Predict(new decimal[] { 10, 12, 11, 13, 12, 14, 15, 13 });
        Assert.True(ssa.Available);
        Assert.Equal(3, ssa.Future.Count);
        Assert.Contains(ssa.ModelName, new[] { "ML.NET-SSA", "SMA3" });
        var onnx = UsageForecast.Predict(new decimal[] { 1, 2, 3 }, new DisabledOnnxForecastEngine());
        Assert.NotEqual("ONNX", onnx.ModelName);
    }

    [Fact]
    public void Forecast_does_not_change_on_hand()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 9, DateTime.Today.AddDays(-1), DateTime.Today.AddDays(20));
        svc.ConfirmOpening("M001");
        _ = UsageForecast.Predict(new decimal[] { 1, 2, 3, 4, 5, 6 });
        Assert.Equal(9m, svc.GetOnHand("M001"));
    }

    [Fact]
    public async Task Update_check_failure_does_not_throw()
    {
        using var handler = new StubHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") };
        var result = await UpdateChecker.CheckAsync(client);
        Assert.False(result.Checked);
        Assert.Contains("재고", result.Message);
        Assert.Equal(UpdateChecker.ReleasesUrl, result.FeedUrl);
    }

    [Fact]
    public void Integrity_reports_missing_file_in_korean()
    {
        var missing = Path.Combine(_work, "no-such.db");
        var text = IntegrityCheck.Run(missing);
        Assert.StartsWith("원인:", text);
        Assert.Contains("조치:", text);
    }

    [Fact]
    public void Integrity_ok_for_initialized_database()
    {
        Assert.Equal("정상", IntegrityCheck.Run(_dbPath));
    }

    [Fact]
    public void Dashboard_kpi_matches_fixture()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 3, DateTime.Today.AddDays(-2), DateTime.Today.AddDays(10));
        svc.ConfirmOpening("M001");
        var supplier = svc.CreateSupplier("A사");
        svc.Receive(DateTime.Today, supplier.Id, "R1",
        [
            new ReceiptLineRequest { ItemCode = "M001", Quantity = 2, UnitPrice = 50, LotNumber = "N", ExpiryDate = DateTime.Today.AddDays(40) }
        ]);
        svc.Issue(DateTime.Today, null, [new IssueLineRequest { ItemCode = "M001", Quantity = 1, LotNumber = "OPEN" }]);
        var kpi = DashboardMetrics.Build(db, svc, DateTime.Today);
        Assert.Equal(1, kpi.ActiveItems);
        Assert.Equal(1, kpi.ReorderItems);
        Assert.Equal(100m, kpi.MonthPurchaseAmount);
        Assert.Equal(1m, kpi.MonthIssueQty);
        var bars = DashboardMetrics.MonthlyIssueBars(db, DateTime.Today.Year);
        Assert.All(bars, b => Assert.True(b.Qty > 0));
        Assert.DoesNotContain(bars, b => b.Month != DateTime.Today.Month && b.Qty > 0 && false);
    }

    private string CreateSampleWorkbook(decimal? openingForM001 = null)
    {
        var path = Path.Combine(_work, $"sample-{Guid.NewGuid():N}.xlsx");
        using var wb = new XLWorkbook();
        var items = wb.AddWorksheet("품목마스터");
        items.Cell(1, 1).Value = "품목마스터";
        items.Cell(2, 1).Value = "품목코드";
        items.Cell(2, 2).Value = "품목명";
        items.Cell(2, 3).Value = "분류";
        items.Cell(2, 4).Value = "규격/단위";
        items.Cell(2, 5).Value = "구입가격";
        items.Cell(2, 8).Value = "최소재고";
        var rows = new[]
        {
            ("M001", "주사기", "소모품"),
            ("M002", "수액세트", "소모품"),
            ("M003", "PICC 관련 소모품", "시술재료"),
            ("M004", "Chemoport 관련 소모품", "시술재료"),
            ("M005", "초음파 젤", "소모품")
        };
        for (var i = 0; i < rows.Length; i++)
        {
            items.Cell(3 + i, 1).Value = rows[i].Item1;
            items.Cell(3 + i, 2).Value = rows[i].Item2;
            items.Cell(3 + i, 3).Value = rows[i].Item3;
            items.Cell(3 + i, 4).Value = "개";
            items.Cell(3 + i, 5).Value = 0;
            items.Cell(3 + i, 8).Value = 10;
        }

        items.Cell(20, 1).Value = "";
        items.Cell(20, 4).Value = "=A3";

        var stock = wb.AddWorksheet("재고현황");
        stock.Cell(1, 1).Value = "품목코드";
        stock.Cell(1, 5).Value = "기초재고";
        stock.Cell(2, 1).Value = "M001";
        if (openingForM001 is { } qty)
        {
            stock.Cell(2, 5).Value = qty;
        }

        wb.SaveAs(path);
        return path;
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

        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}

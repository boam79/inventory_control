using Inventory.Core;
using Inventory.Infrastructure;

namespace Inventory.Tests;

public sealed class InventoryServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"springclinic-inv-{Guid.NewGuid():N}.db");

    public InventoryServiceTests() => InventoryDatabase.Initialize(_dbPath);

    [Fact]
    public void Duplicate_item_code_is_rejected_and_inactive_item_is_hidden_from_issue_list()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        Assert.Throws<InvalidOperationException>(() => svc.CreateItem("M001", "다른", "소모품", "개", "개", 1));
        svc.SaveOpeningDraft("M001", "L1", 5, DateTime.Today, DateTime.Today.AddDays(30));
        svc.ConfirmOpening("M001");
        svc.DeactivateItem("M001");
        Assert.DoesNotContain(svc.ItemsAvailableForIssue(), i => i.Code == "M001");
        Assert.Throws<InvalidOperationException>(() => svc.DeleteItem("M001"));
    }

    [Fact]
    public void Department_and_supplier_require_name_and_support_search()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        Assert.Throws<InvalidOperationException>(() => svc.CreateDepartment(" "));
        svc.CreateDepartment("외래");
        svc.CreateSupplier("대한의료");
        Assert.Contains(svc.SearchDepartments("외"), d => d.Name == "외래");
        Assert.Contains(svc.SearchSuppliers("대한"), s => s.Name == "대한의료");
        var dept = svc.SearchDepartments("외래").Single();
        svc.DeactivateDepartment(dept.Id);
        Assert.False(db.Departments.Single().IsActive);
    }

    [Fact]
    public void Unset_opening_is_not_zero()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        Assert.Null(svc.GetOnHand("M001"));
        Assert.Equal(OpeningStatus.Unset, svc.GetOpeningStatus("M001"));
    }

    [Fact]
    public void Confirmed_opening_matches_lot_qty_and_blocks_issue_before_confirm()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        Assert.Throws<InvalidOperationException>(() =>
            svc.Issue(DateTime.Today, null, [new IssueLineRequest { ItemCode = "M001", Quantity = 1 }]));
        svc.SaveOpeningDraft("M001", "OPEN", 7, DateTime.Today.AddDays(-1), DateTime.Today.AddDays(60));
        svc.ConfirmOpening("M001");
        Assert.Equal(7m, svc.GetOnHand("M001"));
        Assert.Equal(7m, svc.LotsForItem("M001").Single().Quantity);
    }

    [Fact]
    public void Receipt_freezes_unit_price_and_increases_lot()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        var item = svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 1, DateTime.Today.AddDays(-2), DateTime.Today.AddDays(90));
        svc.ConfirmOpening("M001");
        var supplier = svc.CreateSupplier("A사");
        var doc = svc.Receive(DateTime.Today, supplier.Id, "INV-1",
        [
            new ReceiptLineRequest { ItemCode = "M001", Quantity = 10, UnitPrice = 100, LotNumber = "L100", ExpiryDate = DateTime.Today.AddDays(40) }
        ]);
        Assert.Equal(11m, svc.GetOnHand("M001"));
        Assert.Equal(1000m, doc.Lines.Single().Amount);
        item.ReferencePrice = 999;
        db.SaveChanges();
        Assert.Equal(100m, db.StockLines.Single(l => l.DocumentId == doc.Id).UnitPrice);
    }

    [Fact]
    public void Duplicate_document_number_warns_but_saves()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 1, DateTime.Today.AddDays(-2), DateTime.Today.AddDays(90));
        svc.ConfirmOpening("M001");
        var supplier = svc.CreateSupplier("A사");
        var first = svc.Receive(DateTime.Today, supplier.Id, "DUP",
        [
            new ReceiptLineRequest { ItemCode = "M001", Quantity = 1, UnitPrice = 10, LotNumber = "A", ExpiryDate = DateTime.Today.AddDays(10) }
        ]);
        var second = svc.Receive(DateTime.Today, supplier.Id, "DUP",
        [
            new ReceiptLineRequest { ItemCode = "M001", Quantity = 1, UnitPrice = 10, LotNumber = "B", ExpiryDate = DateTime.Today.AddDays(10) }
        ]);
        Assert.False(first.DuplicateWarning);
        Assert.True(second.DuplicateWarning);
    }

    [Fact]
    public void Issue_blocks_overstock_and_expired_lot_and_snapshots_cost()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 5, DateTime.Today.AddDays(-3), DateTime.Today.AddDays(90));
        svc.ConfirmOpening("M001");
        var supplier = svc.CreateSupplier("A사");
        svc.Receive(DateTime.Today.AddDays(-1), supplier.Id, "R1",
        [
            new ReceiptLineRequest { ItemCode = "M001", Quantity = 0.001m, UnitPrice = 50, LotNumber = "EXP", ExpiryDate = DateTime.Today.AddDays(-1) }
        ]);
        Assert.Throws<InvalidOperationException>(() =>
            svc.Issue(DateTime.Today, null, [new IssueLineRequest { ItemCode = "M001", Quantity = 6 }]));
        Assert.Throws<InvalidOperationException>(() =>
            svc.Issue(DateTime.Today, null, [new IssueLineRequest { ItemCode = "M001", Quantity = 0.001m, LotNumber = "EXP" }]));
        var issue = svc.Issue(DateTime.Today, null, [new IssueLineRequest { ItemCode = "M001", Quantity = 1, LotNumber = "OPEN" }]);
        var snap = issue.Lines.Single().UnitCostSnapshot;
        svc.Receive(DateTime.Today, supplier.Id, "R2",
        [
            new ReceiptLineRequest { ItemCode = "M001", Quantity = 10, UnitPrice = 999, LotNumber = "NEW", ExpiryDate = DateTime.Today.AddDays(20) }
        ]);
        Assert.Equal(snap, db.StockLines.Single(l => l.Id == issue.Lines.Single().Id).UnitCostSnapshot);
    }

    [Fact]
    public void Issue_before_receipt_date_is_blocked()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 1, DateTime.Today, DateTime.Today.AddDays(30));
        svc.ConfirmOpening("M001");
        Assert.Throws<InvalidOperationException>(() =>
            svc.Issue(DateTime.Today.AddDays(-1), null, [new IssueLineRequest { ItemCode = "M001", Quantity = 1, LotNumber = "OPEN" }]));
    }

    [Fact]
    public void Adjustment_keeps_original_receipt_and_updates_lot()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 5, DateTime.Today.AddDays(-1), DateTime.Today.AddDays(30));
        svc.ConfirmOpening("M001");
        svc.Adjust(DateTime.Today, "M001", "OPEN", -1, AdjustmentType.Disposal, "파손");
        Assert.Equal(4m, svc.GetOnHand("M001"));
        Assert.Contains(db.Documents, d => d.Type == DocumentType.Adjustment);
        Assert.Equal(OpeningStatus.Confirmed, svc.GetOpeningStatus("M001"));
    }

    [Fact]
    public void Cancel_reverses_stock_without_deleting_original()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 1, DateTime.Today.AddDays(-2), DateTime.Today.AddDays(30));
        svc.ConfirmOpening("M001");
        var supplier = svc.CreateSupplier("A사");
        var receipt = svc.Receive(DateTime.Today, supplier.Id, "C1",
        [
            new ReceiptLineRequest { ItemCode = "M001", Quantity = 4, UnitPrice = 10, LotNumber = "L4", ExpiryDate = DateTime.Today.AddDays(20) }
        ]);
        svc.CancelDocument(receipt.Id, "입력 오류");
        Assert.True(db.Documents.Single(d => d.Id == receipt.Id).IsCancelled);
        Assert.Contains(db.Documents, d => d.Type == DocumentType.Reversal);
        Assert.Equal(1m, svc.GetOnHand("M001"));
        Assert.Equal(0m, svc.LotsForItem("M001").Single(l => l.LotNumber == "L4").Quantity);

        var summary = svc.ListDocumentSummaries(10).Single(d => d.Id == receipt.Id);
        Assert.Equal("주사기", summary.FirstItemName);
        Assert.True(summary.IsCancelled);
        Assert.Equal(40m, summary.TotalAmount);
        Assert.Equal(10m, summary.UnitPrice);
    }

    [Fact]
    public void GetDocumentDetail_and_DeleteDocument_support_edit_delete_flow()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 10, DateTime.Today.AddDays(-2), DateTime.Today.AddDays(30));
        svc.ConfirmOpening("M001");
        var supplier = svc.CreateSupplier("A사");
        var receipt = svc.Receive(DateTime.Today, supplier.Id, null,
        [
            new ReceiptLineRequest { ItemCode = "M001", Quantity = 2, UnitPrice = 100, LotNumber = "L1", ExpiryDate = DateTime.Today.AddDays(20) }
        ]);

        var detail = svc.GetDocumentDetail(receipt.Id);
        Assert.Equal(DocumentType.Receipt, detail.Type);
        Assert.Equal("A사", detail.SupplierName);
        Assert.Single(detail.Lines);
        Assert.Equal("M001", detail.Lines[0].ItemCode);
        Assert.Equal(2m, detail.Lines[0].Quantity);

        var summary = svc.ListDocumentSummaries(10).Single(d => d.Id == receipt.Id);
        Assert.Equal(200m, summary.TotalAmount);
        Assert.Equal(100m, summary.UnitPrice);

        svc.DeleteDocument(receipt.Id);
        Assert.True(db.Documents.Single(d => d.Id == receipt.Id).IsCancelled);
        Assert.Equal(10m, svc.GetOnHand("M001"));
    }

    [Fact]
    public void On_hand_equals_lot_sum()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "A", 3, DateTime.Today.AddDays(-2), DateTime.Today.AddDays(30));
        svc.ConfirmOpening("M001");
        var supplier = svc.CreateSupplier("A사");
        svc.Receive(DateTime.Today, supplier.Id, "R",
        [
            new ReceiptLineRequest { ItemCode = "M001", Quantity = 2, UnitPrice = 1, LotNumber = "B", ExpiryDate = DateTime.Today.AddDays(10) }
        ]);
        Assert.Equal(svc.LotsForItem("M001").Sum(l => l.Quantity), svc.GetOnHand("M001"));
    }

    [Fact]
    public void Monthly_usage_does_not_mix_years()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 20, new DateTime(2025, 1, 1), new DateTime(2027, 1, 1));
        svc.ConfirmOpening("M001");
        svc.Issue(new DateTime(2025, 3, 10), null, [new IssueLineRequest { ItemCode = "M001", Quantity = 2, LotNumber = "OPEN" }]);
        svc.Issue(new DateTime(2026, 3, 10), null, [new IssueLineRequest { ItemCode = "M001", Quantity = 5, LotNumber = "OPEN" }]);
        Assert.Equal(2m, svc.UsageQuantity(2025, 3));
        Assert.Equal(5m, svc.UsageQuantity(2026, 3));
    }

    [Fact]
    public void Closed_month_rejects_receipt_and_reopen_requires_reason()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 1, DateTime.Today.AddDays(-2), DateTime.Today.AddDays(30));
        svc.ConfirmOpening("M001");
        var today = DateTime.Today;
        svc.CloseMonth(today.Year, today.Month);
        var supplier = svc.CreateSupplier("A사");
        Assert.Throws<InvalidOperationException>(() =>
            svc.Receive(today, supplier.Id, "X",
            [
                new ReceiptLineRequest { ItemCode = "M001", Quantity = 1, UnitPrice = 1, LotNumber = "Z", ExpiryDate = today.AddDays(5) }
            ]));
        Assert.Throws<InvalidOperationException>(() => svc.ReopenMonth(today.Year, today.Month, ""));
        svc.ReopenMonth(today.Year, today.Month, "재실사");
        var again = svc.Receive(today, supplier.Id, "X2",
        [
            new ReceiptLineRequest { ItemCode = "M001", Quantity = 1, UnitPrice = 1, LotNumber = "Z", ExpiryDate = today.AddDays(5) }
        ]);
        Assert.False(again.IsCancelled);
    }

    [Fact]
    public void Below_min_stock_is_classified_as_reorder()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "소모품", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 3, DateTime.Today.AddDays(-1), DateTime.Today.AddDays(30));
        svc.ConfirmOpening("M001");
        var row = svc.SearchStockSnapshots("M001").Single();
        Assert.Equal(StockStatusKind.Reorder, row.Status);
        Assert.Contains(svc.ReorderItems(), i => i.Code == "M001");
    }

    [Fact]
    public void Stock_snapshot_includes_unit_cost_and_stock_value()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M010", "거즈", "드레싱", "개", "개", 10);
        svc.SaveOpeningDraft("M010", "OPEN", 20, new DateTime(2026, 1, 1), new DateTime(2027, 1, 1));
        svc.ConfirmOpening("M010");
        svc.Receive(new DateTime(2026, 8, 1), null, "R1",
            [new ReceiptLineRequest { ItemCode = "M010", Quantity = 10, UnitPrice = 100, LotNumber = "A", ExpiryDate = new DateTime(2027, 1, 1) }]);

        var row = svc.SearchStockSnapshots("M010").Single();
        Assert.Equal(30m, row.OnHand);
        Assert.Equal(1000m / 30m, row.UnitCost);
        Assert.Equal(1000m, row.StockValue);
        Assert.Equal(MoneyFormulas.LineAmount(row.OnHand!.Value, row.UnitCost), row.StockValue);
    }

    [Fact]
    public void Opening_uses_reference_price_so_on_hand_times_unit_cost_equals_stock_value()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M020", "장갑", "소모품", "개", "개", 10);
        db.Items.Single(i => i.Code == "M020").ReferencePrice = 400m;
        db.SaveChanges();
        svc.SaveOpeningDraft("M020", "OPEN", 10740, new DateTime(2026, 1, 1), new DateTime(2027, 1, 1));
        svc.ConfirmOpening("M020");

        var row = svc.SearchStockSnapshots("M020").Single();
        Assert.Equal(10740m, row.OnHand);
        Assert.Equal(400m, row.UnitCost);
        Assert.Equal(4_296_000m, row.StockValue);
        Assert.Equal(MoneyFormulas.LineAmount(row.OnHand!.Value, row.UnitCost), row.StockValue);
    }

    [Fact]
    public void Receive_and_issue_totals_equal_quantity_times_unit_price()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M021", "마스크", "소모품", "개", "개", 10);
        db.Items.Single(i => i.Code == "M021").ReferencePrice = 80m;
        db.SaveChanges();
        svc.SaveOpeningDraft("M021", "OPEN", 10, DateTime.Today.AddDays(-2), DateTime.Today.AddDays(90));
        svc.ConfirmOpening("M021");
        var receipt = svc.Receive(DateTime.Today, null, "R80",
            [new ReceiptLineRequest { ItemCode = "M021", Quantity = 1, UnitPrice = 80, LotNumber = "N", ExpiryDate = DateTime.Today.AddDays(30) }]);
        Assert.Equal(MoneyFormulas.LineAmount(1, 80), receipt.Lines.Single().Amount);

        var receiveSummary = svc.ListDocumentSummaries(10).Single(d => d.Id == receipt.Id);
        Assert.Equal(80m, receiveSummary.TotalAmount);
        Assert.Equal(80m, receiveSummary.UnitPrice);

        var issue = svc.Issue(DateTime.Today, null, [new IssueLineRequest { ItemCode = "M021", Quantity = 2, LotNumber = "OPEN" }]);
        var unit = issue.Lines.Single().UnitPrice;
        Assert.Equal(MoneyFormulas.LineAmount(2, unit), issue.Lines.Single().Amount);

        var issueSummary = svc.ListDocumentSummaries(10).Single(d => d.Id == issue.Id);
        Assert.Equal(issue.Lines.Single().Amount, issueSummary.TotalAmount);
        Assert.Equal(unit, issueSummary.UnitPrice);
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

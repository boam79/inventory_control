using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure;

public static class ExcelCatalog
{
    public static ImportPreview PreviewMaster(string path)
    {
        using var wb = new XLWorkbook(path);
        var sheet = RequireItemSheet(wb);
        var codes = new List<string>();
        var skipped = 0;
        foreach (var excelRow in sheet.RowsUsed().Skip(1))
        {
            var code = excelRow.Cell(1).GetString().Trim();
            var name = excelRow.Cell(2).GetString().Trim();
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || code == "품목코드")
            {
                skipped++;
                continue;
            }

            codes.Add(code);
        }

        return new ImportPreview { ItemCodes = codes.Distinct().ToList(), EmptyRowsSkipped = skipped };
    }

    public static ImportResult ImportMaster(InventoryDbContext db, string path, bool includeOpening) =>
        Import(db, path, includeOpening ? ImportMode.MasterAndOpening : ImportMode.MasterOnly);

    public static ImportResult Import(InventoryDbContext db, string path, ImportMode mode)
    {
        using var wb = new XLWorkbook(path);
        var sheet = RequireItemSheet(wb);
        var svc = new InventoryService(db, "import");
        var imported = 0;
        var skipped = 0;
        var openings = 0;
        var applyOpening = mode == ImportMode.MasterAndOpening;
        var history = mode == ImportMode.FullHistory
            ? ReadHistoryRows(wb)
            : new HistoryRows([], [], 0);
        var openingByCode = mode == ImportMode.MasterOnly
            ? new Dictionary<string, decimal>()
            : ReadOpeningQuantities(wb);
        var doubleCount = mode == ImportMode.FullHistory
                          && history.Transactions.Count > 0
                          && openingByCode.Count > 0;
        if (doubleCount)
        {
            applyOpening = false;
        }
        else if (mode == ImportMode.FullHistory && history.Transactions.Count == 0)
        {
            applyOpening = false;
        }

        foreach (var excelRow in sheet.RowsUsed().Skip(1))
        {
            var code = excelRow.Cell(1).GetString().Trim();
            var name = excelRow.Cell(2).GetString().Trim();
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || code == "품목코드")
            {
                skipped++;
                continue;
            }

            if (db.Items.Any(i => i.Code == code))
            {
                skipped++;
                continue;
            }

            var spec = excelRow.Cell(4).GetString().Trim();
            var minStock = ParseDecimal(excelRow.Cell(8).GetString());
            var item = svc.CreateItem(code, name, excelRow.Cell(3).GetString().Trim(), spec, spec, minStock);
            if (decimal.TryParse(excelRow.Cell(5).GetString(), out var price) && price > 0)
            {
                item.ReferencePrice = price;
                db.SaveChanges();
            }

            if (applyOpening && openingByCode.TryGetValue(code, out var qty) && qty > 0)
            {
                svc.SaveOpeningDraft(code, "OPEN", qty, DateTime.Today, DateTime.Today.AddDays(365));
                svc.ConfirmOpening(code);
                openings++;
            }

            imported++;
        }

        var tx = 0;
        if (mode == ImportMode.FullHistory)
        {
            tx = CommitHistory(svc, db, history);
        }

        return new ImportResult
        {
            ImportedItems = imported,
            SkippedRows = skipped + history.EmptySkipped,
            TransactionRows = tx,
            OpeningConfirmed = openings,
            DoubleCountWarning = doubleCount,
            Warning = doubleCount
                ? "기초재고와 구매·사용 이력을 같이 넣으면 재고가 두 번 잡힙니다. 기초는 건너뛰고 이력만 넣었습니다."
                : string.Empty
        };
    }

    public static void ExportStock(InventoryDbContext db, string path)
    {
        var svc = new InventoryService(db, "export");
        using var wb = new XLWorkbook();

        var stock = wb.AddWorksheet("재고현황");
        stock.Cell(1, 1).Value = "품목코드";
        stock.Cell(1, 2).Value = "품목명";
        stock.Cell(1, 3).Value = "현재재고";
        stock.Cell(1, 4).Value = "상태";
        var row = 2;
        foreach (var snap in svc.SearchStockSnapshots(string.Empty))
        {
            stock.Cell(row, 1).Value = snap.Code;
            stock.Cell(row, 2).Value = snap.Name;
            stock.Cell(row, 3).Value = snap.OnHand?.ToString() ?? "미설정";
            if (snap.OnHand is { } qty)
            {
                stock.Cell(row, 3).SetValue(qty);
            }
            else
            {
                stock.Cell(row, 3).Value = "미설정";
            }

            stock.Cell(row, 4).Value = StatusKo(snap.Status);
            row++;
        }

        var reorder = wb.AddWorksheet("발주필요");
        reorder.Cell(1, 1).Value = "품목코드";
        reorder.Cell(1, 2).Value = "품목명";
        reorder.Cell(1, 3).Value = "현재재고";
        reorder.Cell(1, 4).Value = "최소재고";
        row = 2;
        foreach (var item in svc.ReorderItems())
        {
            reorder.Cell(row, 1).Value = item.Code;
            reorder.Cell(row, 2).Value = item.Name;
            reorder.Cell(row, 3).Value = svc.GetOnHand(item.Code) ?? 0;
            reorder.Cell(row, 4).Value = item.MinStock;
            row++;
        }

        var expiry = wb.AddWorksheet("유효기간");
        expiry.Cell(1, 1).Value = "품목코드";
        expiry.Cell(1, 2).Value = "LOT";
        expiry.Cell(1, 3).Value = "유효기간";
        expiry.Cell(1, 4).Value = "수량";
        row = 2;
        foreach (var lot in db.Lots.Include(l => l.Item).Where(l => l.ExpiryDate != null && l.Quantity > 0).OrderBy(l => l.ExpiryDate))
        {
            expiry.Cell(row, 1).Value = lot.Item.Code;
            expiry.Cell(row, 2).Value = lot.LotNumber;
            expiry.Cell(row, 3).Value = lot.ExpiryDate;
            expiry.Cell(row, 4).Value = lot.Quantity;
            row++;
        }

        var usage = wb.AddWorksheet("사용");
        usage.Cell(1, 1).Value = "연도";
        usage.Cell(1, 2).Value = "월";
        usage.Cell(1, 3).Value = "품목코드";
        usage.Cell(1, 4).Value = "수량";
        usage.Cell(1, 5).Value = "원가스냅샷";
        row = 2;
        foreach (var doc in db.Documents.Include(d => d.Lines).Where(d => d.Type == DocumentType.Issue && !d.IsCancelled))
        {
            foreach (var line in doc.Lines)
            {
                usage.Cell(row, 1).Value = doc.DocumentDate.Year;
                usage.Cell(row, 2).Value = doc.DocumentDate.Month;
                usage.Cell(row, 3).Value = db.Items.Single(i => i.Id == line.ItemId).Code;
                usage.Cell(row, 4).Value = line.Quantity;
                usage.Cell(row, 5).Value = line.UnitCostSnapshot;
                row++;
            }
        }

        wb.SaveAs(path);
    }

    public static void ExportReport(IReadOnlyList<ReportRow> rows, string path)
    {
        using var wb = new XLWorkbook();
        var sheet = wb.AddWorksheet("통계보고서");
        sheet.Cell(1, 1).Value = "기간";
        sheet.Cell(1, 2).Value = "구분";
        sheet.Cell(1, 3).Value = "사용수량";
        sheet.Cell(1, 4).Value = "입고수량";
        sheet.Cell(1, 5).Value = "구매금액";
        var row = 2;
        foreach (var item in rows)
        {
            sheet.Cell(row, 1).Value = item.PeriodLabel;
            sheet.Cell(row, 2).Value = item.Dimension;
            sheet.Cell(row, 3).Value = item.IssueQty;
            sheet.Cell(row, 4).Value = item.ReceiptQty;
            sheet.Cell(row, 5).Value = item.PurchaseAmount;
            row++;
        }

        wb.SaveAs(path);
    }

    private sealed record HistoryLine(DateTime Date, string ItemCode, decimal Quantity, bool Receipt, string? Supplier);

    private sealed record HistoryRows(IReadOnlyList<HistoryLine> Transactions, IReadOnlyList<HistoryLine> Unused, int EmptySkipped);

    private static HistoryRows ReadHistoryRows(XLWorkbook wb)
    {
        var lines = new List<HistoryLine>();
        var empty = 0;
        empty += ReadSheet(wb, "구매", isReceipt: true, lines);
        empty += ReadSheet(wb, "사용", isReceipt: false, lines);
        return new HistoryRows(lines, [], empty);
    }

    private static int ReadSheet(XLWorkbook wb, string namePart, bool isReceipt, List<HistoryLine> lines)
    {
        var sheet = wb.Worksheets.FirstOrDefault(ws => ws.Name.Contains(namePart) && !ws.Name.Contains("품목"));
        if (sheet is null)
        {
            return 0;
        }

        IXLRow? header = null;
        foreach (var row in sheet.RowsUsed())
        {
            var texts = row.CellsUsed().Select(c => c.GetString().Trim()).ToList();
            if (texts.Any(t => t is "구매일" or "사용일" or "품목코드"))
            {
                header = row;
                break;
            }
        }

        if (header is null)
        {
            return 0;
        }

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in header.CellsUsed())
        {
            var key = cell.GetString().Trim();
            if (!string.IsNullOrWhiteSpace(key) && !map.ContainsKey(key))
            {
                map[key] = cell.Address.ColumnNumber;
            }
        }

        var dateCol = FindCol(map, isReceipt ? "구매일" : "사용일", "날짜");
        var codeCol = FindCol(map, "품목코드", "코드");
        var qtyCol = FindCol(map, isReceipt ? "구매수량" : "사용수량", "수량");
        var supplierCol = FindCol(map, "공급업체", "거래처");
        if (dateCol is null || codeCol is null || qtyCol is null)
        {
            return 0;
        }

        var skipped = 0;
        foreach (var excelRow in sheet.RowsUsed().Where(r => r.RowNumber() > header.RowNumber()))
        {
            var code = excelRow.Cell(codeCol.Value).GetString().Trim();
            var dateText = excelRow.Cell(dateCol.Value).GetString().Trim();
            var qtyText = excelRow.Cell(qtyCol.Value).GetString().Trim();
            if (string.IsNullOrWhiteSpace(code) || code == "품목코드"
                || string.IsNullOrWhiteSpace(dateText) || string.IsNullOrWhiteSpace(qtyText))
            {
                skipped++;
                continue;
            }

            if (!TryParseDate(excelRow.Cell(dateCol.Value), out var date) || !decimal.TryParse(qtyText, out var qty) || qty <= 0)
            {
                skipped++;
                continue;
            }

            var supplier = supplierCol is null ? null : excelRow.Cell(supplierCol.Value).GetString().Trim();
            lines.Add(new HistoryLine(date, code, qty, isReceipt, string.IsNullOrWhiteSpace(supplier) ? null : supplier));
        }

        return skipped;
    }

    private static int? FindCol(Dictionary<string, int> map, params string[] names)
    {
        foreach (var name in names)
        {
            if (map.TryGetValue(name, out var col))
            {
                return col;
            }
        }

        foreach (var pair in map)
        {
            if (names.Any(name => pair.Key.Contains(name)))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static bool TryParseDate(IXLCell cell, out DateTime date)
    {
        if (cell.DataType == XLDataType.DateTime)
        {
            date = cell.GetDateTime();
            return true;
        }

        return DateTime.TryParse(cell.GetString(), out date);
    }

    private static int CommitHistory(InventoryService svc, InventoryDbContext db, HistoryRows history)
    {
        var count = 0;
        foreach (var line in history.Transactions.Where(t => t.Receipt).OrderBy(t => t.Date))
        {
            if (!db.Items.Any(i => i.Code == line.ItemCode))
            {
                continue;
            }

            int? supplierId = null;
            if (!string.IsNullOrWhiteSpace(line.Supplier))
            {
                var found = svc.SearchSuppliers(line.Supplier).FirstOrDefault()
                            ?? svc.CreateSupplier(line.Supplier);
                supplierId = found.Id;
            }

            svc.Receive(line.Date, supplierId, null,
            [
                new ReceiptLineRequest
                {
                    ItemCode = line.ItemCode,
                    Quantity = line.Quantity,
                    UnitPrice = 0,
                    LotNumber = "LEGACY",
                    ExpiryDate = line.Date.AddDays(365)
                }
            ]);
            count++;
        }

        foreach (var item in db.Items.ToList())
        {
            if (item.OpeningStatus != OpeningStatus.Confirmed && db.Lots.Any(l => l.ItemId == item.Id))
            {
                svc.ConfirmOpening(item.Code);
            }
        }

        foreach (var line in history.Transactions.Where(t => !t.Receipt).OrderBy(t => t.Date))
        {
            if (!db.Items.Any(i => i.Code == line.ItemCode))
            {
                continue;
            }

            svc.Issue(line.Date, null,
            [
                new IssueLineRequest { ItemCode = line.ItemCode, Quantity = line.Quantity, LotNumber = "LEGACY" }
            ]);
            count++;
        }

        return count;
    }

    private static Dictionary<string, decimal> ReadOpeningQuantities(XLWorkbook wb)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var sheet = wb.Worksheets.FirstOrDefault(ws => ws.Name.Contains("재고"));
        if (sheet is null)
        {
            return map;
        }

        foreach (var excelRow in sheet.RowsUsed().Skip(1))
        {
            var code = excelRow.Cell(1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(code) || code == "품목코드")
            {
                continue;
            }

            var raw = excelRow.Cell(5).GetString().Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (decimal.TryParse(raw, out var qty) && qty > 0)
            {
                map[code] = qty;
            }
        }

        return map;
    }

    private static IXLWorksheet RequireItemSheet(XLWorkbook wb) =>
        wb.Worksheets.FirstOrDefault(ws => ws.Name.Contains("품목"))
        ?? wb.Worksheet(1);

    private static decimal ParseDecimal(string text) =>
        decimal.TryParse(text, out var n) ? n : 0m;

    private static string StatusKo(StockStatusKind status) => status switch
    {
        StockStatusKind.Unset => "미설정",
        StockStatusKind.Reorder => "발주권장",
        StockStatusKind.OutOfStock => "품절",
        StockStatusKind.Expiring => "유효기간",
        StockStatusKind.Inactive => "사용중지",
        _ => "정상"
    };
}

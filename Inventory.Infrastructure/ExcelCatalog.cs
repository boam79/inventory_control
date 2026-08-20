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

    public static ImportResult ImportMaster(InventoryDbContext db, string path, bool includeOpening)
    {
        var preview = PreviewMaster(path);
        using var wb = new XLWorkbook(path);
        var sheet = RequireItemSheet(wb);
        var svc = new InventoryService(db, "import");
        var imported = 0;
        var skipped = 0;
        var openings = 0;
        var openingByCode = includeOpening ? ReadOpeningQuantities(wb) : new Dictionary<string, decimal>();

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

            if (includeOpening && openingByCode.TryGetValue(code, out var qty) && qty > 0)
            {
                svc.SaveOpeningDraft(code, "OPEN", qty, DateTime.Today, DateTime.Today.AddDays(365));
                svc.ConfirmOpening(code);
                openings++;
            }

            imported++;
        }

        return new ImportResult
        {
            ImportedItems = imported,
            SkippedRows = skipped,
            TransactionRows = 0,
            OpeningConfirmed = openings
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

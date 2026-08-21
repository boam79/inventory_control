using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure;

public enum ReportPeriodKind
{
    Day = 0,
    Month = 1,
    Quarter = 2,
    Year = 3,
    Custom = 4
}

public enum ReportDimension
{
    Item = 0,
    Category = 1,
    Department = 2,
    Supplier = 3
}

public sealed record ReportRow(
    string PeriodLabel,
    string Dimension,
    decimal IssueQty,
    decimal ReceiptQty,
    decimal PurchaseAmount);

public sealed record MonthClosePreview(
    bool IsClosed,
    int ReceiptDocs,
    int IssueDocs,
    decimal ReceiptQty,
    decimal IssueQty,
    decimal PurchaseAmount,
    int UnsetOpenings,
    int NegativeLots);

public static class ReportUi
{
    public const string Period = "기간";
    public const string Dimension = "구분";
    public const string IssueQty = "사용수량";
    public const string ReceiptQty = "입고수량";
    public const string PurchaseAmount = "구매금액";
}

public static class ReportAnalytics
{
    public static (DateTime Start, DateTime EndExclusive) ResolveRange(
        ReportPeriodKind kind,
        DateTime anchor,
        DateTime? customStart = null,
        DateTime? customEnd = null)
    {
        var day = anchor.Date;
        return kind switch
        {
            ReportPeriodKind.Day => (day, day.AddDays(1)),
            ReportPeriodKind.Month => (new DateTime(day.Year, day.Month, 1), new DateTime(day.Year, day.Month, 1).AddMonths(1)),
            ReportPeriodKind.Quarter => QuarterRange(day),
            ReportPeriodKind.Year => (new DateTime(day.Year, 1, 1), new DateTime(day.Year + 1, 1, 1)),
            ReportPeriodKind.Custom => (
                (customStart ?? day).Date,
                (customEnd ?? day).Date.AddDays(1)),
            _ => (day, day.AddDays(1))
        };
    }

    public static string PeriodLabel(ReportPeriodKind kind, DateTime start) => kind switch
    {
        ReportPeriodKind.Day => start.ToString("yyyy-MM-dd"),
        ReportPeriodKind.Month => start.ToString("yyyy-MM"),
        ReportPeriodKind.Quarter => $"{start.Year}-Q{((start.Month - 1) / 3) + 1}",
        ReportPeriodKind.Year => start.Year.ToString(),
        ReportPeriodKind.Custom => start.ToString("yyyy-MM-dd"),
        _ => start.ToString("yyyy-MM-dd")
    };

    public static IReadOnlyList<ReportRow> Query(
        InventoryDbContext db,
        ReportPeriodKind kind,
        DateTime anchor,
        ReportDimension dimension,
        DateTime? customStart = null,
        DateTime? customEnd = null)
    {
        var (start, end) = ResolveRange(kind, anchor, customStart, customEnd);
        var label = kind == ReportPeriodKind.Custom
            ? $"{start:yyyy-MM-dd}~{end.AddDays(-1):yyyy-MM-dd}"
            : PeriodLabel(kind, start);

        var docs = db.Documents
            .Where(d => !d.IsCancelled && d.DocumentDate >= start && d.DocumentDate < end)
            .Select(d => new { d.Id, d.Type, d.DepartmentId, d.SupplierId })
            .ToList();
        if (docs.Count == 0)
        {
            return [];
        }

        var docIds = docs.Select(d => d.Id).ToList();
        var lines = db.StockLines
            .Where(l => docIds.Contains(l.DocumentId))
            .Select(l => new { l.DocumentId, l.ItemId, l.Quantity, l.Amount })
            .ToList();
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var items = db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionary(i => i.Id);
        var departments = db.Departments.ToDictionary(d => d.Id, d => d.Name);
        var suppliers = db.Suppliers.ToDictionary(s => s.Id, s => s.Name);
        var docMap = docs.ToDictionary(d => d.Id);

        var groups = new Dictionary<string, (decimal Issue, decimal Receipt, decimal Purchase)>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (!docMap.TryGetValue(line.DocumentId, out var doc) || !items.TryGetValue(line.ItemId, out var item))
            {
                continue;
            }

            var key = dimension switch
            {
                ReportDimension.Category => string.IsNullOrWhiteSpace(item.Category) ? "(분류 없음)" : item.Category,
                ReportDimension.Department => doc.DepartmentId is { } dep && departments.TryGetValue(dep, out var depName)
                    ? depName
                    : "(부서 없음)",
                ReportDimension.Supplier => doc.SupplierId is { } sup && suppliers.TryGetValue(sup, out var supName)
                    ? supName
                    : "(공급업체 없음)",
                _ => $"{item.Code} {item.Name}"
            };

            groups.TryGetValue(key, out var acc);
            if (doc.Type == DocumentType.Issue)
            {
                acc.Issue += line.Quantity;
            }
            else if (doc.Type == DocumentType.Receipt)
            {
                acc.Receipt += line.Quantity;
                acc.Purchase += line.Amount;
            }

            groups[key] = acc;
        }

        return groups
            .OrderBy(g => g.Key)
            .Select(g => new ReportRow(label, g.Key, g.Value.Issue, g.Value.Receipt, g.Value.Purchase))
            .ToList();
    }

    public static MonthClosePreview PreviewMonth(InventoryDbContext db, int year, int month)
    {
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);
        var docs = db.Documents
            .Where(d => !d.IsCancelled && d.DocumentDate >= start && d.DocumentDate < end)
            .Select(d => new { d.Id, d.Type })
            .ToList();
        var receipts = docs.Where(d => d.Type == DocumentType.Receipt).ToList();
        var issues = docs.Where(d => d.Type == DocumentType.Issue).ToList();
        var receiptIds = receipts.Select(d => d.Id).ToList();
        var issueIds = issues.Select(d => d.Id).ToList();
        var receiptQty = receiptIds.Count == 0
            ? 0m
            : db.StockLines.Where(l => receiptIds.Contains(l.DocumentId)).Sum(l => (decimal?)l.Quantity) ?? 0m;
        var issueQty = issueIds.Count == 0
            ? 0m
            : db.StockLines.Where(l => issueIds.Contains(l.DocumentId)).Sum(l => (decimal?)l.Quantity) ?? 0m;
        var purchase = receiptIds.Count == 0
            ? 0m
            : db.StockLines.Where(l => receiptIds.Contains(l.DocumentId)).Sum(l => (decimal?)l.Amount) ?? 0m;
        var closed = db.MonthCloses.Any(c => c.Year == year && c.Month == month && c.IsClosed);
        var unset = db.Items.Count(i => i.IsActive && i.OpeningStatus == OpeningStatus.Unset);
        var negative = db.Lots.Count(l => l.Quantity < 0);
        return new MonthClosePreview(
            closed,
            receipts.Count,
            issues.Count,
            receiptQty,
            issueQty,
            purchase,
            unset,
            negative);
    }

    private static (DateTime Start, DateTime EndExclusive) QuarterRange(DateTime day)
    {
        var month = ((day.Month - 1) / 3) * 3 + 1;
        var start = new DateTime(day.Year, month, 1);
        return (start, start.AddMonths(3));
    }
}

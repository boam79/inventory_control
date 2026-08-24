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
    public const string IssueQty = "출고수량";
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

    public static DateTime StepBack(ReportPeriodKind kind, DateTime anchor, int periodsBack) => kind switch
    {
        ReportPeriodKind.Day => anchor.AddDays(-periodsBack),
        ReportPeriodKind.Quarter => anchor.AddMonths(-3 * periodsBack),
        ReportPeriodKind.Year => anchor.AddYears(-periodsBack),
        ReportPeriodKind.Custom => anchor,
        _ => anchor.AddMonths(-periodsBack)
    };

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

        var lines = LoadLines(db, start, end);
        if (lines.Count == 0)
        {
            return [];
        }

        var departments = db.Departments.AsNoTracking().ToDictionary(d => d.Id, d => d.Name);
        var suppliers = db.Suppliers.AsNoTracking().ToDictionary(s => s.Id, s => s.Name);
        return Aggregate(lines, label, dimension, departments, suppliers);
    }

    /// <summary>
    /// Same aggregation as <see cref="Query"/> but for the "recent N periods" trend view: loads the
    /// whole span covering all periods in a single query, then buckets in memory, instead of issuing
    /// one round trip (plus one items/departments/suppliers reload) per period.
    /// </summary>
    public static IReadOnlyList<ReportRow> QueryTrend(
        InventoryDbContext db,
        ReportPeriodKind kind,
        DateTime anchor,
        ReportDimension dimension,
        int periodsBack,
        DateTime? customStart = null,
        DateTime? customEnd = null)
    {
        if (periodsBack <= 1 || kind == ReportPeriodKind.Custom)
        {
            return Query(db, kind, anchor, dimension, customStart, customEnd);
        }

        var earliestAnchor = StepBack(kind, anchor, periodsBack - 1);
        var (overallStart, _) = ResolveRange(kind, earliestAnchor);
        var (_, overallEnd) = ResolveRange(kind, anchor);

        var lines = LoadLines(db, overallStart, overallEnd);
        if (lines.Count == 0)
        {
            return [];
        }

        var departments = db.Departments.AsNoTracking().ToDictionary(d => d.Id, d => d.Name);
        var suppliers = db.Suppliers.AsNoTracking().ToDictionary(s => s.Id, s => s.Name);

        var rows = new List<ReportRow>();
        for (var i = periodsBack - 1; i >= 0; i--)
        {
            var stepped = StepBack(kind, anchor, i);
            var (start, end) = ResolveRange(kind, stepped);
            var label = PeriodLabel(kind, start);
            var bucket = lines.Where(l => l.DocumentDate >= start && l.DocumentDate < end);
            rows.AddRange(Aggregate(bucket, label, dimension, departments, suppliers));
        }

        return rows;
    }

    private sealed record RawLine(
        DocumentType Type,
        DateTime DocumentDate,
        int? DepartmentId,
        int? SupplierId,
        string Category,
        string ItemCode,
        string ItemName,
        decimal Quantity,
        decimal Amount);

    /// <summary>
    /// Loads issue/receipt lines for a date range as a single SQL join (StockLines -> Documents -> Items)
    /// instead of fetching document ids then re-querying lines/items with `Contains(...)` id lists, which
    /// both adds round trips and risks hitting SQLite's bound-parameter limit once item/document counts grow.
    /// </summary>
    private static List<RawLine> LoadLines(InventoryDbContext db, DateTime start, DateTime end) =>
        db.StockLines.AsNoTracking()
            .Where(l =>
                !l.Document.IsCancelled
                && l.Document.DocumentDate >= start
                && l.Document.DocumentDate < end
                && (l.Document.Type == DocumentType.Issue || l.Document.Type == DocumentType.Receipt))
            .Join(db.Items.AsNoTracking(), l => l.ItemId, i => i.Id, (l, i) => new RawLine(
                l.Document.Type,
                l.Document.DocumentDate,
                l.Document.DepartmentId,
                l.Document.SupplierId,
                i.Category,
                i.Code,
                i.Name,
                l.Quantity,
                l.Amount))
            .ToList();

    private static List<ReportRow> Aggregate(
        IEnumerable<RawLine> lines,
        string label,
        ReportDimension dimension,
        IReadOnlyDictionary<int, string> departments,
        IReadOnlyDictionary<int, string> suppliers)
    {
        var groups = new Dictionary<string, (decimal Issue, decimal Receipt, decimal Purchase)>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var key = dimension switch
            {
                ReportDimension.Category => string.IsNullOrWhiteSpace(line.Category) ? "(분류 없음)" : line.Category,
                ReportDimension.Department => line.DepartmentId is { } dep && departments.TryGetValue(dep, out var depName)
                    ? depName
                    : "(부서 없음)",
                ReportDimension.Supplier => line.SupplierId is { } sup && suppliers.TryGetValue(sup, out var supName)
                    ? supName
                    : "(공급업체 없음)",
                _ => $"{line.ItemCode} {line.ItemName}"
            };

            groups.TryGetValue(key, out var acc);
            if (line.Type == DocumentType.Issue)
            {
                acc.Issue += line.Quantity;
            }
            else if (line.Type == DocumentType.Receipt)
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

    public static IReadOnlyList<ReportRow> QuerySupplierMonthlyPurchases(
        InventoryDbContext db,
        DateTime anchor,
        int monthsBack = 6) =>
        QueryTrend(db, ReportPeriodKind.Month, anchor, ReportDimension.Supplier, monthsBack);

    public static MonthClosePreview PreviewMonth(InventoryDbContext db, int year, int month)
    {
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);

        // One grouped query for document counts and one for line totals, instead of loading the
        // month's document ids into memory and re-querying StockLines three times with `Contains(...)`.
        var docCounts = db.Documents.AsNoTracking()
            .Where(d => !d.IsCancelled && d.DocumentDate >= start && d.DocumentDate < end)
            .GroupBy(d => d.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToList()
            .ToDictionary(x => x.Type, x => x.Count);

        var lineTotals = db.StockLines.AsNoTracking()
            .Where(l => !l.Document.IsCancelled && l.Document.DocumentDate >= start && l.Document.DocumentDate < end)
            .GroupBy(l => l.Document.Type)
            .Select(g => new { Type = g.Key, Qty = g.Sum(x => x.Quantity), Amount = g.Sum(x => x.Amount) })
            .ToList()
            .ToDictionary(x => x.Type, x => (x.Qty, x.Amount));

        var (receiptQty, purchase) = lineTotals.GetValueOrDefault(DocumentType.Receipt);
        var (issueQty, _) = lineTotals.GetValueOrDefault(DocumentType.Issue);
        var closed = db.MonthCloses.AsNoTracking().Any(c => c.Year == year && c.Month == month && c.IsClosed);
        var unset = db.Items.AsNoTracking().Count(i => i.IsActive && i.OpeningStatus == OpeningStatus.Unset);
        var negative = db.Lots.AsNoTracking().Count(l => l.Quantity < 0);
        return new MonthClosePreview(
            closed,
            docCounts.GetValueOrDefault(DocumentType.Receipt),
            docCounts.GetValueOrDefault(DocumentType.Issue),
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

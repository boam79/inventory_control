using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure;

public sealed record MonthlyQty(int Year, int Month, decimal Qty);

public sealed record DashboardKpi(
    int ActiveItems,
    int ReorderItems,
    int OutOfStockItems,
    int ExpiringLots,
    decimal MonthPurchaseAmount,
    decimal MonthIssueQty,
    decimal MonthDisposalQty,
    int TodayReceiptDocs,
    int TodayIssueDocs);

public static class DashboardMetrics
{
    public static DashboardKpi Build(InventoryDbContext db, InventoryService svc, DateTime today, int expiryWarningDays = 90)
    {
        var qty = db.Lots.AsNoTracking()
            .GroupBy(l => l.ItemId)
            .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToList()
            .ToDictionary(x => x.ItemId, x => x.Qty);
        var items = db.Items.AsNoTracking()
            .Select(i => new { i.Id, i.IsActive, i.OpeningStatus, i.MinStock })
            .ToList();
        var active = items.Count(i => i.IsActive);
        var reorder = 0;
        var outOfStock = 0;
        foreach (var item in items)
        {
            if (!item.IsActive || item.OpeningStatus == OpeningStatus.Unset)
            {
                continue;
            }

            var onHand = qty.GetValueOrDefault(item.Id);
            if (onHand == 0)
            {
                outOfStock++;
            }
            else if (onHand <= item.MinStock)
            {
                reorder++;
            }
        }

        var warnUntil = today.AddDays(expiryWarningDays);
        var expiring = db.Lots.AsNoTracking().Count(l =>
            l.Quantity > 0 && l.ExpiryDate != null && l.ExpiryDate.Value.Date <= warnUntil);
        var purchase = db.Documents.AsNoTracking()
            .Where(d => d.Type == DocumentType.Receipt && !d.IsCancelled
                        && d.DocumentDate.Year == today.Year && d.DocumentDate.Month == today.Month)
            .SelectMany(d => d.Lines)
            .Sum(l => (decimal?)l.Amount) ?? 0m;
        var issue = svc.UsageQuantity(today.Year, today.Month);
        var disposal = db.Documents.AsNoTracking()
            .Where(d => d.Type == DocumentType.Adjustment && !d.IsCancelled
                        && d.AdjustmentType == AdjustmentType.Disposal
                        && d.DocumentDate.Year == today.Year && d.DocumentDate.Month == today.Month)
            .SelectMany(d => d.Lines)
            .Select(l => l.Quantity)
            .AsEnumerable()
            .Sum(q => Math.Abs(q));
        var todayReceipts = db.Documents.AsNoTracking().Count(d =>
            d.Type == DocumentType.Receipt && !d.IsCancelled && d.DocumentDate.Date == today);
        var todayIssues = db.Documents.AsNoTracking().Count(d =>
            d.Type == DocumentType.Issue && !d.IsCancelled && d.DocumentDate.Date == today);
        return new DashboardKpi(
            active, reorder, outOfStock, expiring, purchase, issue, disposal, todayReceipts, todayIssues);
    }

    public static IReadOnlyList<(int Month, decimal Qty)> MonthlyIssueBars(InventoryDbContext db, int year) =>
        Enumerable.Range(1, 12)
            .Select(month => (Month: month, Qty: db.Documents.AsNoTracking()
                .Where(d => d.Type == DocumentType.Issue && !d.IsCancelled
                            && d.DocumentDate.Year == year && d.DocumentDate.Month == month)
                .SelectMany(d => d.Lines)
                .Sum(l => (decimal?)l.Quantity) ?? 0m))
            .Where(row => row.Qty > 0)
            .ToList();

    public static IReadOnlyList<MonthlyQty> TrailingMonthlyIssues(
        InventoryDbContext db, DateTime today, int months = 13, string? itemCode = null)
    {
        var end = new DateTime(today.Year, today.Month, 1);
        var start = end.AddMonths(1 - months);
        var endExclusive = end.AddMonths(1);
        var lines = db.StockLines.AsNoTracking()
            .Where(l => l.Document.Type == DocumentType.Issue && !l.Document.IsCancelled
                        && l.Document.DocumentDate >= start && l.Document.DocumentDate < endExclusive);
        if (!string.IsNullOrWhiteSpace(itemCode))
        {
            var itemId = db.Items.AsNoTracking()
                .Where(i => i.Code == itemCode)
                .Select(i => i.Id)
                .FirstOrDefault();
            lines = itemId == 0 ? lines.Where(_ => false) : lines.Where(l => l.ItemId == itemId);
        }

        var grouped = lines
            .Select(l => new { l.Document.DocumentDate, l.Quantity })
            .AsEnumerable()
            .GroupBy(row => (row.DocumentDate.Year, row.DocumentDate.Month))
            .ToDictionary(g => g.Key, g => g.Sum(row => row.Quantity));

        return Enumerable.Range(0, months)
            .Select(offset =>
            {
                var monthDate = start.AddMonths(offset);
                grouped.TryGetValue((monthDate.Year, monthDate.Month), out var qty);
                return new MonthlyQty(monthDate.Year, monthDate.Month, qty);
            })
            .ToList();
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<MonthlyQty>> TrailingMonthlyIssuesByItems(
        InventoryDbContext db, DateTime today, IReadOnlyCollection<string> itemCodes, int months = 13)
    {
        var end = new DateTime(today.Year, today.Month, 1);
        var start = end.AddMonths(1 - months);
        var endExclusive = end.AddMonths(1);
        var codes = itemCodes.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        if (codes.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<MonthlyQty>>();
        }

        var codeToId = db.Items.AsNoTracking()
            .Where(i => codes.Contains(i.Code))
            .Select(i => new { i.Id, i.Code })
            .ToList()
            .ToDictionary(x => x.Code, x => x.Id, StringComparer.Ordinal);
        var ids = codeToId.Values.ToList();
        var grouped = ids.Count == 0
            ? new Dictionary<(int ItemId, int Year, int Month), decimal>()
            : db.StockLines.AsNoTracking()
                .Where(l => ids.Contains(l.ItemId)
                            && l.Document.Type == DocumentType.Issue && !l.Document.IsCancelled
                            && l.Document.DocumentDate >= start && l.Document.DocumentDate < endExclusive)
                .Select(l => new { l.ItemId, l.Document.DocumentDate, l.Quantity })
                .AsEnumerable()
                .GroupBy(row => (row.ItemId, row.DocumentDate.Year, row.DocumentDate.Month))
                .ToDictionary(g => g.Key, g => g.Sum(row => row.Quantity));

        var result = new Dictionary<string, IReadOnlyList<MonthlyQty>>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            var itemId = codeToId.GetValueOrDefault(code);
            result[code] = Enumerable.Range(0, months)
                .Select(offset =>
                {
                    var monthDate = start.AddMonths(offset);
                    grouped.TryGetValue((itemId, monthDate.Year, monthDate.Month), out var qty);
                    return new MonthlyQty(monthDate.Year, monthDate.Month, qty);
                })
                .ToList();
        }

        return result;
    }
}

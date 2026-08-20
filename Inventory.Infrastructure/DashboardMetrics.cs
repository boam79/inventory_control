namespace Inventory.Infrastructure;

public sealed record DashboardKpi(
    int ActiveItems,
    int ReorderItems,
    int OutOfStockItems,
    int ExpiringLots,
    decimal MonthPurchaseAmount,
    decimal MonthIssueQty,
    decimal MonthDisposalQty);

public static class DashboardMetrics
{
    public static DashboardKpi Build(InventoryDbContext db, InventoryService svc, DateTime today, int expiryWarningDays = 90)
    {
        var snaps = svc.SearchStockSnapshots(string.Empty, expiryWarningDays);
        var active = db.Items.Count(i => i.IsActive);
        var reorder = snaps.Count(s => s.Status == StockStatusKind.Reorder);
        var outOfStock = snaps.Count(s => s.Status == StockStatusKind.OutOfStock);
        var warnUntil = today.AddDays(expiryWarningDays);
        var expiring = db.Lots.Count(l =>
            l.Quantity > 0 && l.ExpiryDate != null && l.ExpiryDate.Value.Date <= warnUntil);
        var purchase = db.Documents
            .Where(d => d.Type == DocumentType.Receipt && !d.IsCancelled
                        && d.DocumentDate.Year == today.Year && d.DocumentDate.Month == today.Month)
            .SelectMany(d => d.Lines)
            .Sum(l => (decimal?)l.Amount) ?? 0m;
        var issue = svc.UsageQuantity(today.Year, today.Month);
        var disposal = db.Documents
            .Where(d => d.Type == DocumentType.Adjustment && !d.IsCancelled
                        && d.AdjustmentType == AdjustmentType.Disposal
                        && d.DocumentDate.Year == today.Year && d.DocumentDate.Month == today.Month)
            .SelectMany(d => d.Lines)
            .Select(l => l.Quantity)
            .AsEnumerable()
            .Sum(q => Math.Abs(q));
        return new DashboardKpi(active, reorder, outOfStock, expiring, purchase, issue, disposal);
    }

    public static IReadOnlyList<(int Month, decimal Qty)> MonthlyIssueBars(InventoryDbContext db, int year) =>
        Enumerable.Range(1, 12)
            .Select(month => (Month: month, Qty: db.Documents
                .Where(d => d.Type == DocumentType.Issue && !d.IsCancelled
                            && d.DocumentDate.Year == year && d.DocumentDate.Month == month)
                .SelectMany(d => d.Lines)
                .Sum(l => (decimal?)l.Quantity) ?? 0m))
            .Where(row => row.Qty > 0)
            .ToList();
}

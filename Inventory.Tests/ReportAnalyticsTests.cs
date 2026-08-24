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

    [Fact]
    public void Department_dimension_splits_issues_by_department_across_months()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("D001", "장갑", "소독", "개", "개", 10);
        svc.SaveOpeningDraft("D001", "OPEN", 500, new DateTime(2026, 1, 1), new DateTime(2027, 1, 1));
        svc.ConfirmOpening("D001");
        var er = svc.CreateDepartment("외래");
        var jinche = svc.CreateDepartment("시술실");
        svc.Issue(new DateTime(2026, 6, 5), er.Id, [new IssueLineRequest { ItemCode = "D001", Quantity = 5, LotNumber = "OPEN" }]);
        svc.Issue(new DateTime(2026, 7, 5), jinche.Id, [new IssueLineRequest { ItemCode = "D001", Quantity = 8, LotNumber = "OPEN" }]);

        var june = ReportAnalytics.Query(db, ReportPeriodKind.Month, new DateTime(2026, 6, 15), ReportDimension.Department);
        Assert.Single(june);
        Assert.Equal("외래", june.Single().Dimension);
        Assert.Equal(5m, june.Single().IssueQty);

        var july = ReportAnalytics.Query(db, ReportPeriodKind.Month, new DateTime(2026, 7, 15), ReportDimension.Department);
        Assert.Single(july);
        Assert.Equal("시술실", july.Single().Dimension);
        Assert.Equal(8m, july.Single().IssueQty);
    }

    [Fact]
    public void StepBack_moves_anchor_by_whole_periods()
    {
        var anchor = new DateTime(2026, 7, 15);
        Assert.Equal(new DateTime(2026, 6, 15), ReportAnalytics.StepBack(ReportPeriodKind.Month, anchor, 1));
        Assert.Equal(new DateTime(2026, 1, 15), ReportAnalytics.StepBack(ReportPeriodKind.Quarter, anchor, 2));
        Assert.Equal(new DateTime(2024, 7, 15), ReportAnalytics.StepBack(ReportPeriodKind.Year, anchor, 2));
        Assert.Equal(new DateTime(2026, 7, 10), ReportAnalytics.StepBack(ReportPeriodKind.Day, anchor, 5));
        Assert.Equal(anchor, ReportAnalytics.StepBack(ReportPeriodKind.Custom, anchor, 3));
    }

    [Fact]
    public void Month_close_preview_counts_receipts_and_issues()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M009", "거즈", "드레싱", "개", "개", 10);
        svc.SaveOpeningDraft("M009", "OPEN", 50, new DateTime(2026, 1, 1), new DateTime(2027, 1, 1));
        svc.ConfirmOpening("M009");
        svc.Receive(new DateTime(2026, 8, 2), null, "R08",
            [new ReceiptLineRequest { ItemCode = "M009", Quantity = 5, UnitPrice = 100, LotNumber = "C", ExpiryDate = new DateTime(2027, 1, 1) }]);
        svc.Issue(new DateTime(2026, 8, 3), null, [new IssueLineRequest { ItemCode = "M009", Quantity = 2, LotNumber = "C" }]);

        var preview = ReportAnalytics.PreviewMonth(db, 2026, 8);
        Assert.False(preview.IsClosed);
        Assert.Equal(1, preview.ReceiptDocs);
        Assert.Equal(1, preview.IssueDocs);
        Assert.Equal(5m, preview.ReceiptQty);
        Assert.Equal(2m, preview.IssueQty);
        Assert.Equal(500m, preview.PurchaseAmount);
        Assert.Equal(ReportUi.IssueQty, "출고수량");
        Assert.Equal(ReportUi.Period, "기간");
    }

    [Fact]
    public void Trailing_monthly_issues_can_filter_by_item()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var svc = new InventoryService(db, "admin");
        svc.CreateItem("M001", "주사기", "주사", "개", "개", 10);
        svc.CreateItem("M002", "거즈", "드레싱", "개", "개", 10);
        svc.SaveOpeningDraft("M001", "OPEN", 100, new DateTime(2025, 1, 1), new DateTime(2027, 1, 1));
        svc.SaveOpeningDraft("M002", "OPEN", 100, new DateTime(2025, 1, 1), new DateTime(2027, 1, 1));
        svc.ConfirmOpening("M001");
        svc.ConfirmOpening("M002");
        svc.Issue(new DateTime(2026, 8, 3), null, [new IssueLineRequest { ItemCode = "M001", Quantity = 4, LotNumber = "OPEN" }]);
        svc.Issue(new DateTime(2026, 8, 4), null, [new IssueLineRequest { ItemCode = "M002", Quantity = 7, LotNumber = "OPEN" }]);

        var today = new DateTime(2026, 8, 21);
        var all = DashboardMetrics.TrailingMonthlyIssues(db, today, 13);
        var m001 = DashboardMetrics.TrailingMonthlyIssues(db, today, 13, "M001");
        var m002 = DashboardMetrics.TrailingMonthlyIssues(db, today, 13, "M002");
        var missing = DashboardMetrics.TrailingMonthlyIssues(db, today, 13, "NOPE");
        Assert.Equal(11m, all.Single(s => s.Year == 2026 && s.Month == 8).Qty);
        Assert.Equal(4m, m001.Single(s => s.Year == 2026 && s.Month == 8).Qty);
        Assert.Equal(7m, m002.Single(s => s.Year == 2026 && s.Month == 8).Qty);
        Assert.Equal(0m, missing.Single(s => s.Year == 2026 && s.Month == 8).Qty);

        var byItems = DashboardMetrics.TrailingMonthlyIssuesByItems(db, today, ["M001", "M002"]);
        Assert.Equal(4m, byItems["M001"].Single(s => s.Year == 2026 && s.Month == 8).Qty);
        Assert.Equal(7m, byItems["M002"].Single(s => s.Year == 2026 && s.Month == 8).Qty);

        var plot = DashboardChartBuilder.Build(byItems, [("M001", "주사기"), ("M002", "거즈")]);
        Assert.Equal(2, plot.Lines.Count);
        Assert.Equal(13, plot.Labels.Count);
        Assert.Equal(13, plot.Lines[0].Actual.Count);
        Assert.True(DashboardChartBuilder.HasDrawableActual(plot.Lines[0]));
        Assert.True(DashboardChartBuilder.HasDrawableActual(plot.Lines[1]));
        Assert.Equal(4d, plot.Lines[0].Actual[^1]);
        Assert.Equal(7d, plot.Lines[1].Actual[^1]);
        Assert.All(plot.Lines, line => Assert.DoesNotContain(line.Actual, double.IsNaN));

        svc.CreateItem("M003", "장갑", "소독", "개", "개", 10);
        svc.CreateItem("M004", "알코올", "소독", "개", "개", 10);
        svc.SaveOpeningDraft("M003", "OPEN", 100, new DateTime(2025, 1, 1), new DateTime(2027, 1, 1));
        svc.SaveOpeningDraft("M004", "OPEN", 100, new DateTime(2025, 1, 1), new DateTime(2027, 1, 1));
        svc.ConfirmOpening("M003");
        svc.ConfirmOpening("M004");
        svc.Issue(new DateTime(2026, 8, 5), null, [new IssueLineRequest { ItemCode = "M003", Quantity = 9, LotNumber = "OPEN" }]);
        svc.Issue(new DateTime(2026, 8, 6), null, [new IssueLineRequest { ItemCode = "M004", Quantity = 11, LotNumber = "OPEN" }]);
        var four = DashboardMetrics.TrailingMonthlyIssuesByItems(db, today, ["M001", "M002", "M003", "M004"]);
        var fourPlot = DashboardChartBuilder.Build(four, [("M001", "주사기"), ("M002", "거즈"), ("M003", "장갑"), ("M004", "알코올")]);
        Assert.Equal(4, fourPlot.Lines.Count);
        Assert.Equal(4, fourPlot.Lines.Select(l => l.Code).Distinct().Count());
        Assert.Equal(new[] { 4d, 7d, 9d, 11d }, fourPlot.Lines.Select(l => l.Actual[^1]).ToArray());

        // Each item gets its own mini chart (no shared scale), so a huge item and a tiny item
        // do not distort each other; each line keeps its own actual+forecast series.
        var combinedLabels = DashboardChartBuilder.CombinedLabels(fourPlot);
        Assert.Equal(16, combinedLabels.Count);
        Assert.Equal(4, combinedLabels.Count(l => l.Length > 0));

        foreach (var line in fourPlot.Lines)
        {
            var actualWithGap = DashboardChartBuilder.ActualWithGap(line);
            var forecastWithAnchor = DashboardChartBuilder.ForecastWithAnchor(line);
            Assert.Equal(16, actualWithGap.Length);
            Assert.Equal(16, forecastWithAnchor.Length);
            Assert.True(double.IsNaN(actualWithGap[13]));
            Assert.Equal(line.Actual[^1], forecastWithAnchor[12]);
            Assert.True(double.IsNaN(forecastWithAnchor[0]));
        }

        IReadOnlyList<MonthlyQty> Flat(decimal qty) =>
            Enumerable.Range(0, 13)
                .Select(i =>
                {
                    var month = new DateTime(2025, 8, 1).AddMonths(i);
                    return new MonthlyQty(month.Year, month.Month, qty);
                })
                .ToList();
        var mixed = DashboardChartBuilder.Build(
            new Dictionary<string, IReadOnlyList<MonthlyQty>>
            {
                ["A"] = Flat(800),
                ["B"] = Flat(5)
            },
            [("A", "대량품목"), ("B", "소량품목")]);
        Assert.Equal(800d, mixed.Lines[0].Actual[^1]);
        Assert.Equal(5d, mixed.Lines[1].Actual[^1]);
        Assert.Contains("대량품목", mixed.Insight, StringComparison.Ordinal);

        var aggregateMonthly = Enumerable.Range(0, 13)
            .Select(i =>
            {
                var month = new DateTime(2025, 8, 1).AddMonths(i);
                return new MonthlyQty(month.Year, month.Month, 100 + i * 5);
            })
            .ToList();
        var aggregateLine = DashboardChartBuilder.BuildAggregateLine(aggregateMonthly);
        Assert.Equal("__aggregate__", aggregateLine.Code);
        Assert.Equal(13, aggregateLine.Actual.Count);
        Assert.Equal(3, aggregateLine.Forecast.Count);
        var (nextQty, deltaPct, hasForecast) = DashboardChartBuilder.NextMonthOutlook(aggregateLine);
        Assert.True(hasForecast);
        Assert.True(nextQty > 0);
        Assert.Contains("다음달 예상 출고", DashboardChartBuilder.FormatNextMonthBadge(aggregateLine), StringComparison.Ordinal);
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

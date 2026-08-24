namespace Inventory.Infrastructure;

public sealed record DashboardChartLine(
    string Name,
    string Code,
    IReadOnlyList<double> Actual,
    IReadOnlyList<double> Forecast);

public sealed record DashboardChartPlot(
    IReadOnlyList<string> Labels,
    IReadOnlyList<DashboardChartLine> Lines,
    string Insight);

public static class DashboardChartBuilder
{
    public static DashboardChartPlot Build(
        IReadOnlyDictionary<string, IReadOnlyList<MonthlyQty>> monthly,
        IReadOnlyList<(string Code, string Name)> items)
    {
        if (items.Count == 0)
        {
            return new DashboardChartPlot([], [], string.Empty);
        }

        var first = monthly.GetValueOrDefault(items[0].Code) ?? monthly.Values.FirstOrDefault() ?? [];
        var labels = first.Select(s => $"{s.Year}-{s.Month:00}").ToArray();
        var lines = new List<DashboardChartLine>();
        foreach (var item in items)
        {
            var months = monthly.GetValueOrDefault(item.Code) ?? first;
            var hist = months.Select(s => (double)s.Qty).ToArray();
            var forecast = UsageForecast.Predict(months.Select(s => s.Qty).ToList());
            var predicted = forecast.Available
                ? forecast.Future.Select(v => (double)v).ToArray()
                : [double.NaN, double.NaN, double.NaN];
            lines.Add(new DashboardChartLine(item.Name, item.Code, hist, predicted));
        }

        return new DashboardChartPlot(labels, lines, Insight(lines));
    }

    public static bool HasDrawableActual(DashboardChartLine line) =>
        line.Actual.Any(v => !double.IsNaN(v) && !double.IsInfinity(v));

    /// <summary>Actual history labels plus three forecast month markers, for a per-item mini chart's X axis.</summary>
    public static IReadOnlyList<string> CombinedLabels(DashboardChartPlot plot, bool sparse = true)
    {
        var all = plot.Labels.Concat(["예측+1", "예측+2", "예측+3"]).ToArray();
        if (!sparse || all.Length == 0)
        {
            return all;
        }

        var last = all.Length - 1;
        var midHistory = Math.Max(0, plot.Labels.Count / 2);
        var keep = new HashSet<int> { 0, midHistory, plot.Labels.Count - 1, last };
        return all.Select((label, index) => keep.Contains(index) ? label : "").ToArray();
    }

    /// <summary>Actual quantities padded with NaN over the three forecast slots, so the series stops at the last real month.</summary>
    public static double[] ActualWithGap(DashboardChartLine line) =>
        line.Actual.Concat([double.NaN, double.NaN, double.NaN]).ToArray();

    /// <summary>NaN over history except the last real month (anchor), followed by the forecast, so the dashed segment visually continues the solid line.</summary>
    public static double[] ForecastWithAnchor(DashboardChartLine line)
    {
        var result = new double[line.Actual.Count + 3];
        Array.Fill(result, double.NaN);
        if (line.Actual.Count > 0)
        {
            result[line.Actual.Count - 1] = line.Actual[^1];
        }

        for (var i = 0; i < Math.Min(3, line.Forecast.Count); i++)
        {
            result[line.Actual.Count + i] = line.Forecast[i];
        }

        return result;
    }

    public static string Insight(IReadOnlyList<DashboardChartLine> lines)
    {
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var latest = lines
            .Select(line => (line.Name, Qty: line.Actual.Count == 0 ? 0 : line.Actual[^1]))
            .OrderByDescending(x => x.Qty)
            .First();
        var rising = lines.Where(IsRising).Select(l => l.Name).Take(3).ToArray();
        var falling = lines.Where(IsFalling).Select(l => l.Name).Take(3).ToArray();
        var text = $"최근 달 출고가 가장 많은 품목은 {latest.Name} {latest.Qty:N0}개입니다.";
        if (rising.Length > 0)
        {
            text += $" 예측이 오르는 품목: {string.Join(", ", rising)}.";
        }

        if (falling.Length > 0)
        {
            text += $" 예측이 내리는 품목: {string.Join(", ", falling)}.";
        }

        return text;
    }

    public static bool IsRising(DashboardChartLine line) =>
        line.Actual.Count > 0 && line.Forecast.Count > 0
        && !double.IsNaN(line.Forecast[0])
        && line.Forecast[0] > line.Actual[^1] + 0.5;

    public static bool IsFalling(DashboardChartLine line) =>
        line.Actual.Count > 0 && line.Forecast.Count > 0
        && !double.IsNaN(line.Forecast[0])
        && line.Forecast[0] < line.Actual[^1] - 0.5;

    /// <summary>Aggregate monthly outbound for the dashboard hero chart.</summary>
    public static DashboardChartLine BuildAggregateLine(
        IReadOnlyList<MonthlyQty> monthly,
        string name = "전체 출고")
    {
        var hist = monthly.Select(s => (double)s.Qty).ToArray();
        var forecast = UsageForecast.Predict(monthly.Select(s => s.Qty).ToList());
        var predicted = forecast.Available
            ? forecast.Future.Select(v => (double)v).ToArray()
            : [double.NaN, double.NaN, double.NaN];
        return new DashboardChartLine(name, "__aggregate__", hist, predicted);
    }

    public static string[] HeroLabels(IReadOnlyList<string> historyLabels) =>
        historyLabels.Concat(["예측+1", "예측+2", "예측+3"]).ToArray();

    public static (double NextQty, double DeltaPct, bool HasForecast) NextMonthOutlook(DashboardChartLine line)
    {
        if (line.Forecast.Count == 0 || double.IsNaN(line.Forecast[0]))
        {
            return (0, 0, false);
        }

        var lastActual = line.Actual.Count == 0 ? 0 : line.Actual[^1];
        var next = line.Forecast[0];
        if (lastActual <= 0)
        {
            return (next, 0, true);
        }

        return (next, (next - lastActual) / lastActual * 100, true);
    }

    public static string FormatNextMonthBadge(DashboardChartLine line)
    {
        var (nextQty, deltaPct, hasForecast) = NextMonthOutlook(line);
        if (!hasForecast)
        {
            return "다음달 예상 출고 —";
        }

        if (line.Actual.Count == 0 || line.Actual[^1] <= 0)
        {
            return $"다음달 예상 출고 {nextQty:N0}";
        }

        if (Math.Abs(deltaPct) < 0.5)
        {
            return $"다음달 예상 출고 {nextQty:N0} −";
        }

        var arrow = deltaPct > 0 ? "▲" : "▼";
        return $"다음달 예상 출고 {nextQty:N0} {arrow}{Math.Abs(deltaPct):N0}%";
    }
}

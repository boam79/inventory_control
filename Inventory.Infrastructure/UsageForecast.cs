using MathNet.Numerics.Statistics;
using Microsoft.ML;
using Microsoft.ML.Transforms.TimeSeries;

namespace Inventory.Infrastructure;

public sealed record ForecastResult(
    bool Available,
    string ModelName,
    IReadOnlyList<decimal> Future,
    string Warning);

public interface IOnnxForecastEngine
{
    bool TryPredict(IReadOnlyList<decimal> history, out IReadOnlyList<decimal> future);
}

public sealed class DisabledOnnxForecastEngine : IOnnxForecastEngine
{
    public bool TryPredict(IReadOnlyList<decimal> history, out IReadOnlyList<decimal> future)
    {
        future = Array.Empty<decimal>();
        return false;
    }
}

public static class UsageForecast
{
    private sealed class SeriesPoint
    {
        public float Value { get; set; }
    }

    private sealed class SeriesForecast
    {
        public float[] Forecast { get; set; } = Array.Empty<float>();
    }

    public static ForecastResult Predict(
        IReadOnlyList<decimal> history,
        IOnnxForecastEngine? onnx = null)
    {
        onnx ??= new DisabledOnnxForecastEngine();
        if (history.Count < 3)
        {
            return new ForecastResult(
                false,
                "none",
                Array.Empty<decimal>(),
                "데이터 부족: 예측 불가 또는 단순 평균만 가능합니다.");
        }

        if (onnx.TryPredict(history, out var deep) && deep.Count > 0)
        {
            return new ForecastResult(true, "ONNX", deep, string.Empty);
        }

        var ssa = TrySsa(history);
        if (ssa is not null)
        {
            return ssa;
        }

        var window = history.TakeLast(Math.Min(3, history.Count)).Select(v => (double)v).ToArray();
        var avg = (decimal)window.Mean();
        return new ForecastResult(true, "SMA3", new[] { avg, avg, avg }, string.Empty);
    }

    private static ForecastResult? TrySsa(IReadOnlyList<decimal> history)
    {
        try
        {
            if (history.Count < 6)
            {
                return null;
            }

            var ml = new MLContext(seed: 1);
            var data = history.Select(v => new SeriesPoint { Value = (float)v }).ToList();
            var view = ml.Data.LoadFromEnumerable(data);
            var windowSize = Math.Max(2, Math.Min(4, history.Count / 2));
            var pipeline = ml.Forecasting.ForecastBySsa(
                outputColumnName: nameof(SeriesForecast.Forecast),
                inputColumnName: nameof(SeriesPoint.Value),
                windowSize: windowSize,
                seriesLength: history.Count,
                trainSize: history.Count,
                horizon: 3);
            var model = pipeline.Fit(view);
            var engine = model.CreateTimeSeriesEngine<SeriesPoint, SeriesForecast>(ml);
            var pred = engine.Predict();
            if (pred.Forecast is null || pred.Forecast.Length == 0)
            {
                return null;
            }

            return new ForecastResult(
                true,
                "ML.NET-SSA",
                pred.Forecast.Select(v => (decimal)Math.Max(0, v)).ToArray(),
                string.Empty);
        }
        catch
        {
            return null;
        }
    }
}

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Inventory.Infrastructure;

public sealed class OnnxCpuEngine : IOnnxForecastEngine, IDisposable
{
    private readonly InferenceSession _session;
    private bool _disposed;

    private OnnxCpuEngine(InferenceSession session) => _session = session;

    public static OnnxCpuEngine? TryCreate()
    {
        try
        {
            var options = new SessionOptions
            {
                LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
            };
            var session = new InferenceSession(OnnxIdentityModel.Create(), options);
            if (Math.Abs(Infer(session, 1f) - 1f) > 0.01f)
            {
                session.Dispose();
                return null;
            }

            return new OnnxCpuEngine(session);
        }
        catch
        {
            return null;
        }
    }

    public bool TryHello(out float output)
    {
        try
        {
            output = Infer(_session, 1f);
            return Math.Abs(output - 1f) <= 0.01f;
        }
        catch
        {
            output = 0;
            return false;
        }
    }

    public bool TryPredict(IReadOnlyList<decimal> history, out IReadOnlyList<decimal> future)
    {
        future = Array.Empty<decimal>();
        if (history.Count == 0)
        {
            return false;
        }

        try
        {
            var value = (decimal)Math.Max(0, Infer(_session, (float)history[^1]));
            future = new[] { value, value, value };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static float Infer(InferenceSession session, float input)
    {
        var tensor = new DenseTensor<float>(new[] { input }, new[] { 1 });
        using var results = session.Run([NamedOnnxValue.CreateFromTensor("X", tensor)]);
        return results.First().AsEnumerable<float>().First();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session.Dispose();
        _disposed = true;
    }
}

using Serilog;
using Serilog.Core;
using System.Text.RegularExpressions;

namespace Inventory.Infrastructure;

public static class AppLog
{
    private static readonly Regex ResidentId = new(@"\d{6}-?\d{7}", RegexOptions.Compiled);
    private static readonly Regex PatientWord = new(@"환자\s*\S+", RegexOptions.Compiled);

    public static Logger CreateFileLogger(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "inventory-.log");
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .Filter.ByExcluding(log =>
            {
                var text = log.RenderMessage();
                return ResidentId.IsMatch(text) || PatientWord.IsMatch(text);
            })
            .WriteTo.File(path, rollingInterval: RollingInterval.Day)
            .CreateLogger();
    }

    public static string Sanitize(string text)
    {
        var withoutId = ResidentId.Replace(text, "[redacted]");
        return PatientWord.Replace(withoutId, "환자 [redacted]");
    }

    public static bool TryRun(Action action, ILogger logger)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, AppLog.Sanitize(ex.Message));
            return false;
        }
    }
}

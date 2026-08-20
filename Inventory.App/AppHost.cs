using Inventory.Core;
using Inventory.Infrastructure;

namespace Inventory.App;

internal static class AppHost
{
    public static string DatabasePath =>
        AppSession.Current?.DatabasePath ?? SqliteConnectionString.DefaultDatabasePath();

    public static string Actor => AppSession.Current?.UserName ?? "system";

    public static InventoryDbContext OpenDb() => InventoryDatabase.CreateContext(DatabasePath);

    public static string Run(Func<InventoryDbContext, InventoryService, string?> work)
    {
        using var db = OpenDb();
        var svc = new InventoryService(db, Actor);
        try
        {
            return work(db, svc) ?? string.Empty;
        }
        catch (Exception ex)
        {
            return $"원인: {AppLog.Sanitize(ex.Message)}\n조치: 입력값을 확인한 뒤 다시 시도하세요.";
        }
    }
}

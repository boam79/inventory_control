using Inventory.Core;
using Inventory.Infrastructure;

namespace Inventory.Tests;

public class QuerySpeedTests
{
    [Fact]
    public void Stock_snapshot_query_does_not_send_a_giant_id_list()
    {
        var src = ReadRepoFile("Inventory.Infrastructure", "InventoryService.cs");
        Assert.DoesNotContain("ids.Contains", src, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking()", src, StringComparison.Ordinal);
        Assert.Contains("ListDocumentSummaries", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_does_not_scan_all_stock_snapshots()
    {
        var src = ReadRepoFile("Inventory.Infrastructure", "DashboardMetrics.cs");
        Assert.DoesNotContain("SearchStockSnapshots", src, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking()", src, StringComparison.Ordinal);
        Assert.Contains("l.Document.DocumentDate", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Menu_click_does_not_rebuild_the_whole_nav()
    {
        var src = ReadRepoFile("Inventory.App", "MainWindow.xaml.cs");
        Assert.Contains("HighlightNav()", src, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke", src, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildMenu()", src, StringComparison.Ordinal);
        Assert.Contains("BuildNav()", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Sqlite_connection_is_pooled_and_shared()
    {
        var cs = SqliteConnectionString.FromFile("C:\\tmp\\inventory.db");
        Assert.Contains("Cache=Shared", cs, StringComparison.Ordinal);
        Assert.Contains("Pooling=True", cs, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}

using System.Windows;
using Velopack;

namespace Inventory.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        if (args.Any(a => string.Equals(a, "--seed-demo", StringComparison.OrdinalIgnoreCase)))
        {
            var force = args.Any(a => string.Equals(a, "--force", StringComparison.OrdinalIgnoreCase));
            var dbPath = Inventory.Core.SqliteConnectionString.DefaultDatabasePath();
            Inventory.Infrastructure.InventoryDatabase.Initialize(dbPath);
            using var db = Inventory.Infrastructure.InventoryDatabase.CreateContext(dbPath);
            var result = Inventory.Infrastructure.DemoSeedService.Generate(db, DateTime.Today, force);
            Console.WriteLine(result.Message);
            Environment.ExitCode = result.Applied ? 0 : 2;
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}

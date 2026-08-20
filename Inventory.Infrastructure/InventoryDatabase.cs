using Inventory.Core;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure;

public static class InventoryDatabase
{
    public const string SchemaVersionKey = "SchemaVersion";
    public const string SchemaVersionValue = "1";

    public static InventoryDbContext CreateContext(string dbPath)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(SqliteConnectionString.FromFile(dbPath))
            .Options;
        return new InventoryDbContext(options);
    }

    public static void Initialize(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        var folder = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        using var db = CreateContext(dbPath);
        db.Database.Migrate();

        if (!db.AppMeta.Any(row => row.Key == SchemaVersionKey))
        {
            db.AppMeta.Add(new AppMeta
            {
                Key = SchemaVersionKey,
                Value = SchemaVersionValue
            });
            db.SaveChanges();
        }
    }
}

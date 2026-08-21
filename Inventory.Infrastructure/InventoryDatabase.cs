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
        var db = new InventoryDbContext(options);
        ApplySqlitePragmas(db);
        return db;
    }

    private static void ApplySqlitePragmas(InventoryDbContext db)
    {
        db.Database.OpenConnection();
        var conn = db.Database.GetDbConnection();
        foreach (var sql in new[]
                 {
                     "PRAGMA journal_mode=WAL;",
                     "PRAGMA synchronous=NORMAL;",
                     "PRAGMA cache_size=-16000;",
                     "PRAGMA temp_store=MEMORY;"
                 })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    private static void EnsureReadIndexes(InventoryDbContext db)
    {
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Items_Name ON Items(Name);");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Lots_ExpiryDate ON Lots(ExpiryDate);");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Documents_TypeDate ON Documents(Type, IsCancelled, DocumentDate);");
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
        ApplySqlitePragmas(db);
        EnsureReadIndexes(db);

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

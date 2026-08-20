using Inventory.Infrastructure;

namespace Inventory.Tests;

public sealed class SqliteDatabaseTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"springclinic-inventory-{Guid.NewGuid():N}.db");

    [Fact]
    public void Initialize_creates_sqlite_file_connects_and_deletes()
    {
        InventoryDatabase.Initialize(_dbPath);

        Assert.True(File.Exists(_dbPath), "마이그레이션 후 SQLite 파일이 있어야 한다.");

        using (var db = InventoryDatabase.CreateContext(_dbPath))
        {
            Assert.True(db.Database.CanConnect());
            Assert.Equal("1", db.AppMeta.Single(row => row.Key == "SchemaVersion").Value);
        }

        DisposeSqliteFiles();
        Assert.False(File.Exists(_dbPath), "테스트 종료 후 임시 DB 파일은 삭제되어야 한다.");
    }

    public void Dispose() => DisposeSqliteFiles();

    private void DisposeSqliteFiles()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[]
                 {
                     _dbPath,
                     _dbPath + "-wal",
                     _dbPath + "-shm",
                     _dbPath + "-journal"
                 })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

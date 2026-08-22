using Inventory.Core;
using Inventory.Infrastructure;

namespace Inventory.Tests;

public sealed class AuthenticationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"springclinic-auth-{Guid.NewGuid():N}.db");

    public AuthenticationTests()
    {
        InventoryDatabase.Initialize(_dbPath);
    }

    [Fact]
    public void Empty_store_creates_default_local_administrator()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var auth = new AuthenticationService(db);

        var (userName, role) = auth.EnsureLocalOperator();

        Assert.Equal(AuthenticationService.DefaultLocalUserName, userName);
        Assert.Equal(UserRole.Administrator, role);
        Assert.True(auth.HasAnyUser());
        Assert.Equal(1, db.Users.Count());
    }

    [Fact]
    public void Existing_administrator_is_preferred_over_creating_local()
    {
        AddUser("clinic-admin", "admin-password", UserRole.Administrator);
        AddUser("nurse", "nurse-pass", UserRole.DepartmentUser);

        using var db = InventoryDatabase.CreateContext(_dbPath);
        var (userName, role) = new AuthenticationService(db).EnsureLocalOperator();

        Assert.Equal("clinic-admin", userName);
        Assert.Equal(UserRole.Administrator, role);
        Assert.DoesNotContain(db.Users, u => u.UserName == AuthenticationService.DefaultLocalUserName);
    }

    [Fact]
    public void Without_admin_ensures_local_administrator()
    {
        AddUser("nurse", "nurse-pass", UserRole.DepartmentUser);

        using var db = InventoryDatabase.CreateContext(_dbPath);
        var (userName, role) = new AuthenticationService(db).EnsureLocalOperator();

        Assert.Equal(AuthenticationService.DefaultLocalUserName, userName);
        Assert.Equal(UserRole.Administrator, role);
        Assert.True(db.Users.Any(u =>
            u.UserName == AuthenticationService.DefaultLocalUserName
            && u.Role == UserRole.Administrator
            && u.IsActive));
    }

    [Fact]
    public void CreateUser_rejects_duplicate_name()
    {
        AddUser("buyer", "good-password", UserRole.Purchasing);

        using var db = InventoryDatabase.CreateContext(_dbPath);
        var auth = new AuthenticationService(db);
        Assert.Throws<InvalidOperationException>(() =>
            auth.CreateUser("buyer", "other-pass", UserRole.Viewer));
    }

    public void Dispose()
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

    private void AddUser(string userName, string password, UserRole role)
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        db.Users.Add(new UserAccount
        {
            UserName = userName,
            PasswordHash = PasswordHasher.Hash(password),
            Role = role,
            IsActive = true
        });
        db.SaveChanges();
    }
}

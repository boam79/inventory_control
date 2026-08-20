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
    public void Wrong_password_fails()
    {
        AddUser("buyer", "good-password", UserRole.Purchasing);

        using var db = InventoryDatabase.CreateContext(_dbPath);
        var auth = new AuthenticationService(db);
        var result = auth.SignIn("buyer", "bad-password");

        Assert.False(result.Succeeded);
        Assert.Null(result.Permissions);
    }

    [Fact]
    public void Unknown_user_fails()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var result = new AuthenticationService(db).SignIn("nobody", "any");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Successful_login_returns_role_permissions_from_core()
    {
        AddUser("admin", "admin-password", UserRole.Administrator);

        using var db = InventoryDatabase.CreateContext(_dbPath);
        var result = new AuthenticationService(db).SignIn("admin", "admin-password");

        Assert.True(result.Succeeded);
        Assert.Equal("admin", result.UserName);
        Assert.Equal(UserRole.Administrator, result.Role);
        Assert.Equal(RolePermissions.For(UserRole.Administrator), result.Permissions);
        Assert.True(result.Permissions!.CanManageUsers);
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

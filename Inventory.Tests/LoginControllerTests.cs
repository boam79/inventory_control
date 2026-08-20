using Inventory.Core;
using Inventory.Infrastructure;

namespace Inventory.Tests;

public sealed class LoginControllerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"springclinic-login-{Guid.NewGuid():N}.db");

    public LoginControllerTests() => InventoryDatabase.Initialize(_dbPath);

    [Fact]
    public void Wrong_password_shows_korean_cause_and_action()
    {
        using (var setup = InventoryDatabase.CreateContext(_dbPath))
        {
            new AuthenticationService(setup).CreateUser("nurse", "ok-pass", UserRole.DepartmentUser);
        }

        using var db = InventoryDatabase.CreateContext(_dbPath);
        var login = new LoginController(db);
        login.Login("nurse", "wrong");

        Assert.False(login.OpenMainShell);
        Assert.Contains("원인:", login.ErrorMessage);
        Assert.Contains("조치:", login.ErrorMessage);
        Assert.Contains("비밀번호", login.ErrorMessage);
    }

    [Fact]
    public void Successful_login_opens_main_shell()
    {
        using (var setup = InventoryDatabase.CreateContext(_dbPath))
        {
            new AuthenticationService(setup).CreateUser("admin", "admin-pass", UserRole.Administrator);
        }

        using var db = InventoryDatabase.CreateContext(_dbPath);
        var login = new LoginController(db);
        login.Login("admin", "admin-pass");

        Assert.True(login.OpenMainShell);
        Assert.Equal("admin", login.SignedInUser);
        Assert.Equal(UserRole.Administrator, login.SignedInRole);
        Assert.True(string.IsNullOrEmpty(login.ErrorMessage));
    }

    [Fact]
    public void First_admin_can_be_created_when_store_is_empty()
    {
        using var db = InventoryDatabase.CreateContext(_dbPath);
        var login = new LoginController(db);
        Assert.True(login.NeedsFirstAdmin);
        login.CreateFirstAdministrator("root", "root-pass");
        Assert.True(login.OpenMainShell);
        Assert.Equal("root", login.SignedInUser);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm", _dbPath + "-journal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

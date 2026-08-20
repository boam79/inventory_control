using Inventory.Core;

namespace Inventory.Infrastructure;

public sealed class AuthenticationService
{
    private readonly InventoryDbContext _db;

    public AuthenticationService(InventoryDbContext db)
    {
        _db = db;
    }

    public LoginAttempt SignIn(string userName, string password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return LoginAttempt.Fail();
        }

        var user = _db.Users.SingleOrDefault(row =>
            row.IsActive && row.UserName == userName);
        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
        {
            return LoginAttempt.Fail();
        }

        return LoginAttempt.Success(user.UserName, user.Role);
    }
}

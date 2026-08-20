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
            return LoginAttempt.Fail(LoginFailureReason.EmptyCredentials);
        }

        var user = _db.Users.SingleOrDefault(row =>
            row.IsActive && row.UserName == userName);
        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
        {
            return LoginAttempt.Fail(LoginFailureReason.InvalidCredentials);
        }

        return LoginAttempt.Success(user.UserName, user.Role);
    }

    public bool HasAnyUser() => _db.Users.Any();

    public void CreateUser(string userName, string password, UserRole role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (_db.Users.Any(row => row.UserName == userName))
        {
            throw new InvalidOperationException("이미 있는 아이디입니다.");
        }

        _db.Users.Add(new UserAccount
        {
            UserName = userName.Trim(),
            PasswordHash = PasswordHasher.Hash(password),
            Role = role,
            IsActive = true
        });
        _db.SaveChanges();
    }
}

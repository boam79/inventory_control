using Inventory.Core;

namespace Inventory.Infrastructure;

public sealed class AuthenticationService
{
    public const string DefaultLocalUserName = "local";

    private readonly InventoryDbContext _db;

    public AuthenticationService(InventoryDbContext db)
    {
        _db = db;
    }

    public bool HasAnyUser() => _db.Users.Any();

    /// <summary>
    /// Picks an existing active administrator, otherwise ensures a fixed local admin exists.
    /// Used as the audit/document actor when login is not required.
    /// </summary>
    public (string UserName, UserRole Role) EnsureLocalOperator()
    {
        var admin = _db.Users
            .Where(row => row.IsActive && row.Role == UserRole.Administrator)
            .OrderBy(row => row.Id)
            .FirstOrDefault();
        if (admin is not null)
        {
            return (admin.UserName, admin.Role);
        }

        var local = _db.Users.SingleOrDefault(row => row.UserName == DefaultLocalUserName);
        if (local is null)
        {
            CreateUser(DefaultLocalUserName, "local-unused", UserRole.Administrator);
            return (DefaultLocalUserName, UserRole.Administrator);
        }

        if (!local.IsActive || local.Role != UserRole.Administrator)
        {
            local.IsActive = true;
            local.Role = UserRole.Administrator;
            _db.SaveChanges();
        }

        return (local.UserName, local.Role);
    }

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

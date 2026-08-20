namespace Inventory.Core;

public sealed class LoginAttempt
{
    public bool Succeeded { get; init; }
    public string? UserName { get; init; }
    public UserRole? Role { get; init; }
    public PermissionFlags? Permissions { get; init; }

    public static LoginAttempt Fail() => new() { Succeeded = false };

    public static LoginAttempt Success(string userName, UserRole role) => new()
    {
        Succeeded = true,
        UserName = userName,
        Role = role,
        Permissions = RolePermissions.For(role)
    };
}

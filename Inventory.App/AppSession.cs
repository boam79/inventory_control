using Inventory.Core;

namespace Inventory.App;

public sealed record AppSession(
    string UserName,
    UserRole Role,
    PermissionFlags Permissions,
    string DatabasePath)
{
    public static AppSession? Current { get; set; }
}

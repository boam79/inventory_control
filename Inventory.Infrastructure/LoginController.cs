using Inventory.Core;

namespace Inventory.Infrastructure;

public sealed class LoginController
{
    private readonly AuthenticationService _auth;

    public LoginController(InventoryDbContext db)
    {
        _auth = new AuthenticationService(db);
        NeedsFirstAdmin = !_auth.HasAnyUser();
    }

    public bool NeedsFirstAdmin { get; private set; }
    public bool OpenMainShell { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;
    public string? SignedInUser { get; private set; }
    public UserRole? SignedInRole { get; private set; }
    public PermissionFlags? Permissions { get; private set; }

    public void Login(string userName, string password)
    {
        OpenMainShell = false;
        var result = _auth.SignIn(userName, password);
        if (!result.Succeeded)
        {
            ErrorMessage = LoginMessages.For(result.FailureReason);
            SignedInUser = null;
            SignedInRole = null;
            Permissions = null;
            return;
        }

        ErrorMessage = string.Empty;
        OpenMainShell = true;
        SignedInUser = result.UserName;
        SignedInRole = result.Role;
        Permissions = result.Permissions;
    }

    public void CreateFirstAdministrator(string userName, string password)
    {
        OpenMainShell = false;
        if (!NeedsFirstAdmin)
        {
            ErrorMessage = "원인: 이미 사용자가 있습니다.\n조치: 기존 계정으로 로그인하세요.";
            return;
        }

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            ErrorMessage = LoginMessages.For(LoginFailureReason.EmptyCredentials);
            return;
        }

        _auth.CreateUser(userName, password, UserRole.Administrator);
        NeedsFirstAdmin = false;
        Login(userName, password);
    }
}

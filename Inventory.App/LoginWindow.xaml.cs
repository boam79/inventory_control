using Inventory.Core;
using Inventory.Infrastructure;
using System.Windows;
using System.Windows.Input;

namespace Inventory.App;

public partial class LoginWindow : Window
{
    private readonly InventoryDbContext _db;
    private readonly LoginController _login;

    public LoginWindow()
    {
        InitializeComponent();
        var dbPath = SqliteConnectionString.DefaultDatabasePath();
        InventoryDatabase.Initialize(dbPath);
        var integrity = IntegrityCheck.Run(dbPath);
        _db = InventoryDatabase.CreateContext(dbPath);
        _login = new LoginController(_db);
        FirstAdminHint.Visibility = _login.NeedsFirstAdmin ? Visibility.Visible : Visibility.Collapsed;
        CreateAdminButton.Visibility = _login.NeedsFirstAdmin ? Visibility.Visible : Visibility.Collapsed;
        UserNameBox.Focus();
        if (integrity != "정상")
        {
            ErrorText.Text = integrity;
        }
    }

    private void Login_Click(object sender, RoutedEventArgs e) => TryLogin();

    private void CreateAdmin_Click(object sender, RoutedEventArgs e)
    {
        _login.CreateFirstAdministrator(UserNameBox.Text, PasswordBox.Password);
        ApplyResult();
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryLogin();
        }
    }

    private void TryLogin()
    {
        _login.Login(UserNameBox.Text, PasswordBox.Password);
        ApplyResult();
    }

    private void ApplyResult()
    {
        ErrorText.Text = _login.ErrorMessage;
        if (!_login.OpenMainShell || _login.SignedInUser is null || _login.SignedInRole is null)
        {
            return;
        }

        AppSession.Current = new AppSession(
            _login.SignedInUser,
            _login.SignedInRole.Value,
            _login.Permissions ?? RolePermissions.For(_login.SignedInRole.Value),
            SqliteConnectionString.DefaultDatabasePath());
        var shell = new MainWindow();
        shell.Show();
        Close();
    }
}

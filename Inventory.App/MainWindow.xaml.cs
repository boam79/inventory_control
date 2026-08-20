using Inventory.Core;
using Inventory.Infrastructure;
using System.Windows;
using System.Windows.Controls;

namespace Inventory.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var session = AppSession.Current;
        UserLabel.Text = session is null
            ? "사용자 없음"
            : $"사용자: {session.UserName} ({session.Role})";
        PeriodLabel.Text = $"기준: {DateTime.Today:yyyy년 M월}";
        VersionLabel.Text = $"버전 {ProductInfo.Name}";
        Loaded += MainWindow_Loaded;
        MenuList.SelectedIndex = 0;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var integrity = IntegrityCheck.Run(AppHost.DatabasePath);
        if (integrity != "정상")
        {
            MessageBox.Show(integrity + "\n핵심 화면은 계속 열립니다.", ProductInfo.DisplayName);
        }

        using (var db = AppHost.OpenDb())
        {
            var folder = new SettingsStore(db).Get(
                SettingsStore.BackupFolder,
                    System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SpringClinicInventory",
                    "backups"));
            BackupService.RunDailyBackupIfNeeded(AppHost.DatabasePath, folder, DateTime.Today);
        }

        var update = await UpdateChecker.CheckAsync();
        PeriodLabel.Text = $"기준: {DateTime.Today:yyyy년 M월}  · {update.Message}";
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        AppSession.Current = null;
        var login = new LoginWindow();
        login.Show();
        Close();
    }

    private void MenuList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MenuList.SelectedItem is not ListBoxItem item)
        {
            return;
        }

        var tag = item.Tag as string ?? string.Empty;
        MainContent.Content = tag switch
        {
            "dashboard" => new Views.DashboardView(),
            "receive" => new Views.ReceiveView(),
            "issue" => new Views.IssueView(),
            "stock" => new Views.StockView(),
            "ledger" => new Views.LedgerView(),
            "lots" => new Views.LotsView(),
            "reorder" => new Views.ReorderView(),
            "stats" => new Views.StatsView(),
            "close" => new Views.CloseView(),
            "masters" => new Views.MastersView(),
            "users" => new Views.UsersView(),
            "backup" => new Views.BackupView(),
            "settings" => new Views.SettingsView(),
            _ => new TextBlock { Text = item.Content?.ToString() }
        };
    }
}

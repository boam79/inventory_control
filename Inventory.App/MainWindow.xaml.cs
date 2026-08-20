using Inventory.Core;
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
        MenuList.SelectedIndex = 0;
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

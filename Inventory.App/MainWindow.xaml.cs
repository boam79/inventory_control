using Inventory.Core;
using Inventory.Infrastructure;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Inventory.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        MinWidth = UiLayout.MinWidth;
        MinHeight = UiLayout.MinHeight;
        Width = UiLayout.DesignWidth;
        Height = UiLayout.DesignHeight;
        MenuList.Width = UiLayout.MenuWidth;
        var session = AppSession.Current;
        UserLabel.Text = session is null
            ? "사용자 없음"
            : $"{session.UserName} ({session.Role})";
        PeriodLabel.Text = $"기준연월 {DateTime.Today:yyyy년 M월}";
        VersionLabel.Text = $"버전 {ProductInfo.Version}";
        AlertLabel.Text = "알림 없음";
        BuildMenu();
        Loaded += MainWindow_Loaded;
        MenuList.SelectedIndex = 0;
    }

    private void BuildMenu()
    {
        (string Tag, string Title)[] items =
        [
            ("dashboard", "대시보드"),
            ("receive", "입고 등록"),
            ("issue", "사용 등록"),
            ("stock", "재고현황"),
            ("ledger", "거래내역"),
            ("lots", "LOT·유효기간"),
            ("reorder", "발주 필요 품목"),
            ("stats", "통계·보고서"),
            ("close", "월 마감"),
            ("masters", "기준정보"),
            ("users", "사용자·권한"),
            ("backup", "백업·복원"),
            ("settings", "환경설정")
        ];
        foreach (var item in items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(MenuIcon(item.Tag));
            row.Children.Add(new TextBlock
            {
                Text = item.Title,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13
            });
            MenuList.Items.Add(new ListBoxItem { Tag = item.Tag, Content = row });
        }
    }

    private static System.Windows.Shapes.Path MenuIcon(string tag)
    {
        var data = tag switch
        {
            "dashboard" => "M2,2 H7 V7 H2 Z M9,2 H14 V7 H9 Z M2,9 H7 V14 H2 Z M9,9 H14 V14 H9 Z",
            "receive" => "M3,2 H13 V9 H10 L8,13 L6,9 H3 Z",
            "issue" => "M6,3 L10,3 L10,8 L13,8 L8,14 L3,8 L6,8 Z",
            "stock" => "M2,4 L8,2 L14,4 L14,12 L8,14 L2,12 Z",
            "ledger" => "M3,2 H13 V14 H3 Z M5,5 H11 M5,8 H11 M5,11 H9",
            "lots" => "M2,5 H14 V13 H2 Z M5,5 V3 H11 V5",
            "reorder" => "M3,12 L8,3 L13,12 Z",
            "stats" => "M3,12 V8 H5 V12 Z M7,12 V4 H9 V12 Z M11,12 V6 H13 V12 Z",
            "close" => "M3,3 H13 V13 H3 Z M3,6 H13",
            "masters" => "M4,3 H12 V5 H4 Z M4,7 H12 V9 H4 Z M4,11 H10 V13 H4 Z",
            "users" => "M8,2 A3,3 0 1 1 7.99,2 Z M3,14 Q8,9 13,14",
            "backup" => "M4,3 H12 V8 H9 L8,11 L7,8 H4 Z",
            "settings" => "M6,2 H10 V6 H14 V10 H10 V14 H6 V10 H2 V6 H6 Z",
            _ => "M2,2 H14 V14 H2 Z"
        };
        return new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Fill = new SolidColorBrush(Color.FromRgb(70, 90, 108)),
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
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
                    global::System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SpringClinicInventory",
                    "backups"));
            BackupService.RunDailyBackupIfNeeded(AppHost.DatabasePath, folder, DateTime.Today);
        }

        var update = await UpdateChecker.CheckAsync();
        SetAlert(update.Message, isWarning: !update.Checked);
        try
        {
            var stage = global::System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpringClinicInventory",
                "updates");
            var marker = global::System.IO.Path.Combine(stage, "current.marker");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var staged = await UpdateChecker.TryStageLatestIfPresentAsync(
                client,
                AppHost.DatabasePath,
                stage,
                marker);
            if (staged is { Applied: true })
            {
                SetAlert(AlertLabel.Text + " 검증된 업데이트가 대기 폴더에 있습니다.", isWarning: false);
            }

            var velo = await VelopackUpdater.CheckAndDownloadAsync();
            var first = velo.Split('\n')[0];
            var warn = first.Contains("실패", StringComparison.Ordinal) || first.Contains("못", StringComparison.Ordinal);
            SetAlert(first, warn);
        }
        catch
        {
            SetAlert("업데이트를 확인하지 못했습니다. 재고 업무를 계속하세요.", isWarning: true);
        }
    }

    private void SetAlert(string message, bool isWarning)
    {
        AlertLabel.Text = string.IsNullOrWhiteSpace(message) ? "알림 없음" : message;
        if (isWarning)
        {
            AlertChip.Background = new SolidColorBrush(Color.FromRgb(92, 68, 32));
            AlertLabel.Foreground = new SolidColorBrush(Color.FromRgb(255, 214, 140));
        }
        else
        {
            AlertChip.Background = (Brush)FindResource("ClinicHeaderChipBrush");
            AlertLabel.Foreground = Brushes.White;
        }
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
            _ => new TextBlock { Text = tag }
        };
    }
}

using Inventory.Core;
using Inventory.Infrastructure;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Inventory.App;

public partial class MainWindow : Window
{
    private string? _seedNote;
    private string _selectedMenu = "dashboard";

    public MainWindow()
    {
        InitializeComponent();
        MinWidth = UiLayout.MinWidth;
        MinHeight = UiLayout.MinHeight;
        Width = UiLayout.DesignWidth;
        Height = UiLayout.DesignHeight;
        var session = AppSession.Current;
        UserLabel.Text = session is null
            ? "사용자 없음"
            : $"{session.UserName} ({session.Role})";
        PeriodLabel.Text = $"기준연월 {DateTime.Today:yyyy년 M월}";
        VersionLabel.Text = $"버전 {ProductInfo.Version}";
        AlertLabel.Text = "";
        PageTitle.Text = ShellPages.Title(_selectedMenu);
        PageHint.Text = "로컬 · 오프라인";
        BuildNav();
        Loaded += MainWindow_Loaded;
    }

    private void BuildNav()
    {
        var flags = AppSession.Current?.Permissions ?? RolePermissions.For(UserRole.Viewer);
        if (!ShellPages.CanSee(_selectedMenu, flags))
        {
            _selectedMenu = "dashboard";
        }

        NavPanel.Children.Clear();
        foreach (var tag in ShellPages.NavOrder.Where(t => ShellPages.CanSee(t, flags)))
        {
            var capture = tag;
            var button = new Button
            {
                Content = ShellPages.NavLabel(capture),
                Tag = capture,
                Style = (Style)FindResource("NavButton")
            };
            button.Click += (_, _) => OpenMenu(capture);
            NavPanel.Children.Add(button);
        }

        HighlightNav();
    }

    private void HighlightNav()
    {
        foreach (var child in NavPanel.Children)
        {
            if (child is Button button)
            {
                var active = button.Tag as string == _selectedMenu;
                button.Style = (Style)FindResource(active ? "NavButtonActive" : "NavButton");
            }
        }
    }

    public void OpenMenu(string tag, bool force = false)
    {
        var same = !force && tag == _selectedMenu && MainContent.Content is not TextBlock and not null;
        _selectedMenu = tag;
        HighlightNav();
        if (!same)
        {
            ShowPage(tag);
        }
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

        TryAutoSeedIfEmpty();
        OpenMenu("dashboard");

        try
        {
            var update = await VelopackUpdater.CheckStatusAsync();
            var first = update.Split('\n')[0];
            var warn = first.Contains("실패", StringComparison.Ordinal)
                       || first.Contains("못", StringComparison.Ordinal)
                       || first.Contains("원인", StringComparison.Ordinal);
            SetAlert(first, warn);
        }
        catch
        {
            SetAlert("업데이트를 확인하지 못했습니다. 재고 업무를 계속하세요.", isWarning: true);
        }

        if (!string.IsNullOrWhiteSpace(_seedNote))
        {
            SetAlert(_seedNote, isWarning: false);
        }
    }

    private void TryAutoSeedIfEmpty()
    {
        if (AppSession.Current?.Role != UserRole.Administrator)
        {
            return;
        }

        using var db = AppHost.OpenDb();
        if (!DemoSeedService.ShouldAutoSeed(db))
        {
            return;
        }

        SetAlert("거래가 없어 테스트 데이터(품목 약 1만)를 넣는 중입니다...", isWarning: false);
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var result = DemoSeedService.TryAutoSeed(db, DateTime.Today, AppHost.Actor);
            _seedNote = result.Message;
            SetAlert(result.Message, isWarning: !result.Applied);
        }
        catch (Exception ex)
        {
            SetAlert($"테스트 데이터를 만들지 못했습니다: {AppLog.Sanitize(ex.Message)}", isWarning: true);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void SetAlert(string message, bool isWarning)
    {
        AlertLabel.Text = string.IsNullOrWhiteSpace(message) ? "" : message;
        AlertLabel.Foreground = isWarning
            ? new SolidColorBrush(Color.FromRgb(196, 123, 22))
            : new SolidColorBrush(Color.FromRgb(31, 111, 115));
    }

    public void ShowDashboard() => OpenMenu("dashboard", force: true);

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        SetAlert("업데이트를 준비하는 중...", isWarning: false);
        try
        {
            var progress = new Progress<string>(text =>
            {
                var first = (text ?? "").Split('\n')[0];
                SetAlert(string.IsNullOrWhiteSpace(first) ? "업데이트 중..." : first, isWarning: false);
            });
            var result = await VelopackUpdater.ApplyFromButtonAsync(progress, applyAndRestart: true);
            var warn = result.Contains("원인", StringComparison.Ordinal)
                       || result.Contains("설치본이 아닙니다", StringComparison.Ordinal);
            SetAlert(result.Split('\n')[0], warn);
            if (result.Contains("설치본이 아닙니다", StringComparison.Ordinal))
            {
                MessageBox.Show(result, ProductInfo.DisplayName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            SetAlert($"원인: {AppLog.Sanitize(ex.Message)} 입고·사용은 계속하세요.", isWarning: true);
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        AppSession.Current = null;
        var login = new LoginWindow();
        login.Show();
        Close();
    }

    private void ShowPage(string tag)
    {
        PageTitle.Text = ShellPages.Title(tag);
        PageHint.Text = string.IsNullOrWhiteSpace(ShellPages.Hint(tag)) ? "로컬 · 오프라인" : ShellPages.Hint(tag);
        MainContent.Content = new TextBlock
        {
            Text = "불러오는 중...",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(107, 122, 134)),
            Margin = new Thickness(4, 8, 0, 0)
        };
        var captured = tag;
        Dispatcher.BeginInvoke(() =>
        {
            if (_selectedMenu != captured)
            {
                return;
            }

            MainContent.Content = captured switch
            {
                "dashboard" => new Views.DashboardView(),
                "receive" => new Views.ReceiveView(),
                "issue" => new Views.IssueView(),
                "stock" => new Views.StockView(),
                "stats" => new Views.StatsView(),
                "users" => new Views.UsersView(),
                "backup" => new Views.BackupView(),
                "settings" => new Views.SettingsView(),
                _ => new TextBlock { Text = captured }
            };
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
}

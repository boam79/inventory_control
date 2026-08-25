using Inventory.Core;
using Inventory.Infrastructure;
using Inventory.App.Views;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Inventory.App;

public partial class MainWindow : Window
{
    private string? _seedNote;
    private string _selectedMenu = "dashboard";
    private bool _startupUpdatePromptShown;

    public MainWindow()
    {
        InitializeComponent();
        MinWidth = UiLayout.MinWidth;
        MinHeight = UiLayout.MinHeight;
        Width = UiLayout.DesignWidth;
        Height = UiLayout.DesignHeight;
        ApplyFontScaleFromSettings();
        var session = AppSession.Current;
        UserLabel.Text = session is null
            ? "사용자 없음"
            : $"{session.UserName} ({session.Role})";
        PeriodLabel.Text = $"기준연월 {DateTime.Today:yyyy년 M월}";
        StatusBarLeft.Text = $"버전 {ProductInfo.Version} · 오프라인 · 로컬 DB";
        StatusBarRight.Text = "마지막 백업: 확인 중…";
        AlertLabel.Text = "";
        PageTitle.Text = ShellPages.Title(_selectedMenu);
        PageHint.Text = "로컬 · 오프라인";
        BuildNav();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Loaded += MainWindow_Loaded;
    }

    public static void ApplyFontScaleFromSettings()
    {
        using var db = AppHost.OpenDb();
        var raw = new SettingsStore(db).Get(SettingsStore.FontScale, "normal");
        var scale = raw switch
        {
            "large" => 1.15,
            "xlarge" => 1.3,
            _ => 1.0
        };
        if (Application.Current is not null)
        {
            Application.Current.Resources["AppFontScale"] = scale;
        }

        if (Application.Current?.MainWindow is MainWindow main)
        {
            main.RootPanel.LayoutTransform = new ScaleTransform(scale, scale);
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.D1 || e.Key == Key.NumPad1)
            {
                OpenReceiveIssue(VoucherMode.Receive);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.D2 || e.Key == Key.NumPad2)
            {
                OpenReceiveIssue(VoucherMode.Issue);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.F5)
        {
            OpenMenu(_selectedMenu, force: true);
            e.Handled = true;
        }
    }

    private void BuildNav()
    {
        var flags = AppSession.Current?.Permissions ?? RolePermissions.For(UserRole.Viewer);
        if (!ShellPages.CanSee(_selectedMenu, flags))
        {
            _selectedMenu = ShellPages.NavOrder.FirstOrDefault(t => ShellPages.CanSee(t, flags)) ?? "dashboard";
        }

        NavPanel.Children.Clear();
        var secondary = Application.Current?.TryFindResource("ClinicTextSecondaryBrush") as Brush ?? Brushes.Gray;
        foreach (var (groupLabel, _) in ShellPages.NavGroups)
        {
            var tags = ShellPages.OrderedTagsInGroup(groupLabel, flags).ToList();
            if (tags.Count == 0)
            {
                continue;
            }

            NavPanel.Children.Add(new TextBlock
            {
                Text = groupLabel,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = secondary,
                Margin = new Thickness(14, 10, 14, 4)
            });
            foreach (var tag in tags)
            {
                var capture = tag;
                var button = new Button
                {
                    Content = ShellPages.NavLabel(capture),
                    Tag = capture,
                    Style = (Style)FindResource("SidebarNavButton")
                };
                button.Click += (_, _) => OpenMenu(capture);
                NavPanel.Children.Add(button);
            }
        }

        HighlightNav();
    }

    private void HighlightNav()
    {
        foreach (var child in NavPanel.Children.OfType<Button>())
        {
            var active = child.Tag as string == _selectedMenu;
            child.Style = (Style)FindResource(active ? "SidebarNavButtonActive" : "SidebarNavButton");
        }
    }

    public void OpenMenu(string tag, bool force = false)
    {
        tag = ShellPages.NormalizeTag(tag);
        var same = !force && tag == _selectedMenu && MainContent.Content is not TextBlock and not null;
        _selectedMenu = tag;
        HighlightNav();
        if (!same)
        {
            ShowPage(tag);
        }
    }

    public void OpenReceiveIssue(VoucherMode mode, string? itemCode = null, string? itemName = null, bool expandForm = true)
    {
        ReceiveIssueView.PendingLaunch = new ReceiveIssueView.LaunchContext(mode, itemCode, itemName, expandForm);
        OpenMenu("receive_issue", force: true);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateLastBackupLabel();
        var integrity = IntegrityCheck.Run(AppHost.DatabasePath);
        if (integrity != "정상")
        {
            MessageBox.Show(integrity + "\n핵심 화면은 계속 열립니다.", ProductInfo.DisplayName);
        }

        using (var db = AppHost.OpenDb())
        {
            var store = new SettingsStore(db);
            var folder = store.Get(
                SettingsStore.BackupFolder,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SpringClinicInventory",
                    "backups"));
            BackupService.RunDailyBackupIfNeeded(AppHost.DatabasePath, folder, DateTime.Today);
            store.Set(SettingsStore.LastBackupDate, DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        UpdateLastBackupLabel();
        TryAutoSeedIfEmpty();
        OpenMenu("dashboard");
        TrySendUsageHeartbeat();

        try
        {
            var startup = await VelopackUpdater.CheckStartupAsync();
            if (startup.Policy == StartupUpdatePolicy.ForceUpdate && startup.ForcePrompt is not null)
            {
                await RunForcedStartupUpdateAsync(startup.ForcePrompt);
            }
            else
            {
                if (startup.Policy == StartupUpdatePolicy.ContinueOfflineOrFailed)
                {
                    App.Log?.Warning("시작 업데이트 확인 실패(업무 계속): {Message}", AppLog.Sanitize(startup.StatusMessage));
                }

                var first = startup.StatusMessage.Split('\n')[0];
                var warn = first.Contains("실패", StringComparison.Ordinal)
                           || first.Contains("못", StringComparison.Ordinal)
                           || first.Contains("원인", StringComparison.Ordinal);
                SetAlert(first, warn);
            }
        }
        catch (Exception ex)
        {
            App.Log?.Warning(ex, "시작 업데이트 확인 예외(업무 계속): {Message}", AppLog.Sanitize(ex.Message));
        }

        if (!string.IsNullOrWhiteSpace(_seedNote))
        {
            SetAlert(_seedNote, isWarning: false);
        }
    }

    /// <summary>사용(설치) 하트비트 메일 — 앱 시작마다, 실패해도 업무 차단 없음.</summary>
    private static void TrySendUsageHeartbeat()
    {
        try
        {
            var root = UsageNotifyConfigStore.DefaultAppDataRoot();
            string? clinic = null;
            using (var db = AppHost.OpenDb())
            {
                var fromSettings = new SettingsStore(db).Get(SettingsStore.ClinicName, "");
                clinic = string.IsNullOrWhiteSpace(fromSettings)
                    ? ProductInfo.DisplayName
                    : fromSettings.Trim();
            }

            UsageHeartbeatService.TrySendTodayInBackground(root, clinic, App.Log);
        }
        catch
        {
            // 시작 경로에서 절대 막지 않음
        }
    }

    private void UpdateLastBackupLabel()
    {
        try
        {
            using var db = AppHost.OpenDb();
            var store = new SettingsStore(db);
            var saved = store.Get(SettingsStore.LastBackupDate, "");
            if (!string.IsNullOrWhiteSpace(saved))
            {
                StatusBarRight.Text = $"마지막 백업: {saved}";
                return;
            }

            var folder = store.Get(
                SettingsStore.BackupFolder,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SpringClinicInventory",
                    "backups"));
            if (Directory.Exists(folder))
            {
                var latest = Directory.GetFiles(folder, "*.db")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();
                StatusBarRight.Text = latest is null
                    ? "마지막 백업: 없음"
                    : $"마지막 백업: {latest.LastWriteTime:yyyy-MM-dd}";
                return;
            }
        }
        catch
        {
            // ignore status bar errors
        }

        StatusBarRight.Text = "마지막 백업: —";
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

        SetAlert("거래가 없어 테스트 데이터(품목 1,000)를 넣는 중입니다...", isWarning: false);
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
            ? Application.Current?.TryFindResource("ClinicWarnBrush") as Brush
              ?? new SolidColorBrush(Color.FromRgb(196, 123, 22))
            : Application.Current?.TryFindResource("ClinicAccentBrush") as Brush
              ?? new SolidColorBrush(Color.FromRgb(31, 111, 115));
    }

    public void ShowDashboard() => OpenMenu("dashboard", force: true);

    private async Task RunForcedStartupUpdateAsync(UpdatePromptInfo prompt)
    {
        if (_startupUpdatePromptShown)
        {
            return;
        }

        _startupUpdatePromptShown = true;
        IsEnabled = false;
        var dialog = new ForceUpdateProgressDialog(this, prompt.Message);
        dialog.Show();
        try
        {
            var progress = new Progress<string>(text =>
            {
                dialog.Dispatcher.Invoke(() => dialog.SetProgress(text));
                var first = (text ?? "").Split('\n')[0];
                SetAlert(string.IsNullOrWhiteSpace(first) ? "업데이트 중..." : first, isWarning: false);
            });
            var result = await VelopackUpdater.ApplyFromButtonAsync(progress, applyAndRestart: true);
            // ApplyUpdatesAndRestart normally exits; if we return, apply failed — allow clinic work.
            dialog.AllowClose();
            dialog.Close();
            App.Log?.Warning("강제 업데이트 적용 실패(업무 계속): {Message}", AppLog.Sanitize(result));
            var warn = result.Contains("원인", StringComparison.Ordinal)
                       || result.Contains("설치본이 아닙니다", StringComparison.Ordinal);
            SetAlert(result.Split('\n')[0], warn);
        }
        catch (Exception ex)
        {
            dialog.AllowClose();
            dialog.Close();
            App.Log?.Warning(ex, "강제 업데이트 예외(업무 계속): {Message}", AppLog.Sanitize(ex.Message));
            SetAlert($"원인: {AppLog.Sanitize(ex.Message)} 입고·출고는 계속하세요.", isWarning: true);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e) => await ApplyUpdateAsync();

    private async Task ApplyUpdateAsync()
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
            SetAlert($"원인: {AppLog.Sanitize(ex.Message)} 입고·출고는 계속하세요.", isWarning: true);
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }

    private void ShowPage(string tag)
    {
        PageTitle.Text = ShellPages.Title(tag);
        PageHint.Text = string.IsNullOrWhiteSpace(ShellPages.Hint(tag)) ? "로컬 · 오프라인" : ShellPages.Hint(tag);
        var loading = UiComponents.LoadingHost(out var bar, out var label);
        MainContent.Content = loading;
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
                "receive_issue" => new Views.ReceiveIssueView(),
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

using Inventory.Core;
using Inventory.Infrastructure;
using Serilog;
using System.Windows;
using System.Windows.Threading;

namespace Inventory.App;

public partial class App : Application
{
    private ILogger? _log;

    /// <summary>파일 로그(Serilog). 사용 알림 등 백그라운드 작업용.</summary>
    internal static ILogger? Log { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        var logDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpringClinicInventory",
            "logs");
        _log = AppLog.CreateFileLogger(logDir);
        Log = _log;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                _log?.Error(ex, AppLog.Sanitize(ex.Message));
            }
        };
        BootstrapLocalSession();
        base.OnStartup(e);
    }

    private static void BootstrapLocalSession()
    {
        var dbPath = SqliteConnectionString.DefaultDatabasePath();
        InventoryDatabase.Initialize(dbPath);
        using var db = InventoryDatabase.CreateContext(dbPath);
        var (userName, role) = new AuthenticationService(db).EnsureLocalOperator();
        AppSession.Current = new AppSession(
            userName,
            role,
            RolePermissions.For(role),
            dbPath);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error(e.Exception, AppLog.Sanitize(e.Exception.Message));
        MessageBox.Show(
            "원인: 프로그램 오류가 발생했습니다.\n조치: 작업을 저장했는지 확인한 뒤 계속 사용하세요. 반복되면 백업 후 관리자에게 알리세요.",
            ProductInfo.DisplayName);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_log is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnExit(e);
    }
}

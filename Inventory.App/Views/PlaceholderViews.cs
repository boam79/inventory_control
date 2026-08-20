using System.Windows.Controls;

namespace Inventory.App.Views;

public class PlaceholderView : UserControl
{
    public PlaceholderView(string title)
    {
        Content = new TextBlock
        {
            Text = title,
            FontSize = 20
        };
    }
}

public class DashboardView : PlaceholderView { public DashboardView() : base("대시보드") { } }
public class ReceiveView : PlaceholderView { public ReceiveView() : base("입고 등록") { } }
public class IssueView : PlaceholderView { public IssueView() : base("사용 등록") { } }
public class StockView : PlaceholderView { public StockView() : base("재고현황") { } }
public class LedgerView : PlaceholderView { public LedgerView() : base("거래내역") { } }
public class LotsView : PlaceholderView { public LotsView() : base("LOT·유효기간") { } }
public class ReorderView : PlaceholderView { public ReorderView() : base("발주 필요 품목") { } }
public class StatsView : PlaceholderView { public StatsView() : base("통계·보고서") { } }
public class CloseView : PlaceholderView { public CloseView() : base("월 마감") { } }
public class MastersView : PlaceholderView { public MastersView() : base("기준정보") { } }
public class UsersView : PlaceholderView { public UsersView() : base("사용자·권한") { } }
public class BackupView : PlaceholderView { public BackupView() : base("백업·복원") { } }
public class SettingsView : PlaceholderView { public SettingsView() : base("환경설정") { } }

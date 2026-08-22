using Inventory.Core;

namespace Inventory.Tests;

public class ShellLayoutTests
{
    [Fact]
    public void Every_menu_tag_has_a_korean_page_title()
    {
        Assert.NotEmpty(ShellPages.MenuTags);
        foreach (var tag in ShellPages.MenuTags)
        {
            var title = ShellPages.Title(tag);
            Assert.False(string.IsNullOrWhiteSpace(title));
            Assert.DoesNotContain("PeriodLabel", title, StringComparison.Ordinal);
            Assert.DoesNotContain("OpeningStatus", title, StringComparison.Ordinal);
        }

        Assert.Equal("대시보드", ShellPages.Title("dashboard"));
        Assert.Equal("입고", ShellPages.Title("receive"));
        Assert.Equal("재고현황", ShellPages.Title("stock"));
        Assert.Equal("입고", ShellPages.NavLabel("receive"));
        Assert.Equal("재고", ShellPages.NavLabel("stock"));
        Assert.Equal("대시보드", ShellPages.NavLabel("dashboard"));
        Assert.DoesNotContain("lots", ShellPages.MenuTags);
        Assert.DoesNotContain("close", ShellPages.MenuTags);
        Assert.DoesNotContain("masters", ShellPages.MenuTags);
        Assert.DoesNotContain("lots", ShellPages.NavOrder);
        Assert.DoesNotContain("close", ShellPages.NavOrder);
        Assert.DoesNotContain("masters", ShellPages.NavOrder);
    }

    [Fact]
    public void Lists_keep_a_readable_table_height()
    {
        Assert.Equal(50, UiLayout.PageSize);
        Assert.Equal(8, UiLayout.ChartItemMax);
        Assert.True(UiLayout.ListMaxHeight >= 300);
        Assert.True(UiLayout.ListMaxHeight <= 480);
    }

    [Fact]
    public void Main_window_uses_top_nav_and_page_title()
    {
        var xaml = ReadRepoFile("Inventory.App", "MainWindow.xaml");
        Assert.Contains("x:Name=\"PageTitle\"", xaml);
        Assert.Contains("x:Name=\"NavPanel\"", xaml);
        Assert.Contains("스프링의원", xaml, StringComparison.Ordinal);
        Assert.Contains("재고관리", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShortcutHint\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StatusBarLeft\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"MenuList\"", xaml);
        var titleAt = xaml.IndexOf("x:Name=\"PageTitle\"", StringComparison.Ordinal);
        var scrollAt = xaml.IndexOf("<ScrollViewer", StringComparison.Ordinal);
        Assert.True(titleAt >= 0);
        Assert.True(scrollAt < 0 || titleAt < scrollAt);
    }

    [Fact]
    public void Nav_groups_follow_shell_pages_order()
    {
        Assert.Equal(3, ShellPages.NavGroups.Count);
        Assert.Equal("업무", ShellPages.NavGroups[0].GroupLabel);
        Assert.Equal("분석", ShellPages.NavGroups[1].GroupLabel);
        Assert.Equal("관리", ShellPages.NavGroups[2].GroupLabel);
        var flags = RolePermissions.For(UserRole.Administrator);
        var work = ShellPages.OrderedTagsInGroup("업무", flags).ToList();
        Assert.Equal(["receive", "issue", "stock"], work);
    }

    [Fact]
    public void Dashboard_is_item_table_and_removed_screens_are_gone()
    {
        var workspace = ReadRepoFile("Inventory.App", "Views", "WorkspaceViews.cs");
        Assert.Contains("품목 목록", workspace, StringComparison.Ordinal);
        Assert.Contains("품목 출고 추이", workspace, StringComparison.Ordinal);
        Assert.Contains("nameof(ItemRow.선택)", workspace, StringComparison.Ordinal);
        Assert.Contains("ChartItemMax", workspace, StringComparison.Ordinal);
        Assert.Contains("예측 수량", workspace, StringComparison.Ordinal);
        Assert.Contains("겹치지 않습니다", workspace, StringComparison.Ordinal);
        Assert.Contains("ReplaceSample", workspace, StringComparison.Ordinal);
        Assert.Contains("Expander", workspace, StringComparison.Ordinal);
        Assert.Contains("개발자", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("테스트 데이터 생성 (품목 1,000", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("샘플 데이터를 다양하게 다시 만들기", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("품목은 다시 만들지 않습니다", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("그래도 거래를 추가할까요?", workspace, StringComparison.Ordinal);
        Assert.Contains("기존 품목·입고·출고를 모두 삭제한 뒤", workspace, StringComparison.Ordinal);
        Assert.Contains("LineSeries<double>", workspace, StringComparison.Ordinal);
        Assert.Contains("GeometrySize = 6", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("GeometrySize = 0", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("ColumnSeries<double>", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("Section(\"품목 등록\"", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("class CloseView", workspace, StringComparison.Ordinal);

        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        Assert.DoesNotContain("class LotsView", forms, StringComparison.Ordinal);
        Assert.DoesNotContain("class MastersView", forms, StringComparison.Ordinal);
        Assert.DoesNotContain("class LedgerView", forms, StringComparison.Ordinal);
        Assert.DoesNotContain("class ReorderView", forms, StringComparison.Ordinal);
    }

    [Fact]
    public void Reorder_and_ledger_menus_are_removed_edit_delete_replace_cancel_button()
    {
        Assert.DoesNotContain("reorder", ShellPages.MenuTags);
        Assert.DoesNotContain("ledger", ShellPages.MenuTags);
        Assert.DoesNotContain("reorder", ShellPages.NavOrder);
        Assert.DoesNotContain("ledger", ShellPages.NavOrder);

        var mainWindow = ReadRepoFile("Inventory.App", "MainWindow.xaml.cs");
        Assert.DoesNotContain("Views.LedgerView", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Views.ReorderView", mainWindow, StringComparison.Ordinal);

        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        var workspace = ReadRepoFile("Inventory.App", "Views", "WorkspaceViews.cs");
        Assert.DoesNotContain("선택 전표 취소", forms, StringComparison.Ordinal);
        Assert.Contains("Primary(\"수정\"", forms, StringComparison.Ordinal);
        Assert.Contains("Danger(\"삭제\"", forms, StringComparison.Ordinal);
        Assert.Contains("Danger(\"복원\"", workspace, StringComparison.Ordinal);
        Assert.Contains("ItemSearchBox", forms, StringComparison.Ordinal);
        Assert.Contains("Section(\"이번 전표 품목\"", forms, StringComparison.Ordinal);
        Assert.Contains("Section(\"최근 입고\"", forms, StringComparison.Ordinal);
        Assert.Contains("Section(\"최근 출고\"", forms, StringComparison.Ordinal);
        Assert.Contains("출고 저장", forms, StringComparison.Ordinal);
        Assert.Contains("GetDocumentDetail", forms, StringComparison.Ordinal);
        Assert.Contains("DeleteDocument", forms, StringComparison.Ordinal);
        Assert.Contains("BeginEdit", forms, StringComparison.Ordinal);
    }

    [Fact]
    public void Receive_and_Issue_recent_lists_support_multi_select_delete()
    {
        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        var workspace = ReadRepoFile("Inventory.App", "Views", "WorkspaceViews.cs");
        Assert.Contains("allowMultiSelect: true", forms, StringComparison.Ordinal);
        Assert.Contains("DataGridSelectionMode.Extended", workspace, StringComparison.Ordinal);
        Assert.Contains("DataGridSelectionUnit.FullRow", workspace, StringComparison.Ordinal);
        Assert.Contains("수정은 한 건만 선택할 수 있습니다.", forms, StringComparison.Ordinal);
        Assert.Contains("선택한 {toDelete.Count}건의 전표를 삭제할까요?", forms, StringComparison.Ordinal);

        var receiveStart = forms.IndexOf("public sealed class ReceiveView", StringComparison.Ordinal);
        var issueStart = forms.IndexOf("public sealed class IssueView", StringComparison.Ordinal);
        var stockStart = forms.IndexOf("public sealed class StockView", StringComparison.Ordinal);
        Assert.True(receiveStart >= 0 && issueStart > receiveStart && stockStart > issueStart);
        var receive = forms[receiveStart..issueStart];
        var issue = forms[issueStart..stockStart];
        Assert.Contains("allowMultiSelect: true", receive, StringComparison.Ordinal);
        Assert.Contains("allowMultiSelect: true", issue, StringComparison.Ordinal);
        Assert.Contains("SelectedRecentRows()", receive, StringComparison.Ordinal);
        Assert.Contains("SelectedRecentRows()", issue, StringComparison.Ordinal);
    }

    [Fact]
    public void Stock_screen_is_a_filter_and_table()
    {
        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        Assert.Contains("TableGrid", forms, StringComparison.Ordinal);
        Assert.Contains("검색·필터", forms, StringComparison.Ordinal);
        Assert.Contains("nameof(StockRow.현재고)", forms, StringComparison.Ordinal);
        Assert.Contains("nameof(StockRow.상태)", forms, StringComparison.Ordinal);
        Assert.Contains("BuildFilterChips", forms, StringComparison.Ordinal);
        Assert.Contains("Field(\"품목\", itemSearch.Input)", forms, StringComparison.Ordinal);
        Assert.Contains("Primary(\"적용\"", forms, StringComparison.Ordinal);
        Assert.Contains("Btn(\"초기화\"", forms, StringComparison.Ordinal);
        Assert.Contains("(\"exp\", \"임박\")", forms, StringComparison.Ordinal);
        Assert.DoesNotContain("(\"유효기간\", nameof(StockRow.유효기간))", forms, StringComparison.Ordinal);
    }

    [Fact]
    public void Recent_documents_show_item_name_and_expired_lots_are_flagged()
    {
        // Found via user-story QA: cancelling a receipt/issue required knowing its item,
        // but the recent-document lists only showed date/doc id/line count, not the item.
        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        Assert.Contains("품목 = d.LineCount > 1 ? $\"{d.FirstItemName} 등 {d.LineCount}건\" : d.FirstItemName ?? \"—\"", forms, StringComparison.Ordinal);
        Assert.Contains("(\"품목\", \"품목\")", forms, StringComparison.Ordinal);

        // Already-expired lots (음수 남은일) were listed with no visual flag.
        Assert.Contains("IsExpired = days is not null && days < 0", forms, StringComparison.Ordinal);
        Assert.Contains("class LotRow", forms, StringComparison.Ordinal);
    }

    [Fact]
    public void Issue_recent_list_double_click_starts_edit()
    {
        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        var issueStart = forms.IndexOf("public sealed class IssueView", StringComparison.Ordinal);
        var stockStart = forms.IndexOf("public sealed class StockView", StringComparison.Ordinal);
        Assert.True(issueStart >= 0 && stockStart > issueStart);
        var issue = forms[issueStart..stockStart];
        Assert.Contains("MouseDoubleClick", issue, StringComparison.Ordinal);
        Assert.Contains("BeginEdit", issue, StringComparison.Ordinal);
        Assert.Contains("Field(\"LOT\", lot)", issue, StringComparison.Ordinal);
        Assert.Contains("IssueCartLine", issue, StringComparison.Ordinal);
    }

    [Fact]
    public void Receive_registration_omits_lot_expiry_and_voucher_fields()
    {
        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        var receiveStart = forms.IndexOf("public sealed class ReceiveView", StringComparison.Ordinal);
        var issueStart = forms.IndexOf("public sealed class IssueView", StringComparison.Ordinal);
        Assert.True(receiveStart >= 0 && issueStart > receiveStart);
        var receive = forms[receiveStart..issueStart];
        Assert.DoesNotContain("Field(\"LOT\"", receive, StringComparison.Ordinal);
        Assert.DoesNotContain("Field(\"유효기간\"", receive, StringComparison.Ordinal);
        Assert.DoesNotContain("Field(\"증빙번호\"", receive, StringComparison.Ordinal);
        Assert.DoesNotContain("(\"증빙\", \"증빙\")", receive, StringComparison.Ordinal);
        Assert.DoesNotContain("(\"LOT\", nameof(CartLine.LOT))", receive, StringComparison.Ordinal);
        Assert.Contains("LotNumber = null", receive, StringComparison.Ordinal);
        Assert.Contains("ExpiryDate = null", receive, StringComparison.Ordinal);
    }

    [Fact]
    public void Issue_menu_is_labeled_as_dispatch_not_usage()
    {
        Assert.Equal("출고", ShellPages.Title("issue"));
        Assert.Equal("출고", ShellPages.NavLabel("issue"));
        Assert.DoesNotContain("사용 추이", ShellPages.Hint("dashboard"), StringComparison.Ordinal);
        Assert.Contains("출고 추이", ShellPages.Hint("dashboard"), StringComparison.Ordinal);
    }

    [Fact]
    public void Stats_filters_apply_immediately_and_support_period_trend()
    {
        // User report: changing 기간/집계 in 통계·보고서 looked like it "did not filter" because
        // the table only refreshed on the 적용 button click, and the table never showed which
        // period a row belonged to, so 월별/부서별 changes were invisible until 적용 was pressed.
        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        Assert.Contains("dimension.SelectionChanged += (_, _) => Reload();", forms, StringComparison.Ordinal);
        Assert.Contains("anchor.SelectedDateChanged += (_, _) => Reload();", forms, StringComparison.Ordinal);
        Assert.Contains("최근 6기간 추이로 보기", forms, StringComparison.Ordinal);
        Assert.Contains("ColumnSeries<double>", forms, StringComparison.Ordinal);
        Assert.Contains("(\"기간\", \"기간\")", forms, StringComparison.Ordinal);

        var analytics = ReadRepoFile("Inventory.Infrastructure", "ReportAnalytics.cs");
        Assert.Contains("public static DateTime StepBack(", analytics, StringComparison.Ordinal);
    }

    [Fact]
    public void App_starts_on_main_window_without_login_or_logout()
    {
        var appXaml = ReadRepoFile("Inventory.App", "App.xaml");
        Assert.Contains("StartupUri=\"MainWindow.xaml\"", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LoginWindow", appXaml, StringComparison.Ordinal);

        var mainXaml = ReadRepoFile("Inventory.App", "MainWindow.xaml");
        Assert.DoesNotContain("LogoutButton", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("로그아웃", mainXaml, StringComparison.Ordinal);

        var mainCs = ReadRepoFile("Inventory.App", "MainWindow.xaml.cs");
        Assert.DoesNotContain("Logout_Click", mainCs, StringComparison.Ordinal);
        Assert.DoesNotContain("LoginWindow", mainCs, StringComparison.Ordinal);

        var appCs = ReadRepoFile("Inventory.App", "App.xaml.cs");
        Assert.Contains("BootstrapLocalSession", appCs, StringComparison.Ordinal);
        Assert.Contains("EnsureLocalOperator", appCs, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}

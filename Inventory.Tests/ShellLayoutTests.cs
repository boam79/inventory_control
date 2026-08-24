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
        Assert.Equal("입출고", ShellPages.Title("receive_issue"));
        Assert.Equal("재고현황", ShellPages.Title("stock"));
        Assert.Equal("입출고", ShellPages.NavLabel("receive_issue"));
        Assert.Equal("재고", ShellPages.NavLabel("stock"));
        Assert.Equal("대시보드", ShellPages.NavLabel("dashboard"));
        Assert.DoesNotContain("lots", ShellPages.MenuTags);
        Assert.DoesNotContain("close", ShellPages.MenuTags);
        Assert.DoesNotContain("masters", ShellPages.MenuTags);
        Assert.DoesNotContain("receive", ShellPages.MenuTags);
        Assert.DoesNotContain("issue", ShellPages.MenuTags);
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
    public void Main_window_uses_left_sidebar_and_page_title()
    {
        var xaml = ReadRepoFile("Inventory.App", "MainWindow.xaml");
        Assert.Contains("x:Name=\"PageTitle\"", xaml);
        Assert.Contains("x:Name=\"NavPanel\"", xaml);
        Assert.Contains("스프링의원", xaml, StringComparison.Ordinal);
        Assert.Contains("재고관리", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShortcutHint\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StatusBarLeft\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ClinicAccentBrush", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"MenuList\"", xaml);
        Assert.Contains("Width=\"208\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NavPanel\"", xaml, StringComparison.Ordinal);
        var sidebarAt = xaml.IndexOf("Width=\"208\"", StringComparison.Ordinal);
        var navAt = xaml.IndexOf("x:Name=\"NavPanel\"", StringComparison.Ordinal);
        Assert.True(sidebarAt >= 0);
        Assert.True(navAt > sidebarAt);
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
        Assert.Equal(["receive_issue", "stock"], work);
    }

    [Fact]
    public void Receive_issue_tab_visible_with_receive_or_issue_permission()
    {
        var admin = ShellPages.CanSee("receive_issue", RolePermissions.For(UserRole.Administrator));
        var purchasing = ShellPages.CanSee("receive_issue", RolePermissions.For(UserRole.Purchasing));
        var dept = ShellPages.CanSee("receive_issue", RolePermissions.For(UserRole.DepartmentUser));
        var viewer = ShellPages.CanSee("receive_issue", RolePermissions.For(UserRole.Viewer));

        Assert.True(admin);
        Assert.True(purchasing);
        Assert.True(dept);
        Assert.False(viewer);
    }

    [Fact]
    public void Legacy_receive_and_issue_tags_normalize_to_receive_issue()
    {
        Assert.Equal("receive_issue", ShellPages.NormalizeTag("receive"));
        Assert.Equal("receive_issue", ShellPages.NormalizeTag("issue"));
        Assert.Equal("stock", ShellPages.NormalizeTag("stock"));
    }

    [Fact]
    public void Dashboard_is_item_table_and_removed_screens_are_gone()
    {
        var workspace = ReadRepoFile("Inventory.App", "Views", "WorkspaceViews.cs");
        Assert.Contains("품목 목록", workspace, StringComparison.Ordinal);
        Assert.Contains("품목 출고 추이", workspace, StringComparison.Ordinal);
        Assert.Contains("전체 출고 추이 · 3개월 예측", workspace, StringComparison.Ordinal);
        Assert.Contains("ShowHeroChart", workspace, StringComparison.Ordinal);
        Assert.Contains("BuildAggregateLine", workspace, StringComparison.Ordinal);
        Assert.Contains("Height = 280", workspace, StringComparison.Ordinal);
        Assert.Contains("다음달 예상 출고", workspace, StringComparison.Ordinal);
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

        var mainCs = ReadRepoFile("Inventory.App", "MainWindow.xaml.cs");
        Assert.Contains("SidebarNavButton", mainCs, StringComparison.Ordinal);
        Assert.Contains("SidebarNavButtonActive", mainCs, StringComparison.Ordinal);

        var plot = ReadRepoFile("Inventory.Infrastructure", "DashboardChartPlot.cs");
        Assert.Contains("BuildAggregateLine", plot, StringComparison.Ordinal);
        Assert.Contains("NextMonthOutlook", plot, StringComparison.Ordinal);
        Assert.Contains("FormatNextMonthBadge", plot, StringComparison.Ordinal);

        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        Assert.DoesNotContain("class LotsView", forms, StringComparison.Ordinal);
        Assert.DoesNotContain("class MastersView", forms, StringComparison.Ordinal);
        Assert.DoesNotContain("class LedgerView", forms, StringComparison.Ordinal);
        Assert.DoesNotContain("class ReorderView", forms, StringComparison.Ordinal);
        Assert.DoesNotContain("class ReceiveView", forms, StringComparison.Ordinal);
        Assert.DoesNotContain("class IssueView", forms, StringComparison.Ordinal);
        Assert.Contains("class ReceiveIssueView", forms, StringComparison.Ordinal);
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
        Assert.DoesNotContain("Views.ReceiveView", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Views.IssueView", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Views.ReceiveIssueView", mainWindow, StringComparison.Ordinal);
        Assert.Contains("OpenReceiveIssue", mainWindow, StringComparison.Ordinal);

        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        var workspace = ReadRepoFile("Inventory.App", "Views", "WorkspaceViews.cs");
        Assert.DoesNotContain("선택 전표 취소", forms, StringComparison.Ordinal);
        Assert.Contains("Primary(\"수정\"", forms, StringComparison.Ordinal);
        Assert.Contains("Danger(\"삭제\"", forms, StringComparison.Ordinal);
        Assert.Contains("Danger(\"복원\"", workspace, StringComparison.Ordinal);
        Assert.Contains("ItemSearchBox", forms, StringComparison.Ordinal);
        Assert.Contains("Section(\"새 전표\"", forms, StringComparison.Ordinal);
        Assert.Contains("Section(\"최근 전표\"", forms, StringComparison.Ordinal);
        Assert.Contains("입고 저장", forms, StringComparison.Ordinal);
        Assert.Contains("출고 저장", forms, StringComparison.Ordinal);
        Assert.Contains("GetDocumentDetail", forms, StringComparison.Ordinal);
        Assert.Contains("DeleteDocument", forms, StringComparison.Ordinal);
        Assert.Contains("BeginEdit", forms, StringComparison.Ordinal);
    }

    [Fact]
    public void Receive_issue_recent_list_supports_multi_select_delete_and_type_filter()
    {
        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        var workspace = ReadRepoFile("Inventory.App", "Views", "WorkspaceViews.cs");
        Assert.Contains("allowMultiSelect: true", forms, StringComparison.Ordinal);
        Assert.Contains("DataGridSelectionMode.Extended", workspace, StringComparison.Ordinal);
        Assert.Contains("DataGridSelectionUnit.FullRow", workspace, StringComparison.Ordinal);
        Assert.Contains("수정은 한 건만 선택할 수 있습니다.", forms, StringComparison.Ordinal);
        Assert.Contains("선택한 {toDelete.Count}건의 전표를 삭제할까요?", forms, StringComparison.Ordinal);

        var viewStart = forms.IndexOf("public sealed class ReceiveIssueView", StringComparison.Ordinal);
        var stockStart = forms.IndexOf("public sealed class StockView", StringComparison.Ordinal);
        Assert.True(viewStart >= 0 && stockStart > viewStart);
        var receiveIssue = forms[viewStart..stockStart];
        Assert.Contains("allowMultiSelect: true", receiveIssue, StringComparison.Ordinal);
        Assert.Contains("SelectedRecentRows()", receiveIssue, StringComparison.Ordinal);
        Assert.Contains("ColumnSpec(\"유형\", \"유형\"", receiveIssue, StringComparison.Ordinal);
        Assert.Contains("(\"all\", \"전체\"), (\"receive\", \"입고\"), (\"issue\", \"출고\")", receiveIssue, StringComparison.Ordinal);
        Assert.Contains("ListDocumentSummaries(80, typeFilter, voucherTypesOnly: true)", receiveIssue, StringComparison.Ordinal);
        Assert.Contains("saveButton.Content = isReceive ? \"입고 저장\" : \"출고 저장\"", receiveIssue, StringComparison.Ordinal);
        Assert.Contains("Primary(currentMode == VoucherMode.Receive ? \"입고 저장\" : \"출고 저장\"", receiveIssue, StringComparison.Ordinal);
        Assert.Contains("RadioButton", receiveIssue, StringComparison.Ordinal);
        Assert.Contains("Expander", receiveIssue, StringComparison.Ordinal);
    }

    [Fact]
    public void Stock_screen_is_a_filter_and_table_with_receive_issue_shortcuts()
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
        Assert.Contains("이 품목 입고", forms, StringComparison.Ordinal);
        Assert.Contains("이 품목 출고", forms, StringComparison.Ordinal);
        Assert.Contains("itemSearch.SelectionChanged += _ => Reload();", forms, StringComparison.Ordinal);
        Assert.Contains("LOT 상세", forms, StringComparison.Ordinal);
        Assert.DoesNotContain("(\"유효기간\", nameof(StockRow.유효기간))", forms, StringComparison.Ordinal);
        Assert.Contains("총 재고 금액", forms, StringComparison.Ordinal);
        Assert.Contains("nameof(StockRow.재고금액)", forms, StringComparison.Ordinal);
        Assert.Contains("ExportStockList", forms, StringComparison.Ordinal);
        Assert.Contains("ExcelExportScopeDialog", forms, StringComparison.Ordinal);
        Assert.Contains("allowMultiSelect: true", forms, StringComparison.Ordinal);
        Assert.Contains("품목 선택", forms, StringComparison.Ordinal);
    }

    [Fact]
    public void Stats_view_supports_supplier_monthly_purchase_report()
    {
        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        Assert.Contains("거래처별 월별 구매내역", forms, StringComparison.Ordinal);
        Assert.Contains("QuerySupplierMonthlyPurchases", forms, StringComparison.Ordinal);
        Assert.Contains("ExportSupplierMonthlyPurchases", forms, StringComparison.Ordinal);
        Assert.Contains("거래처 선택", forms, StringComparison.Ordinal);
        Assert.Contains("SupplierPurchaseRow", forms, StringComparison.Ordinal);
        Assert.Contains("nameof(SupplierPurchaseRow.월)", forms, StringComparison.Ordinal);
        Assert.Contains("nameof(SupplierPurchaseRow.거래처)", forms, StringComparison.Ordinal);
    }

    [Fact]
    public void Recent_documents_show_item_name_and_expired_lots_are_flagged()
    {
        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        Assert.Contains("유형 = DocTypeKo(d.Type)", forms, StringComparison.Ordinal);
        Assert.Contains("품목 = d.LineCount > 1 ? $\"{d.FirstItemName} 등 {d.LineCount}건\" : d.FirstItemName ?? \"—\"", forms, StringComparison.Ordinal);
        Assert.Contains("(\"품목\", \"품목\")", forms, StringComparison.Ordinal);
        Assert.Contains("IsExpired = days is not null && days < 0", forms, StringComparison.Ordinal);
        Assert.Contains("class LotRow", forms, StringComparison.Ordinal);
    }

    [Fact]
    public void Receive_issue_shows_line_and_document_totals()
    {
        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        var viewStart = forms.IndexOf("public sealed class ReceiveIssueView", StringComparison.Ordinal);
        var stockStart = forms.IndexOf("public sealed class StockView", StringComparison.Ordinal);
        Assert.True(viewStart >= 0 && stockStart > viewStart);
        var view = forms[viewStart..stockStart];
        Assert.Contains("Field(\"총금액\", lineTotal)", view, StringComparison.Ordinal);
        Assert.Contains("UpdateLineTotal()", view, StringComparison.Ordinal);
        Assert.Contains("nameof(CartLine.총금액)", view, StringComparison.Ordinal);
        Assert.Contains("nameof(CartLine.단가표시)", view, StringComparison.Ordinal);
        Assert.Contains("ColumnSpec(\"단가\", \"단가\"", view, StringComparison.Ordinal);
        Assert.Contains("ColumnSpec(\"단가\", nameof(CartLine.단가표시)", view, StringComparison.Ordinal);
        Assert.Contains("TotalAmount", view, StringComparison.Ordinal);
        Assert.Contains("UnitPrice", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Receive_issue_recent_list_double_click_starts_edit()
    {
        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        var viewStart = forms.IndexOf("public sealed class ReceiveIssueView", StringComparison.Ordinal);
        var stockStart = forms.IndexOf("public sealed class StockView", StringComparison.Ordinal);
        Assert.True(viewStart >= 0 && stockStart > viewStart);
        var view = forms[viewStart..stockStart];
        Assert.Contains("MouseDoubleClick", view, StringComparison.Ordinal);
        Assert.Contains("BeginEdit", view, StringComparison.Ordinal);
        Assert.Contains("Field(\"LOT\", lot)", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Receive_registration_omits_lot_expiry_and_voucher_fields()
    {
        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        var viewStart = forms.IndexOf("public sealed class ReceiveIssueView", StringComparison.Ordinal);
        var stockStart = forms.IndexOf("public sealed class StockView", StringComparison.Ordinal);
        Assert.True(viewStart >= 0 && stockStart > viewStart);
        var view = forms[viewStart..stockStart];
        Assert.Contains("LotNumber = null", view, StringComparison.Ordinal);
        Assert.Contains("ExpiryDate = null", view, StringComparison.Ordinal);
        Assert.Contains("Field(\"공급업체\", supplier)", view, StringComparison.Ordinal);
        Assert.Contains("Field(\"LOT\", lot)", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Issue_menu_is_labeled_as_dispatch_not_usage()
    {
        Assert.Equal("출고", ShellPages.Title("issue"));
        Assert.Equal("출고", ShellPages.NavLabel("issue"));
        Assert.Equal("입출고", ShellPages.NavLabel("receive_issue"));
        Assert.DoesNotContain("사용 추이", ShellPages.Hint("dashboard"), StringComparison.Ordinal);
        Assert.Contains("출고 추이", ShellPages.Hint("dashboard"), StringComparison.Ordinal);
    }

    [Fact]
    public void Stats_filters_apply_immediately_and_support_period_trend()
    {
        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        Assert.Contains("dimension.SelectionChanged += (_, _) => Reload();", forms, StringComparison.Ordinal);
        Assert.Contains("anchorDate.SelectedDateChanged += (_, _) => Reload();", forms, StringComparison.Ordinal);
        Assert.Contains("anchorMonth.SelectedDateChanged += (_, _) => Reload();", forms, StringComparison.Ordinal);
        Assert.Contains("최근 6기간 추이로 보기", forms, StringComparison.Ordinal);
        Assert.Contains("ColumnSeries<double>", forms, StringComparison.Ordinal);
        Assert.Contains("(\"기간\", \"기간\")", forms, StringComparison.Ordinal);

        var analytics = ReadRepoFile("Inventory.Infrastructure", "ReportAnalytics.cs");
        Assert.Contains("public static DateTime StepBack(", analytics, StringComparison.Ordinal);
    }

    [Fact]
    public void Stats_view_uses_separate_date_pickers_for_aggregate_and_supplier_reports()
    {
        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        var viewStart = forms.IndexOf("public sealed class StatsView", StringComparison.Ordinal);
        var usersStart = forms.IndexOf("public sealed class UsersView", StringComparison.Ordinal);
        Assert.True(viewStart >= 0 && usersStart > viewStart);
        var view = forms[viewStart..usersStart];
        Assert.Contains("var anchorDate = Date(DateTime.Today);", view, StringComparison.Ordinal);
        Assert.Contains("var anchorMonth = Date(DateTime.Today);", view, StringComparison.Ordinal);
        Assert.Contains("Field(\"기준일\", anchorDate)", view, StringComparison.Ordinal);
        Assert.Contains("Field(\"기준월\", anchorMonth)", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Field(\"기준월\", anchorDate)", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Receive_issue_uses_separate_date_pickers_for_receive_and_issue()
    {
        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        var viewStart = forms.IndexOf("public sealed class ReceiveIssueView", StringComparison.Ordinal);
        var stockStart = forms.IndexOf("public sealed class StockView", StringComparison.Ordinal);
        Assert.True(viewStart >= 0 && stockStart > viewStart);
        var view = forms[viewStart..stockStart];
        Assert.Contains("var dateReceive = Date();", view, StringComparison.Ordinal);
        Assert.Contains("var dateIssue = Date();", view, StringComparison.Ordinal);
        Assert.Contains("Field(\"입고일\", dateReceive)", view, StringComparison.Ordinal);
        Assert.Contains("Field(\"출고일\", dateIssue)", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Field(\"입고일\", date)", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Field(\"출고일\", date)", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Receive_issue_add_creates_new_item_from_free_text_on_receive()
    {
        var forms = ReadRepoFile("Inventory.App", "Views", "WorkForms.cs");
        var ui = ReadRepoFile("Inventory.App", "Views", "UiComponents.cs");
        var viewStart = forms.IndexOf("public sealed class ReceiveIssueView", StringComparison.Ordinal);
        var stockStart = forms.IndexOf("public sealed class StockView", StringComparison.Ordinal);
        Assert.True(viewStart >= 0 && stockStart > viewStart);
        var view = forms[viewStart..stockStart];
        Assert.Contains("FindOrCreateItemByName", view, StringComparison.Ordinal);
        Assert.Contains("새 품목으로 등록하고 추가할까요?", view, StringComparison.Ordinal);
        Assert.Contains("목록에서 품목을 고르세요.", view, StringComparison.Ordinal);
        Assert.Contains("search.TypedText", view, StringComparison.Ordinal);
        Assert.Contains("PreferExactMatch", ui, StringComparison.Ordinal);
        Assert.Contains("_suppressListSelection", ui, StringComparison.Ordinal);
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

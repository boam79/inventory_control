using Inventory.Core;
using Inventory.Infrastructure;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using Microsoft.Win32;
using SkiaSharp;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Inventory.App.Views;

public enum VoucherMode { Receive, Issue }

public sealed class ReceiveIssueView : WorkspaceView
{
    public sealed record LaunchContext(VoucherMode Mode, string? ItemCode, string? ItemName, bool ExpandForm);

    internal static LaunchContext? PendingLaunch { get; set; }

    private sealed class CartLine
    {
        public required string 품목 { get; init; }
        public required string 코드 { get; init; }
        public required decimal 수량 { get; init; }
        public decimal? 단가 { get; init; }
        public string? LOT { get; init; }
        public string 총금액 => 단가 is { } p ? $"{(수량 * p):N0}원" : "—";
    }

    private sealed class RecentDocRow
    {
        public required string 유형 { get; init; }
        public required string 일자 { get; init; }
        public required int 전표 { get; init; }
        public required string 품목 { get; init; }
        public required int 품목수 { get; init; }
        public required string 총금액 { get; init; }
        public required string 상태 { get; init; }
        public required DocumentType DocType { get; init; }
        public required bool IsCancelled { get; init; }
    }

    public ReceiveIssueView()
    {
        var perms = AppSession.Current?.Permissions ?? RolePermissions.For(UserRole.Viewer);
        var canReceive = perms.CanReceive;
        var canIssue = perms.CanIssue;
        var mode = canReceive ? VoucherMode.Receive : VoucherMode.Issue;
        LaunchContext? launch = PendingLaunch;
        if (launch is not null)
        {
            mode = launch.Mode;
            if (mode == VoucherMode.Receive && !canReceive)
            {
                mode = VoucherMode.Issue;
            }
            else if (mode == VoucherMode.Issue && !canIssue)
            {
                mode = VoucherMode.Receive;
            }

            PendingLaunch = null;
        }

        var pageBanner = CreatePageBanner();
        var docFilter = "all";
        var dateReceive = Date();
        var dateIssue = Date();
        var supplier = Box();
        var dept = Box();
        var qty = Box("1");
        var price = Box("0");
        var lineTotal = new TextBlock { Text = "0원", MinWidth = 160, Width = 180, VerticalAlignment = VerticalAlignment.Center };
        var lot = Box();
        var receiveItemSearch = new ItemSearchBox(q =>
        {
            using var db = AppHost.OpenDb();
            var svc = new InventoryService(db, AppHost.Actor);
            return svc.SearchStockSnapshots(q, take: 20).Select(UiComponents.StockSuggestion).ToList();
        });
        var issueItemSearch = new ItemSearchBox(q =>
        {
            using var db = AppHost.OpenDb();
            var svc = new InventoryService(db, AppHost.Actor);
            return svc.ItemsAvailableForIssue()
                .Where(i => i.Code.Contains(q, StringComparison.OrdinalIgnoreCase)
                            || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.Name)
                .Take(20)
                .Select(i => UiComponents.ItemSuggestion(i))
                .ToList();
        });
        var selectedCode = "";
        receiveItemSearch.SelectionChanged += s => selectedCode = s?.Code ?? "";
        issueItemSearch.SelectionChanged += s =>
        {
            selectedCode = s?.Code ?? "";
            if (s is null || !string.IsNullOrWhiteSpace(dept.Text))
            {
                return;
            }

            using var db = AppHost.OpenDb();
            var item = db.Items.FirstOrDefault(i => i.Code == s.Code);
            if (item?.DefaultDepartmentId is { } deptId)
            {
                var deptName = db.Departments.FirstOrDefault(d => d.Id == deptId)?.Name;
                if (!string.IsNullOrWhiteSpace(deptName))
                {
                    dept.Text = deptName;
                }
            }
        };

        var itemFieldReceive = Field("품목", receiveItemSearch.Input);
        var itemFieldIssue = Field("품목", issueItemSearch.Input);
        var supplierField = Field("공급업체", supplier);
        var priceField = Field("단가", price);
        var lineTotalField = Field("총금액", lineTotal);
        var deptField = Field("사용부서", dept);
        var lotField = Field("LOT", lot);
        var dateFieldReceive = Field("입고일", dateReceive);
        var dateFieldIssue = Field("출고일", dateIssue);
        var qtyField = Field("수량", qty);

        var cart = new List<CartLine>();
        var cartHost = new StackPanel();
        List<RecentDocRow> recent = [];
        RecentDocRow? selectedDoc = null;
        DataGrid? recentGrid = null;
        var recentHost = new StackPanel();
        var recentFilterHost = new StackPanel();
        int? editingId = null;
        VoucherMode currentMode = mode;

        DatePicker ActiveDatePicker() => currentMode == VoucherMode.Receive ? dateReceive : dateIssue;
        var newVoucherExpander = new Expander { IsExpanded = launch?.ExpandForm == true, Margin = new Thickness(0, 0, 0, 0) };
        Button? saveButton = null;

        void UpdateLineTotal()
        {
            if (currentMode != VoucherMode.Receive)
            {
                return;
            }

            if (decimal.TryParse(qty.Text, CultureInfo.CurrentCulture, out var qv)
                && decimal.TryParse(price.Text, CultureInfo.CurrentCulture, out var pv)
                && qv > 0)
            {
                lineTotal.Text = $"{(qv * pv):N0}원";
            }
            else
            {
                lineTotal.Text = "—";
            }
        }

        void ClearEditBannerOnChange()
        {
            if (editingId is not null)
            {
                SetBanner(pageBanner, null);
            }
        }

        void ResetEditState()
        {
            editingId = null;
            cart.Clear();
            RenderCart();
            SetBanner(pageBanner, null);
        }

        void ApplyModeUi()
        {
            var isReceive = currentMode == VoucherMode.Receive;
            itemFieldReceive.Visibility = isReceive ? Visibility.Visible : Visibility.Collapsed;
            itemFieldIssue.Visibility = isReceive ? Visibility.Collapsed : Visibility.Visible;
            dateFieldReceive.Visibility = isReceive ? Visibility.Visible : Visibility.Collapsed;
            dateFieldIssue.Visibility = isReceive ? Visibility.Collapsed : Visibility.Visible;
            supplierField.Visibility = isReceive ? Visibility.Visible : Visibility.Collapsed;
            priceField.Visibility = isReceive ? Visibility.Visible : Visibility.Collapsed;
            lineTotalField.Visibility = isReceive ? Visibility.Visible : Visibility.Collapsed;
            deptField.Visibility = isReceive ? Visibility.Collapsed : Visibility.Visible;
            lotField.Visibility = isReceive ? Visibility.Collapsed : Visibility.Visible;
            newVoucherExpander.Header = isReceive ? "+ 새 입고" : "+ 새 출고";
            if (saveButton is not null)
            {
                saveButton.Content = isReceive ? "입고 저장" : "출고 저장";
            }
        }

        void SwitchMode(VoucherMode next, bool resetForm = true)
        {
            if (currentMode == next)
            {
                return;
            }

            currentMode = next;
            if (resetForm)
            {
                ResetEditState();
            }
            else if (next == VoucherMode.Issue)
            {
                dateIssue.SelectedDate = dateReceive.SelectedDate;
            }
            else
            {
                dateReceive.SelectedDate = dateIssue.SelectedDate;
            }

            ApplyModeUi();
        }

        void ReloadRecent()
        {
            using var db = AppHost.OpenDb();
            var svc = new InventoryService(db, AppHost.Actor);
            recent = svc.ListDocumentSummaries(80).Select(d => new RecentDocRow
            {
                유형 = d.Type == DocumentType.Receipt ? "입고" : "출고",
                일자 = d.DocumentDate.ToString("yyyy-MM-dd"),
                전표 = d.Id,
                품목 = d.LineCount > 1 ? $"{d.FirstItemName} 등 {d.LineCount}건" : d.FirstItemName ?? "—",
                품목수 = d.LineCount,
                총금액 = $"{d.TotalAmount:N0}원",
                상태 = d.IsCancelled ? "삭제됨" : "저장",
                DocType = d.Type,
                IsCancelled = d.IsCancelled
            }).ToList();
            RenderRecentGrid();
        }

        void RenderRecentGrid()
        {
            selectedDoc = null;
            recentHost.Children.Clear();
            recentFilterHost.Children.Clear();
            recentFilterHost.Children.Add(BuildFilterChips(docFilter, id =>
            {
                docFilter = id;
                RenderRecentGrid();
            }, ("all", "전체"), ("receive", "입고"), ("issue", "출고")));

            var filtered = docFilter switch
            {
                "receive" => recent.Where(r => r.DocType == DocumentType.Receipt).ToList(),
                "issue" => recent.Where(r => r.DocType == DocumentType.Issue).ToList(),
                _ => recent
            };

            recentHost.Children.Add(recentFilterHost);
            if (filtered.Count == 0)
            {
                recentHost.Children.Add(UiComponents.EmptyState("최근 전표가 없습니다", "품목을 추가한 뒤 저장하세요."));
                recentGrid = null;
                return;
            }

            var next = TableGrid(filtered, allowMultiSelect: true,
                new ColumnSpec("유형", "유형", Width: 56),
                new ColumnSpec("일자", "일자"),
                new ColumnSpec("전표", "전표"),
                new ColumnSpec("품목", "품목"),
                new ColumnSpec("품목수", "품목수", ColumnAlign.Right),
                new ColumnSpec("총금액", "총금액", ColumnAlign.Right),
                new ColumnSpec("상태", "상태", Width: 72));
            next.SelectionChanged += (_, _) => selectedDoc = next.SelectedItem as RecentDocRow;
            next.MouseDoubleClick += (_, _) =>
            {
                if (next.SelectedItem is RecentDocRow row)
                {
                    BeginEdit(row);
                }
            };
            recentGrid = next;
            var editRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            editRow.Children.Add(Primary("수정", (_, _) => BeginEdit()));
            editRow.Children.Add(Danger("삭제", (_, _) => DeleteSelected()));
            recentHost.Children.Add(next);
            recentHost.Children.Add(editRow);
        }

        List<RecentDocRow> SelectedRecentRows()
            => recentGrid?.SelectedItems.OfType<RecentDocRow>().ToList() ?? [];

        void BeginEdit(RecentDocRow? fromDoubleClick = null)
        {
            RecentDocRow? target = fromDoubleClick;
            if (target is null)
            {
                var selected = SelectedRecentRows();
                if (selected.Count == 0)
                {
                    SetBanner(pageBanner, "목록에서 전표를 고르세요.", isError: true);
                    return;
                }

                if (selected.Count > 1)
                {
                    SetBanner(pageBanner, "수정은 한 건만 선택할 수 있습니다.", isError: true);
                    return;
                }

                target = selected[0];
            }

            selectedDoc = target;
            if (target.IsCancelled)
            {
                SetBanner(pageBanner, "이미 삭제(취소)된 전표입니다.", isError: true);
                return;
            }

            var requiredMode = target.DocType == DocumentType.Receipt ? VoucherMode.Receive : VoucherMode.Issue;
            if (requiredMode == VoucherMode.Receive && !canReceive)
            {
                SetBanner(pageBanner, "입고 전표 수정 권한이 없습니다.", isError: true);
                return;
            }

            if (requiredMode == VoucherMode.Issue && !canIssue)
            {
                SetBanner(pageBanner, "출고 전표 수정 권한이 없습니다.", isError: true);
                return;
            }

            try
            {
                using var db = AppHost.OpenDb();
                var detail = new InventoryService(db, AppHost.Actor).GetDocumentDetail(target.전표);
                SwitchMode(requiredMode, resetForm: false);
                editingId = detail.Id;
                ActiveDatePicker().SelectedDate = detail.DocumentDate;
                supplier.Text = detail.SupplierName ?? "";
                dept.Text = detail.DepartmentName ?? "";
                cart.Clear();
                foreach (var line in detail.Lines)
                {
                    cart.Add(new CartLine
                    {
                        품목 = line.ItemName,
                        코드 = line.ItemCode,
                        수량 = line.Quantity,
                        단가 = detail.Type == DocumentType.Receipt ? line.UnitPrice : null,
                        LOT = line.LotNumber
                    });
                }

                RenderCart();
                if (detail.Lines.Count > 0)
                {
                    var first = detail.Lines[0];
                    selectedCode = first.ItemCode;
                    if (detail.Type == DocumentType.Receipt)
                    {
                        receiveItemSearch.SetSelection(first.ItemCode, first.ItemName);
                        price.Text = first.UnitPrice.ToString(CultureInfo.CurrentCulture);
                    }
                    else
                    {
                        issueItemSearch.SetSelection(first.ItemCode, first.ItemName);
                        lot.Text = first.LotNumber ?? "";
                    }

                    qty.Text = first.Quantity.ToString(CultureInfo.CurrentCulture);
                }

                UpdateLineTotal();
                newVoucherExpander.IsExpanded = true;
                SetBanner(pageBanner, $"전표 {detail.Id} 수정 중 — 저장하면 기존 전표는 삭제되고 새로 저장됩니다.");
            }
            catch (Exception ex)
            {
                SetBanner(pageBanner, $"원인: {AppLog.Sanitize(ex.Message)}", isError: true);
            }
        }

        void DeleteSelected()
        {
            var selected = SelectedRecentRows();
            if (selected.Count == 0)
            {
                SetBanner(pageBanner, "목록에서 전표를 고르세요.", isError: true);
                return;
            }

            var toDelete = selected.Where(d => !d.IsCancelled).ToList();
            if (toDelete.Count == 0)
            {
                SetBanner(pageBanner, "이미 삭제된 전표입니다.", isError: true);
                return;
            }

            var confirm = toDelete.Count == 1
                ? $"전표 {toDelete[0].전표}를 삭제할까요? 재고는 원래대로 돌아갑니다."
                : $"선택한 {toDelete.Count}건의 전표를 삭제할까요? 재고는 원래대로 돌아갑니다.";
            if (MessageBox.Show(
                    confirm,
                    ProductInfo.DisplayName,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var ids = toDelete.Select(d => d.전표).ToList();
                SetBanner(pageBanner, AppHost.Run((_, s) =>
                {
                    foreach (var id in ids)
                    {
                        s.DeleteDocument(id);
                    }

                    return ids.Count == 1
                        ? "삭제 완료. 재고는 원래대로 돌아갑니다."
                        : $"{ids.Count}건 삭제 완료. 재고는 원래대로 돌아갑니다.";
                }));
                if (editingId is { } editId && ids.Contains(editId))
                {
                    editingId = null;
                }

                ReloadRecent();
            }
            catch (Exception ex)
            {
                SetBanner(pageBanner, $"원인: {AppLog.Sanitize(ex.Message)}", isError: true);
            }
        }

        void RenderCart()
        {
            cartHost.Children.Clear();
            if (cart.Count == 0)
            {
                cartHost.Children.Add(UiComponents.EmptyState("이번 전표에 품목이 없습니다", "품목을 고른 뒤 「품목 추가」를 누르세요."));
                return;
            }

            if (currentMode == VoucherMode.Receive)
            {
                cartHost.Children.Add(TableGrid(cart.ToList(),
                    new ColumnSpec("품목", nameof(CartLine.품목)),
                    new ColumnSpec("코드", nameof(CartLine.코드)),
                    new ColumnSpec("수량", nameof(CartLine.수량), ColumnAlign.Right),
                    new ColumnSpec("단가", nameof(CartLine.단가), ColumnAlign.Right),
                    new ColumnSpec("총금액", nameof(CartLine.총금액), ColumnAlign.Right)));
            }
            else
            {
                cartHost.Children.Add(TableGrid(cart.ToList(),
                    new ColumnSpec("품목", nameof(CartLine.품목)),
                    new ColumnSpec("코드", nameof(CartLine.코드)),
                    new ColumnSpec("수량", nameof(CartLine.수량), ColumnAlign.Right),
                    new ColumnSpec("LOT", nameof(CartLine.LOT))));
            }
        }

        var add = Primary("품목 추가", (_, _) =>
        {
            ItemSearchBox search = currentMode == VoucherMode.Receive ? receiveItemSearch : issueItemSearch;
            if (!search.TryGetSelection(out var picked) || picked is null)
            {
                SetBanner(pageBanner, "품목을 선택 또는 입력하세요.", isError: true);
                return;
            }

            if (!decimal.TryParse(qty.Text, CultureInfo.CurrentCulture, out var qv) || qv <= 0)
            {
                SetBanner(pageBanner, "수량을 숫자로 입력하세요.", isError: true);
                return;
            }

            if (currentMode == VoucherMode.Receive)
            {
                if (!decimal.TryParse(price.Text, CultureInfo.CurrentCulture, out var pv) || pv < 0)
                {
                    SetBanner(pageBanner, "수량과 단가를 숫자로 입력하세요.", isError: true);
                    return;
                }

                selectedCode = picked.Code;
                cart.Add(new CartLine { 품목 = picked.Name, 코드 = picked.Code, 수량 = qv, 단가 = pv });
            }
            else
            {
                selectedCode = picked.Code;
                cart.Add(new CartLine
                {
                    품목 = picked.Name,
                    코드 = picked.Code,
                    수량 = qv,
                    LOT = string.IsNullOrWhiteSpace(lot.Text) ? null : lot.Text.Trim()
                });
            }

            SetBanner(pageBanner, null);
            RenderCart();
        });

        saveButton = Primary("입고 저장", (_, _) =>
        {
            if (cart.Count == 0)
            {
                SetBanner(pageBanner, "품목을 추가한 다음 저장하세요.", isError: true);
                return;
            }

            try
            {
                SetBanner(pageBanner, AppHost.Run((_, svc) =>
                {
                    if (editingId is { } editId)
                    {
                        svc.DeleteDocument(editId);
                    }

                    if (currentMode == VoucherMode.Receive)
                    {
                        int? supplierId = null;
                        if (!string.IsNullOrWhiteSpace(supplier.Text))
                        {
                            var found = svc.SearchSuppliers(supplier.Text).FirstOrDefault()
                                        ?? svc.CreateSupplier(supplier.Text);
                            supplierId = found.Id;
                        }

                        var doc = svc.Receive(
                            dateReceive.SelectedDate ?? DateTime.Today,
                            supplierId,
                            null,
                            cart.Select(l => new ReceiptLineRequest
                            {
                                ItemCode = l.코드,
                                Quantity = l.수량,
                                UnitPrice = l.단가 ?? 0,
                                LotNumber = null,
                                ExpiryDate = null
                            }).ToList());
                        cart.Clear();
                        RenderCart();
                        var wasEdit = editingId.HasValue;
                        editingId = null;
                        return wasEdit
                            ? $"수정 저장됨. 전표 {doc.Id}"
                            : doc.DuplicateWarning
                                ? "저장됨. 경고: 같은 공급업체·증빙번호 입고가 이미 있습니다."
                                : $"저장됨. 전표 {doc.Id}";
                    }

                    int? deptId = null;
                    if (!string.IsNullOrWhiteSpace(dept.Text))
                    {
                        var found = svc.SearchDepartments(dept.Text).FirstOrDefault()
                                    ?? svc.CreateDepartment(dept.Text);
                        deptId = found.Id;
                    }

                    var issueDoc = svc.Issue(
                        dateIssue.SelectedDate ?? DateTime.Today,
                        deptId,
                        cart.Select(l => new IssueLineRequest
                        {
                            ItemCode = l.코드,
                            Quantity = l.수량,
                            LotNumber = l.LOT
                        }).ToList());
                    cart.Clear();
                    RenderCart();
                    var wasIssueEdit = editingId.HasValue;
                    editingId = null;
                    return wasIssueEdit ? $"수정 저장됨. 전표 {issueDoc.Id}" : $"저장됨. 전표 {issueDoc.Id}";
                }));
                ReloadRecent();
            }
            catch (Exception ex)
            {
                SetBanner(pageBanner, $"원인: {AppLog.Sanitize(ex.Message)}", isError: true);
            }
        });

        var modeToggle = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        if (canReceive)
        {
            var receiveRadio = new RadioButton
            {
                Content = "입고",
                GroupName = "VoucherMode",
                IsChecked = currentMode == VoucherMode.Receive,
                Margin = new Thickness(0, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            receiveRadio.Checked += (_, _) =>
            {
                if (receiveRadio.IsChecked == true)
                {
                    SwitchMode(VoucherMode.Receive);
                }
            };
            modeToggle.Children.Add(receiveRadio);
        }

        if (canIssue)
        {
            var issueRadio = new RadioButton
            {
                Content = "출고",
                GroupName = "VoucherMode",
                IsChecked = currentMode == VoucherMode.Issue,
                VerticalAlignment = VerticalAlignment.Center
            };
            issueRadio.Checked += (_, _) =>
            {
                if (issueRadio.IsChecked == true)
                {
                    SwitchMode(VoucherMode.Issue);
                }
            };
            modeToggle.Children.Add(issueRadio);
        }

        supplier.TextChanged += (_, _) => ClearEditBannerOnChange();
        dept.TextChanged += (_, _) => ClearEditBannerOnChange();
        qty.TextChanged += (_, _) =>
        {
            ClearEditBannerOnChange();
            UpdateLineTotal();
        };
        price.TextChanged += (_, _) =>
        {
            ClearEditBannerOnChange();
            UpdateLineTotal();
        };
        lot.TextChanged += (_, _) => ClearEditBannerOnChange();
        dateReceive.SelectedDateChanged += (_, _) => ClearEditBannerOnChange();
        dateIssue.SelectedDateChanged += (_, _) => ClearEditBannerOnChange();

        var form = FormRow(itemFieldReceive, itemFieldIssue, dateFieldReceive, dateFieldIssue, supplierField, deptField, qtyField, priceField, lineTotalField, lotField);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        actions.Children.Add(add);
        actions.Children.Add(saveButton);
        var newVoucherBody = new StackPanel();
        newVoucherBody.Children.Add(modeToggle);
        newVoucherBody.Children.Add(form);
        newVoucherBody.Children.Add(cartHost);
        newVoucherBody.Children.Add(actions);
        newVoucherExpander.Content = newVoucherBody;

        Content = PageRoot(pageBanner,
            Section("새 전표", newVoucherExpander),
            Section("최근 전표", recentHost));
        ApplyModeUi();
        UpdateLineTotal();
        RenderCart();
        ReloadRecent();

        if (launch?.ItemCode is { } code && launch.ItemName is { } name)
        {
            if (currentMode == VoucherMode.Receive)
            {
                receiveItemSearch.SetSelection(code, name);
            }
            else
            {
                issueItemSearch.SetSelection(code, name);
            }

            selectedCode = code;
            newVoucherExpander.IsExpanded = launch.ExpandForm;
        }
    }
}

public sealed class StockView : WorkspaceView
{
    private sealed class LotRow
    {
        public required string LOT { get; init; }
        public required string 잔량 { get; init; }
        public required string 유효기간 { get; init; }
        public required string 남은일 { get; init; }
        public required bool IsExpired { get; init; }
    }

    private sealed class StockRow
    {
        public required string 품목 { get; init; }
        public required string 코드 { get; init; }
        public required string 현재고 { get; init; }
        public required string 상태 { get; init; }
        public required StockStatusKind Kind { get; init; }
    }

    public StockView()
    {
        var pageBanner = CreatePageBanner();
        var filter = "all";
        var itemSearch = new ItemSearchBox(q =>
        {
            using var db = AppHost.OpenDb();
            var svc = new InventoryService(db, AppHost.Actor);
            return svc.SearchStockSnapshots(q, take: 20).Select(UiComponents.StockSuggestion).ToList();
        });
        var page = 1;
        var all = new List<StockRow>();
        var gridHost = new StackPanel();
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        var pageText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var lotHost = new StackPanel();
        var lotExpander = new Expander { Header = "LOT 상세", IsExpanded = false, Margin = new Thickness(0, 12, 0, 0) };
        lotExpander.Content = lotHost;
        DataGrid? grid = null;
        var chipsHost = new StackPanel();
        StockRow? selectedStock = null;
        var perms = AppSession.Current?.Permissions ?? RolePermissions.For(UserRole.Viewer);

        void ShowLots(string code)
        {
            lotHost.Children.Clear();
            using var db = AppHost.OpenDb();
            var svc = new InventoryService(db, AppHost.Actor);
            var lots = svc.LotsForItem(code).Select(l =>
            {
                var days = l.ExpiryDate is null ? (int?)null : (l.ExpiryDate.Value.Date - DateTime.Today).Days;
                return new LotRow
                {
                    LOT = l.LotNumber,
                    잔량 = l.Quantity.ToString("N3"),
                    유효기간 = l.ExpiryDate?.ToString("yyyy-MM-dd") ?? "없음",
                    남은일 = days is null ? "" : $"{days}일",
                    IsExpired = days is not null && days < 0
                };
            }).ToList();
            lotHost.Children.Add(new TextBlock { Text = "붉은 배경은 유효기간이 지난 LOT입니다", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            var lotGrid = TableGrid(lots,
                new ColumnSpec("LOT", "LOT"),
                new ColumnSpec("잔량", "잔량", ColumnAlign.Right),
                new ColumnSpec("유효기간", "유효기간"),
                new ColumnSpec("남은일", "남은일"));
            lotGrid.LoadingRow += (_, e) =>
            {
                if (e.Row.Item is LotRow row && row.IsExpired)
                {
                    e.Row.Background = new SolidColorBrush(Color.FromRgb(252, 235, 233));
                }
            };
            lotHost.Children.Add(lotGrid);
            lotExpander.IsExpanded = true;
        }

        void Render()
        {
            var pages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)UiLayout.PageSize));
            page = Math.Clamp(page, 1, pages);
            var slice = all.Skip((page - 1) * UiLayout.PageSize).Take(UiLayout.PageSize).ToList();
            count.Text = $"총 {all.Count:N0}건";
            pageText.Text = $"{page}/{pages}";
            var next = TableGrid(slice,
                new ColumnSpec("품목", nameof(StockRow.품목)),
                new ColumnSpec("코드", nameof(StockRow.코드)),
                new ColumnSpec("현재고", nameof(StockRow.현재고), ColumnAlign.Right),
                new ColumnSpec("상태", nameof(StockRow.상태), Width: 96));
            next.LoadingRow += (_, e) =>
            {
                if (e.Row.Item is StockRow row)
                {
                    ApplyStockRowStyle(e.Row, row.Kind);
                }
            };
            next.SelectionChanged += (_, _) =>
            {
                if (next.SelectedItem is StockRow row)
                {
                    selectedStock = row;
                    ShowLots(row.코드);
                }
            };
            gridHost.Children.Clear();
            if (slice.Count == 0)
            {
                gridHost.Children.Add(UiComponents.EmptyState(
                    "표시할 품목이 없습니다",
                    string.IsNullOrWhiteSpace(itemSearch.Input.Text) && filter == "all"
                        ? "품목을 등록하거나 검색 조건을 바꿔 보세요."
                        : "검색·필터 조건에 맞는 품목이 없습니다.",
                    Btn("초기화", (_, _) =>
                    {
                        itemSearch.Input.Text = "";
                        filter = "all";
                        RenderChips();
                        Reload();
                    })));
            }
            else
            {
                gridHost.Children.Add(next);
            }

            grid = next;
        }

        void RenderChips()
        {
            chipsHost.Children.Clear();
            chipsHost.Children.Add(BuildFilterChips(filter, id => { filter = id; Reload(); RenderChips(); },
                ("all", "전체"), ("out", "품절"), ("reorder", "발주"), ("unset", "미설정"), ("exp", "임박")));
        }

        void Reload()
        {
            using var db = AppHost.OpenDb();
            var svc = new InventoryService(db, AppHost.Actor);
            var snaps = svc.SearchStockSnapshots(itemSearch.Input.Text.Trim());
            var shown = filter switch
            {
                "out" => snaps.Where(r => r.Status == StockStatusKind.OutOfStock),
                "reorder" => snaps.Where(r => r.Status == StockStatusKind.Reorder),
                "unset" => snaps.Where(r => r.Status == StockStatusKind.Unset),
                "exp" => snaps.Where(r => r.Status == StockStatusKind.Expiring),
                _ => snaps
            };
            all = shown.Select(s => new StockRow
            {
                품목 = s.Name,
                코드 = s.Code,
                현재고 = s.OnHand?.ToString("N3") ?? "미설정",
                상태 = StatusKo(s.Status),
                Kind = s.Status
            }).ToList();
            page = 1;
            lotHost.Children.Clear();
            lotExpander.IsExpanded = false;
            selectedStock = null;
            Render();
        }

        itemSearch.SelectionChanged += _ => Reload();

        var filters = new StackPanel();
        filters.Children.Add(FormRow(Field("품목", itemSearch.Input)));
        var apply = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        apply.Children.Add(Primary("적용", (_, _) => Reload()));
        apply.Children.Add(Btn("초기화", (_, _) =>
        {
            itemSearch.Input.Text = "";
            filter = "all";
            RenderChips();
            Reload();
        }));
        filters.Children.Add(apply);
        filters.Children.Add(chipsHost);

        var listBody = new StackPanel();
        listBody.Children.Add(gridHost);
        listBody.Children.Add(Pager(count, pageText, () => { page--; Render(); }, () => { page++; Render(); }));
        var shortcutRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        if (perms.CanReceive)
        {
            shortcutRow.Children.Add(Primary("이 품목 입고", (_, _) =>
            {
                if (selectedStock is null)
                {
                    SetBanner(pageBanner, "재고 목록에서 품목을 고르세요.", isError: true);
                    return;
                }

                if (Application.Current?.MainWindow is MainWindow main)
                {
                    main.OpenReceiveIssue(VoucherMode.Receive, selectedStock.코드, selectedStock.품목);
                }
            }));
        }

        if (perms.CanIssue)
        {
            shortcutRow.Children.Add(Primary("이 품목 출고", (_, _) =>
            {
                if (selectedStock is null)
                {
                    SetBanner(pageBanner, "재고 목록에서 품목을 고르세요.", isError: true);
                    return;
                }

                if (Application.Current?.MainWindow is MainWindow main)
                {
                    main.OpenReceiveIssue(VoucherMode.Issue, selectedStock.코드, selectedStock.품목);
                }
            }));
        }

        if (shortcutRow.Children.Count > 0)
        {
            listBody.Children.Add(shortcutRow);
        }

        listBody.Children.Add(lotExpander);

        Content = PageRoot(pageBanner,
            Section("검색·필터", filters),
            Section("재고 목록", listBody));
        RenderChips();
        Reload();
    }
}

public sealed class StatsView : WorkspaceView
{
    public StatsView()
    {
        var pageBanner = CreatePageBanner();
        var kind = new ComboBox { ItemsSource = new[] { "일", "월", "분기", "년", "기간지정" }, SelectedIndex = 1, Width = 140 };
        var dimension = new ComboBox { ItemsSource = new[] { "품목", "분류", "부서", "공급업체" }, SelectedIndex = 1, Width = 140 };
        var anchor = Date(DateTime.Today);
        var customStart = Date(DateTime.Today.AddMonths(-1));
        var customEnd = Date(DateTime.Today);
        var customRow = new WrapPanel { Visibility = Visibility.Collapsed };
        customRow.Children.Add(Field("시작", customStart));
        customRow.Children.Add(Field("종료", customEnd));
        var trend = new CheckBox
        {
            Content = "최근 6기간 추이로 보기 (월별·부서별 비교)",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        IReadOnlyList<ReportRow> current = [];
        var gridHost = new StackPanel();
        var chartHost = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var summary = new TextBlock { Margin = new Thickness(0, 0, 0, 8), FontWeight = FontWeights.SemiBold };

        void Reload()
        {
            var period = kind.SelectedIndex switch
            {
                0 => ReportPeriodKind.Day,
                2 => ReportPeriodKind.Quarter,
                3 => ReportPeriodKind.Year,
                4 => ReportPeriodKind.Custom,
                _ => ReportPeriodKind.Month
            };
            var dim = dimension.SelectedIndex switch
            {
                0 => ReportDimension.Item,
                2 => ReportDimension.Department,
                3 => ReportDimension.Supplier,
                _ => ReportDimension.Category
            };
            var baseAnchor = anchor.SelectedDate ?? DateTime.Today;
            var periodsBack = trend.IsChecked == true && period != ReportPeriodKind.Custom ? 6 : 1;
            using var db = AppHost.OpenDb();
            current = ReportAnalytics.QueryTrend(
                db, period, baseAnchor, dim, periodsBack, customStart.SelectedDate, customEnd.SelectedDate);
            summary.Text = current.Count == 0
                ? "해당 기간 집계가 없습니다."
                : $"총 {current.Count:N0}건 · 사용 {current.Sum(r => r.IssueQty):N3} · 입고 {current.Sum(r => r.ReceiptQty):N3} · 구매 {current.Sum(r => r.PurchaseAmount):N0}원";

            chartHost.Children.Clear();
            var periodTotals = current
                .GroupBy(r => r.PeriodLabel, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new { Label = g.Key, Issue = (double)g.Sum(r => r.IssueQty), Receipt = (double)g.Sum(r => r.ReceiptQty) })
                .ToList();
            if (periodTotals.Count > 1)
            {
                var labels = periodTotals.Select(p => p.Label).ToArray();
                var accent = new SKColor(31, 111, 115);
                var warn = new SKColor(196, 123, 22);
                chartHost.Children.Add(new TextBlock
                {
                    Text = "기간별 출고·입고 추이",
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6)
                });
                chartHost.Children.Add(new CartesianChart
                {
                    Height = 180,
                    Series =
                    [
                        new ColumnSeries<double>
                        {
                            Name = "출고",
                            Values = periodTotals.Select(p => p.Issue).ToArray(),
                            Fill = new SolidColorPaint(accent),
                            Stroke = null
                        },
                        new ColumnSeries<double>
                        {
                            Name = "입고",
                            Values = periodTotals.Select(p => p.Receipt).ToArray(),
                            Fill = new SolidColorPaint(warn),
                            Stroke = null
                        }
                    ],
                    XAxes = [new Axis { Labels = labels, TextSize = 10 }],
                    YAxes = [new Axis { MinLimit = 0, TextSize = 10 }],
                    LegendPosition = LiveChartsCore.Measure.LegendPosition.Top
                });
            }

            var display = current
                .OrderBy(r => r.PeriodLabel, StringComparer.Ordinal)
                .ThenByDescending(r => r.IssueQty)
                .Select(r => new
                {
                    기간 = r.PeriodLabel,
                    구분 = r.Dimension,
                    사용 = r.IssueQty.ToString("N3"),
                    입고 = r.ReceiptQty.ToString("N3"),
                    구매금액 = r.PurchaseAmount.ToString("N0") + "원"
                }).ToList();
            gridHost.Children.Clear();
            if (display.Count == 0)
            {
                gridHost.Children.Add(UiComponents.EmptyState("집계 결과가 없습니다", "기간·집계 조건을 바꿔 보세요."));
            }
            else
            {
                gridHost.Children.Add(TableGrid(display,
                    new ColumnSpec("기간", "기간"),
                    new ColumnSpec("구분", "구분"),
                    new ColumnSpec("사용", "사용", ColumnAlign.Right),
                    new ColumnSpec("입고", "입고", ColumnAlign.Right),
                    new ColumnSpec("구매금액", "구매금액")));
            }
        }

        kind.SelectionChanged += (_, _) =>
        {
            customRow.Visibility = kind.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
            trend.IsEnabled = kind.SelectedIndex != 4;
            if (kind.SelectedIndex == 4)
            {
                trend.IsChecked = false;
            }

            Reload();
        };
        dimension.SelectionChanged += (_, _) => Reload();
        anchor.SelectedDateChanged += (_, _) => Reload();
        customStart.SelectedDateChanged += (_, _) => Reload();
        customEnd.SelectedDateChanged += (_, _) => Reload();
        trend.Checked += (_, _) => Reload();
        trend.Unchecked += (_, _) => Reload();

        var filters = new StackPanel();
        filters.Children.Add(FormRow(Field("기간", kind), Field("기준일", anchor), Field("집계", dimension)));
        filters.Children.Add(customRow);
        filters.Children.Add(trend);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(Primary("적용", (_, _) => Reload()));
        actions.Children.Add(Btn("Excel 내보내기", (_, _) =>
        {
            if (current.Count == 0)
            {
                return;
            }

            var dlg = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "통계보고서.xlsx" };
            if (dlg.ShowDialog() == true)
            {
                ExcelCatalog.ExportReport(current, dlg.FileName);
            }
        }));
        filters.Children.Add(actions);
        var list = new StackPanel();
        list.Children.Add(summary);
        list.Children.Add(chartHost);
        list.Children.Add(gridHost);
        Content = PageRoot(pageBanner,
            Section("검색·필터", filters),
            Section("집계 결과", list));
        Reload();
    }
}

public sealed class UsersView : WorkspaceView
{
    public UsersView()
    {
        var user = Box();
        var pass = new PasswordBox { MinWidth = 160, Width = 180, Padding = new Thickness(8, 6, 8, 6) };
        var role = new ComboBox { ItemsSource = Enum.GetValues<UserRole>(), SelectedItem = UserRole.Viewer, Width = 180 };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var gridHost = new StackPanel();

        void Reload()
        {
            using var db = AppHost.OpenDb();
            var rows = db.Users.Select(u => new { 아이디 = u.UserName, 역할 = u.Role.ToString(), 사용 = u.IsActive ? "사용" : "중지" }).ToList();
            gridHost.Children.Clear();
            gridHost.Children.Add(new TextBlock { Text = $"총 {rows.Count:N0}건", Margin = new Thickness(0, 0, 0, 8) });
            gridHost.Children.Add(TableGrid(rows, ("아이디", "아이디"), ("역할", "역할"), ("사용", "사용")));
        }

        var register = new StackPanel();
        register.Children.Add(FormRow(Field("아이디", user), Field("비밀번호", pass), Field("역할", role)));
        register.Children.Add(Primary("등록", (_, _) =>
        {
            if (AppSession.Current?.Permissions.CanManageUsers != true)
            {
                status.Text = "원인: 사용자 관리 권한이 없습니다.";
                return;
            }

            status.Text = AppHost.Run((db, _) =>
            {
                new AuthenticationService(db).CreateUser(user.Text.Trim(), pass.Password, (UserRole)role.SelectedItem);
                return "사용자를 추가했습니다.";
            });
            Reload();
        }));
        register.Children.Add(status);
        Content = new StackPanel
        {
            Children =
            {
                Section("등록", register),
                Section("목록", gridHost)
            }
        };
        Reload();
    }
}

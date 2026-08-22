using Inventory.Core;
using Inventory.Infrastructure;
using Microsoft.Win32;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Inventory.App.Views;

public sealed class ReceiveView : WorkspaceView
{
    private sealed class CartLine
    {
        public required string 품목 { get; init; }
        public required string 코드 { get; init; }
        public required decimal 수량 { get; init; }
        public required decimal 단가 { get; init; }
    }

    private sealed class ReceiptDocRow
    {
        public required string 입고일 { get; init; }
        public required int 전표 { get; init; }
        public required string 품목 { get; init; }
        public required int 품목수 { get; init; }
        public required string 상태 { get; init; }
        public required bool IsCancelled { get; init; }
    }

    public ReceiveView()
    {
        var date = Date();
        var supplier = Box();
        var itemQuery = Box();
        itemQuery.Width = 220;
        var selectedCode = "";
        var qty = Box("1");
        var price = Box("0");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        var cart = new List<CartLine>();
        DataGrid? cartGrid = null;
        var listStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        List<ReceiptDocRow> recent = [];
        ReceiptDocRow? selectedDoc = null;
        DataGrid? recentGrid = null;
        int? editingId = null;

        void ReloadRecent()
        {
            using var db = AppHost.OpenDb();
            var svc = new InventoryService(db, AppHost.Actor);
            recent = svc.ListDocumentSummaries(80).Where(d => d.Type == DocumentType.Receipt).Select(d => new ReceiptDocRow
            {
                입고일 = d.DocumentDate.ToString("yyyy-MM-dd"),
                전표 = d.Id,
                품목 = d.LineCount > 1 ? $"{d.FirstItemName} 등 {d.LineCount}건" : d.FirstItemName ?? "—",
                품목수 = d.LineCount,
                상태 = d.IsCancelled ? "삭제됨" : "저장",
                IsCancelled = d.IsCancelled
            }).ToList();
            selectedDoc = null;
            var next = TableGrid(recent, allowMultiSelect: true, ("입고일", "입고일"), ("전표", "전표"), ("품목", "품목"), ("품목수", "품목수"), ("상태", "상태"));
            next.SelectionChanged += (_, _) => selectedDoc = next.SelectedItem as ReceiptDocRow;
            next.MouseDoubleClick += (_, _) =>
            {
                if (next.SelectedItem is ReceiptDocRow row)
                {
                    BeginEdit(row);
                }
            };
            if (recentGrid is null)
            {
                recentGrid = next;
                return;
            }

            var parent = (Panel)recentGrid.Parent!;
            var index = parent.Children.IndexOf(recentGrid);
            parent.Children.RemoveAt(index);
            recentGrid = next;
            parent.Children.Insert(index, next);
        }

        List<ReceiptDocRow> SelectedRecentRows()
            => recentGrid?.SelectedItems.OfType<ReceiptDocRow>().ToList() ?? [];

        void BeginEdit(ReceiptDocRow? fromDoubleClick = null)
        {
            ReceiptDocRow? target = fromDoubleClick;
            if (target is null)
            {
                var selected = SelectedRecentRows();
                if (selected.Count == 0)
                {
                    listStatus.Text = "목록에서 전표를 고르세요.";
                    return;
                }

                if (selected.Count > 1)
                {
                    listStatus.Text = "수정은 한 건만 선택할 수 있습니다.";
                    return;
                }

                target = selected[0];
            }

            selectedDoc = target;
            if (target.IsCancelled)
            {
                listStatus.Text = "이미 삭제(취소)된 전표입니다.";
                return;
            }

            try
            {
                using var db = AppHost.OpenDb();
                var detail = new InventoryService(db, AppHost.Actor).GetDocumentDetail(target.전표);
                if (detail.Type != DocumentType.Receipt)
                {
                    listStatus.Text = "입고 전표만 수정할 수 있습니다.";
                    return;
                }

                editingId = detail.Id;
                date.SelectedDate = detail.DocumentDate;
                supplier.Text = detail.SupplierName ?? "";
                cart.Clear();
                foreach (var line in detail.Lines)
                {
                    cart.Add(new CartLine
                    {
                        품목 = line.ItemName,
                        코드 = line.ItemCode,
                        수량 = line.Quantity,
                        단가 = line.UnitPrice
                    });
                }

                RenderCart();
                if (detail.Lines.Count > 0)
                {
                    var first = detail.Lines[0];
                    selectedCode = first.ItemCode;
                    itemQuery.Text = first.ItemName;
                    qty.Text = first.Quantity.ToString(CultureInfo.CurrentCulture);
                    price.Text = first.UnitPrice.ToString(CultureInfo.CurrentCulture);
                }

                listStatus.Text = $"전표 {detail.Id} 수정 중 — 저장하면 기존 전표는 삭제되고 새로 저장됩니다.";
                status.Text = listStatus.Text;
            }
            catch (Exception ex)
            {
                listStatus.Text = $"원인: {AppLog.Sanitize(ex.Message)}";
            }
        }

        void DeleteSelected()
        {
            var selected = SelectedRecentRows();
            if (selected.Count == 0)
            {
                listStatus.Text = "목록에서 전표를 고르세요.";
                return;
            }

            var toDelete = selected.Where(d => !d.IsCancelled).ToList();
            if (toDelete.Count == 0)
            {
                listStatus.Text = "이미 삭제된 전표입니다.";
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
                listStatus.Text = AppHost.Run((_, s) =>
                {
                    foreach (var id in ids)
                    {
                        s.DeleteDocument(id);
                    }

                    return ids.Count == 1
                        ? "삭제 완료. 재고는 원래대로 돌아갑니다."
                        : $"{ids.Count}건 삭제 완료. 재고는 원래대로 돌아갑니다.";
                });
                if (editingId is { } editId && ids.Contains(editId))
                {
                    editingId = null;
                }

                ReloadRecent();
            }
            catch (Exception ex)
            {
                listStatus.Text = $"원인: {AppLog.Sanitize(ex.Message)}";
            }
        }

        void RenderCart()
        {
            var next = TableGrid(cart.ToList(),
                ("품목", nameof(CartLine.품목)),
                ("코드", nameof(CartLine.코드)),
                ("수량", nameof(CartLine.수량)),
                ("단가", nameof(CartLine.단가)));
            if (cartGrid is null)
            {
                return;
            }

            var parent = (Panel)cartGrid.Parent!;
            var index = parent.Children.IndexOf(cartGrid);
            parent.Children.RemoveAt(index);
            cartGrid = next;
            parent.Children.Insert(index, next);
        }

        void PickFirstMatch()
        {
            var q = itemQuery.Text.Trim();
            if (q.Length == 0)
            {
                return;
            }

            using var db = AppHost.OpenDb();
            var item = db.Items.Where(i => i.IsActive && (i.Code.Contains(q) || i.Name.Contains(q)))
                .OrderBy(i => i.Name).FirstOrDefault();
            if (item is null)
            {
                status.Text = "해당하는 품목이 없습니다.";
                selectedCode = "";
                return;
            }

            selectedCode = item.Code;
            itemQuery.Text = item.Name;
            status.Text = $"{item.Name} ({item.Code})";
        }

        var add = Primary("등록", (_, _) =>
        {
            PickFirstMatch();
            if (string.IsNullOrWhiteSpace(selectedCode))
            {
                status.Text = "품목을 선택 또는 입력하세요.";
                return;
            }

            if (!decimal.TryParse(qty.Text, CultureInfo.CurrentCulture, out var qv) || qv <= 0
                || !decimal.TryParse(price.Text, CultureInfo.CurrentCulture, out var pv) || pv < 0)
            {
                status.Text = "수량과 단가를 숫자로 입력하세요.";
                return;
            }

            cart.Add(new CartLine
            {
                품목 = itemQuery.Text.Trim(),
                코드 = selectedCode,
                수량 = qv,
                단가 = pv
            });
            status.Text = "";
            RenderCart();
        });

        var save = Primary("입고 저장", (_, _) =>
        {
            if (cart.Count == 0)
            {
                status.Text = "품목을 등록한 다음 저장하세요.";
                return;
            }

            try
            {
                status.Text = AppHost.Run((_, svc) =>
                {
                    if (editingId is { } editId)
                    {
                        svc.DeleteDocument(editId);
                    }

                    int? supplierId = null;
                    if (!string.IsNullOrWhiteSpace(supplier.Text))
                    {
                        var found = svc.SearchSuppliers(supplier.Text).FirstOrDefault()
                                    ?? svc.CreateSupplier(supplier.Text);
                        supplierId = found.Id;
                    }

                    var doc = svc.Receive(
                        date.SelectedDate ?? DateTime.Today,
                        supplierId,
                        null,
                        cart.Select(l => new ReceiptLineRequest
                        {
                            ItemCode = l.코드,
                            Quantity = l.수량,
                            UnitPrice = l.단가,
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
                });
                listStatus.Text = status.Text;
                ReloadRecent();
            }
            catch (Exception ex)
            {
                status.Text = $"원인: {AppLog.Sanitize(ex.Message)}";
            }
        });

        itemQuery.LostFocus += (_, _) => PickFirstMatch();
        itemQuery.KeyUp += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                PickFirstMatch();
            }
        };

        var form = FormRow(
            Field("품목", itemQuery),
            Field("입고일", date),
            Field("공급업체", supplier),
            Field("수량", qty),
            Field("단가", price));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        actions.Children.Add(add);
        actions.Children.Add(save);
        var register = new StackPanel();
        register.Children.Add(form);
        register.Children.Add(actions);
        register.Children.Add(status);

        cartGrid = TableGrid(cart);
        var listBody = new StackPanel();
        listBody.Children.Add(cartGrid);
        var recentBody = new StackPanel();
        recentGrid ??= TableGrid(recent);
        recentBody.Children.Add(recentGrid);
        var editRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        editRow.Children.Add(Primary("수정", (_, _) => BeginEdit()));
        editRow.Children.Add(Danger("삭제", (_, _) => DeleteSelected()));
        recentBody.Children.Add(editRow);
        recentBody.Children.Add(listStatus);
        Content = new StackPanel
        {
            Children =
            {
                Section("등록", register),
                Section("목록", listBody),
                Section("최근 입고 전표", recentBody)
            }
        };
        ReloadRecent();
    }
}

public sealed class IssueView : WorkspaceView
{
    private sealed class IssueDocRow
    {
        public required string 출고일 { get; init; }
        public required int 전표 { get; init; }
        public required string 품목 { get; init; }
        public string? FirstItemName { get; init; }
        public required int 품목수 { get; init; }
        public required string 상태 { get; init; }
        public required bool IsCancelled { get; init; }
    }

    public IssueView()
    {
        var date = Date();
        var dept = Box();
        var itemQuery = Box();
        itemQuery.Width = 220;
        var selectedCode = "";
        var qty = Box("1");
        var lot = Box();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        var listStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        List<IssueDocRow> rows = [];
        IssueDocRow? selectedDoc = null;
        DataGrid? grid = null;
        int? editingId = null;

        void PickFirstMatch()
        {
            var q = itemQuery.Text.Trim();
            if (q.Length == 0)
            {
                return;
            }

            using var db = AppHost.OpenDb();
            var svc = new InventoryService(db, AppHost.Actor);
            var item = svc.ItemsAvailableForIssue().Where(i => i.Code.Contains(q) || i.Name.Contains(q)).OrderBy(i => i.Name).FirstOrDefault();
            if (item is null)
            {
                status.Text = "출고할 수 있는 품목이 없습니다.";
                selectedCode = "";
                return;
            }

            selectedCode = item.Code;
            itemQuery.Text = item.Name;
            status.Text = $"{item.Name} ({item.Code})";
            if (string.IsNullOrWhiteSpace(dept.Text) && item.DefaultDepartmentId is { } deptId)
            {
                var deptName = db.Departments.FirstOrDefault(d => d.Id == deptId)?.Name;
                if (!string.IsNullOrWhiteSpace(deptName))
                {
                    dept.Text = deptName;
                }
            }
        }

        void ReloadRecent()
        {
            using var db = AppHost.OpenDb();
            var svc = new InventoryService(db, AppHost.Actor);
            rows = svc.ListDocumentSummaries(80).Where(d => d.Type == DocumentType.Issue).Select(d => new IssueDocRow
            {
                출고일 = d.DocumentDate.ToString("yyyy-MM-dd"),
                전표 = d.Id,
                품목 = d.LineCount > 1 ? $"{d.FirstItemName} 등 {d.LineCount}건" : d.FirstItemName ?? "—",
                FirstItemName = d.FirstItemName,
                품목수 = d.LineCount,
                상태 = d.IsCancelled ? "삭제됨" : "저장",
                IsCancelled = d.IsCancelled
            }).ToList();
            selectedDoc = null;
            var next = TableGrid(rows, allowMultiSelect: true, ("출고일", "출고일"), ("전표", "전표"), ("품목", "품목"), ("품목수", "품목수"), ("상태", "상태"));
            next.SelectionChanged += (_, _) => selectedDoc = next.SelectedItem as IssueDocRow;
            next.MouseDoubleClick += (_, _) =>
            {
                if (next.SelectedItem is IssueDocRow row)
                {
                    BeginEdit(row);
                }
            };
            if (grid is null)
            {
                grid = next;
                return;
            }

            var parent = (Panel)grid.Parent!;
            var index = parent.Children.IndexOf(grid);
            parent.Children.RemoveAt(index);
            grid = next;
            parent.Children.Insert(index, next);
        }

        List<IssueDocRow> SelectedRecentRows()
            => grid?.SelectedItems.OfType<IssueDocRow>().ToList() ?? [];

        void BeginEdit(IssueDocRow? fromDoubleClick = null)
        {
            IssueDocRow? target = fromDoubleClick;
            if (target is null)
            {
                var selected = SelectedRecentRows();
                if (selected.Count == 0)
                {
                    listStatus.Text = "목록에서 전표를 고르세요.";
                    return;
                }

                if (selected.Count > 1)
                {
                    listStatus.Text = "수정은 한 건만 선택할 수 있습니다.";
                    return;
                }

                target = selected[0];
            }

            selectedDoc = target;
            if (target.IsCancelled)
            {
                listStatus.Text = "이미 삭제된 전표입니다.";
                return;
            }

            try
            {
                using var db = AppHost.OpenDb();
                var detail = new InventoryService(db, AppHost.Actor).GetDocumentDetail(target.전표);
                if (detail.Type != DocumentType.Issue)
                {
                    listStatus.Text = "출고 전표만 수정할 수 있습니다.";
                    return;
                }

                editingId = detail.Id;
                date.SelectedDate = detail.DocumentDate;
                dept.Text = detail.DepartmentName ?? "";
                if (detail.Lines.Count == 0)
                {
                    listStatus.Text = "전표에 품목이 없습니다.";
                    return;
                }

                var first = detail.Lines[0];
                selectedCode = first.ItemCode;
                itemQuery.Text = first.ItemName;
                qty.Text = first.Quantity.ToString(CultureInfo.CurrentCulture);
                lot.Text = first.LotNumber ?? "";
                listStatus.Text = $"전표 {detail.Id} 수정 중 — 등록하면 기존 전표는 삭제되고 새로 저장됩니다.";
                status.Text = listStatus.Text;
            }
            catch (Exception ex)
            {
                listStatus.Text = $"원인: {AppLog.Sanitize(ex.Message)}";
            }
        }

        void DeleteSelected()
        {
            var selected = SelectedRecentRows();
            if (selected.Count == 0)
            {
                listStatus.Text = "목록에서 전표를 고르세요.";
                return;
            }

            var toDelete = selected.Where(d => !d.IsCancelled).ToList();
            if (toDelete.Count == 0)
            {
                listStatus.Text = "이미 삭제된 전표입니다.";
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
                listStatus.Text = AppHost.Run((_, s) =>
                {
                    foreach (var id in ids)
                    {
                        s.DeleteDocument(id);
                    }

                    return ids.Count == 1
                        ? "삭제 완료. 재고는 원래대로 돌아갑니다."
                        : $"{ids.Count}건 삭제 완료. 재고는 원래대로 돌아갑니다.";
                });
                if (editingId is { } editId && ids.Contains(editId))
                {
                    editingId = null;
                }

                ReloadRecent();
            }
            catch (Exception ex)
            {
                listStatus.Text = $"원인: {AppLog.Sanitize(ex.Message)}";
            }
        }

        var save = Primary("등록", (_, _) =>
        {
            PickFirstMatch();
            if (string.IsNullOrWhiteSpace(selectedCode))
            {
                status.Text = "품목을 선택 또는 입력하세요.";
                return;
            }

            if (!decimal.TryParse(qty.Text, CultureInfo.CurrentCulture, out var qv) || qv <= 0)
            {
                status.Text = "수량을 숫자로 입력하세요.";
                return;
            }

            try
            {
                status.Text = AppHost.Run((_, svc) =>
                {
                    if (editingId is { } editId)
                    {
                        svc.DeleteDocument(editId);
                    }

                    int? deptId = null;
                    if (!string.IsNullOrWhiteSpace(dept.Text))
                    {
                        var found = svc.SearchDepartments(dept.Text).FirstOrDefault()
                                    ?? svc.CreateDepartment(dept.Text);
                        deptId = found.Id;
                    }

                    var doc = svc.Issue(date.SelectedDate ?? DateTime.Today, deptId,
                    [
                        new IssueLineRequest
                        {
                            ItemCode = selectedCode,
                            Quantity = qv,
                            LotNumber = string.IsNullOrWhiteSpace(lot.Text) ? null : lot.Text.Trim()
                        }
                    ]);
                    var wasEdit = editingId.HasValue;
                    editingId = null;
                    return wasEdit ? $"수정 저장됨. 전표 {doc.Id}" : $"저장됨. 전표 {doc.Id}";
                });
                listStatus.Text = status.Text;
                ReloadRecent();
            }
            catch (Exception ex)
            {
                status.Text = $"원인: {AppLog.Sanitize(ex.Message)}";
            }
        });

        itemQuery.LostFocus += (_, _) => PickFirstMatch();
        var register = new StackPanel();
        register.Children.Add(FormRow(
            Field("품목", itemQuery),
            Field("출고일", date),
            Field("사용부서", dept),
            Field("수량", qty),
            Field("LOT", lot)));
        register.Children.Add(save);
        register.Children.Add(status);
        grid = TableGrid(rows);
        var list = new StackPanel();
        list.Children.Add(grid);
        var editRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        editRow.Children.Add(Primary("수정", (_, _) => BeginEdit()));
        editRow.Children.Add(Danger("삭제", (_, _) => DeleteSelected()));
        list.Children.Add(editRow);
        list.Children.Add(listStatus);
        Content = new StackPanel
        {
            Children =
            {
                Section("등록", register),
                Section("목록", list)
            }
        };
        ReloadRecent();
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
        var query = Box();
        query.Width = 220;
        var page = 1;
        var all = new List<StockRow>();
        var gridHost = new StackPanel();
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        var pageText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var lotHost = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        DataGrid? grid = null;

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
            lotHost.Children.Add(new TextBlock { Text = "LOT 상세 (붉은 배경은 유효기간이 지난 LOT입니다)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            var lotGrid = TableGrid(lots, ("LOT", "LOT"), ("잔량", "잔량"), ("유효기간", "유효기간"), ("남은일", "남은일"));
            lotGrid.LoadingRow += (_, e) =>
            {
                if (e.Row.Item is LotRow row && row.IsExpired)
                {
                    e.Row.Background = new SolidColorBrush(Color.FromRgb(252, 235, 233));
                }
            };
            lotHost.Children.Add(lotGrid);
        }

        void Render()
        {
            var pages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)UiLayout.PageSize));
            page = Math.Clamp(page, 1, pages);
            var slice = all.Skip((page - 1) * UiLayout.PageSize).Take(UiLayout.PageSize).ToList();
            count.Text = $"총 {all.Count:N0}건";
            pageText.Text = $"{page}/{pages}";
            var next = TableGrid(slice,
                ("품목", nameof(StockRow.품목)),
                ("코드", nameof(StockRow.코드)),
                ("현재고", nameof(StockRow.현재고)),
                ("상태", nameof(StockRow.상태)));
            next.LoadingRow += (_, e) =>
            {
                if (e.Row.Item is StockRow row && row.Kind is StockStatusKind.OutOfStock)
                {
                    e.Row.Background = new SolidColorBrush(Color.FromRgb(252, 235, 233));
                }
                else if (e.Row.Item is StockRow warn && warn.Kind is StockStatusKind.Reorder or StockStatusKind.Expiring)
                {
                    e.Row.Background = new SolidColorBrush(Color.FromRgb(255, 244, 224));
                }
            };
            next.SelectionChanged += (_, _) =>
            {
                if (next.SelectedItem is StockRow row)
                {
                    ShowLots(row.코드);
                }
            };
            gridHost.Children.Clear();
            gridHost.Children.Add(next);
            grid = next;
        }

        void Reload()
        {
            using var db = AppHost.OpenDb();
            var svc = new InventoryService(db, AppHost.Actor);
            var snaps = svc.SearchStockSnapshots(query.Text.Trim());
            all = snaps.Select(s => new StockRow
            {
                품목 = s.Name,
                코드 = s.Code,
                현재고 = s.OnHand?.ToString("N3") ?? "미설정",
                상태 = StatusKo(s.Status),
                Kind = s.Status
            }).ToList();
            page = 1;
            lotHost.Children.Clear();
            Render();
        }

        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        chips.Children.Add(Btn("전체", (_, _) => Reload()));

        var filters = new StackPanel();
        filters.Children.Add(FormRow(Field("품목", query)));
        var apply = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        apply.Children.Add(Primary("적용", (_, _) => Reload()));
        apply.Children.Add(Btn("초기화", (_, _) =>
        {
            query.Text = "";
            Reload();
        }));
        filters.Children.Add(apply);
        filters.Children.Add(chips);

        var listBody = new StackPanel();
        listBody.Children.Add(gridHost);
        listBody.Children.Add(Pager(count, pageText, () => { page--; Render(); }, () => { page++; Render(); }));
        listBody.Children.Add(lotHost);

        Content = new StackPanel
        {
            Children =
            {
                Section("검색·필터", filters),
                Section("목록", listBody)
            }
        };
        Reload();
    }
}

public sealed class StatsView : WorkspaceView
{
    public StatsView()
    {
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
            gridHost.Children.Add(TableGrid(display, ("기간", "기간"), ("구분", "구분"), ("사용", "사용"), ("입고", "입고"), ("구매금액", "구매금액")));
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
        list.Children.Add(gridHost);
        Content = new StackPanel
        {
            Children =
            {
                Section("검색·필터", filters),
                Section("목록", list)
            }
        };
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

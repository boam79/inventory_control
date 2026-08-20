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

public abstract class WorkspaceView : UserControl
{
    protected static TextBox Box(string text = "") => new() { Text = text, MinWidth = 88, MaxWidth = 220, Margin = new Thickness(4) };
    protected static DatePicker Date(DateTime? value = null) => new() { SelectedDate = value ?? DateTime.Today, Margin = new Thickness(4) };
    protected static Button Btn(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Margin = new Thickness(4), Padding = new Thickness(10, 4, 10, 4), MinWidth = 80 };
        button.Click += click;
        return button;
    }

    protected static TextBlock Title(string text) => new()
    {
        Text = text,
        FontSize = 20,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(32, 42, 54)),
        Margin = new Thickness(0, 0, 0, 8)
    };
    protected static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 8) };

    protected static void Alert(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        MessageBox.Show(message, ProductInfo.DisplayName);
    }

    protected static DataGrid GridOf(object items) => new()
    {
        ItemsSource = items is System.Collections.IEnumerable enumerable ? enumerable : new[] { items },
        AutoGenerateColumns = true,
        IsReadOnly = true,
        MinHeight = 220,
        MaxHeight = 420
    };
}

public sealed class DashboardView : WorkspaceView
{
    public DashboardView()
    {
        using var db = AppHost.OpenDb();
        var svc = new InventoryService(db, AppHost.Actor);
        var today = DateTime.Today;
        var kpi = DashboardMetrics.Build(db, svc, today);
        var months = DashboardMetrics.TrailingMonthlyIssues(db, today, 13);
        var forecast = UsageForecast.Predict(months.Select(s => s.Qty).ToList());

        var panel = new StackPanel();
        panel.Children.Add(Title("대시보드"));
        if (DemoSeedService.ShouldAutoSeed(db) && AppSession.Current?.Role == UserRole.Administrator)
        {
            var seedButton = Btn("테스트 데이터 생성 (약 2만 건 · 1년치)", (_, _) =>
            {
                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    var message = AppHost.Run((inner, _) =>
                        DemoSeedService.TryAutoSeed(inner, DateTime.Today, AppHost.Actor).Message);
                    Alert(message);
                    var host = Window.GetWindow(this);
                    if (host is MainWindow main)
                    {
                        main.ShowDashboard();
                    }
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            });
            seedButton.FontSize = 16;
            seedButton.Padding = new Thickness(18, 12, 18, 12);
            seedButton.MinHeight = 48;
            seedButton.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(seedButton);
            panel.Children.Add(Note("입고·사용 거래가 없습니다. 이 버튼을 누르면 대시보드·예측용 가상 데이터가 생깁니다. 실제 거래가 있으면 자동으로 넣지 않습니다."));
        }
        else
        {
            panel.Children.Add(Note("발주 필요·품절은 강조 색입니다. 예측은 참고용이며 자동 발주하지 않습니다."));
        }
        var cards = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        cards.Children.Add(KpiCard("사용 품목", kpi.ActiveItems.ToString("N0")));
        cards.Children.Add(KpiCard("발주 필요", kpi.ReorderItems.ToString("N0"), warn: kpi.ReorderItems > 0));
        cards.Children.Add(KpiCard("품절", kpi.OutOfStockItems.ToString("N0"), danger: kpi.OutOfStockItems > 0));
        cards.Children.Add(KpiCard("유효기간 임박", kpi.ExpiringLots.ToString("N0"), warn: kpi.ExpiringLots > 0));
        cards.Children.Add(KpiCard("당월 구매", kpi.MonthPurchaseAmount.ToString("N0") + "원"));
        cards.Children.Add(KpiCard("당월 사용", kpi.MonthIssueQty.ToString("N3")));
        cards.Children.Add(KpiCard("당월 폐기", kpi.MonthDisposalQty.ToString("N3")));
        panel.Children.Add(cards);
        panel.Children.Add(new TextBlock
        {
            Text = forecast.Available
                ? $"예측({forecast.ModelName}): {string.Join(", ", forecast.Future.Select(v => v.ToString("N2")))}  — 막대=실제, 점선=예측. 연도가 다른 같은 월은 따로 그립니다."
                : forecast.Warning,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var labels = months.Select(s => $"{s.Year}-{s.Month:00}").Concat(["예측+1", "예측+2", "예측+3"]).ToArray();
        var actualValues = months.Select(s => (double)s.Qty).Concat([double.NaN, double.NaN, double.NaN]).ToArray();
        var predictedValues = Enumerable.Repeat(double.NaN, months.Count)
            .Concat(forecast.Available ? forecast.Future.Select(v => (double)v) : new[] { double.NaN, double.NaN, double.NaN })
            .ToArray();
        var gridPaint = new SolidColorPaint(new SKColor(220, 224, 228)) { StrokeThickness = 1 };
        var actual = new ColumnSeries<double>
        {
            Name = "실제 사용",
            Values = actualValues,
            Fill = new SolidColorPaint(new SKColor(61, 90, 115, 200))
        };
        var predicted = new LineSeries<double>
        {
            Name = "예측",
            Values = predictedValues,
            Fill = null,
            GeometrySize = 6,
            Stroke = new SolidColorPaint(new SKColor(176, 58, 46), 2)
            {
                PathEffect = new LiveChartsCore.SkiaSharpView.Painting.Effects.DashEffect([6, 4])
            }
        };
        panel.Children.Add(new CartesianChart
        {
            Height = 340,
            LegendPosition = LiveChartsCore.Measure.LegendPosition.Bottom,
            Series = new ISeries[] { actual, predicted },
            XAxes =
            [
                new Axis
                {
                    Labels = labels,
                    Name = "연월",
                    SeparatorsPaint = gridPaint,
                    TextSize = 11
                }
            ],
            YAxes =
            [
                new Axis
                {
                    Name = "사용 수량",
                    MinLimit = 0,
                    SeparatorsPaint = gridPaint
                }
            ]
        });
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static Border KpiCard(string label, string value, bool warn = false, bool danger = false)
    {
        Brush accent = danger
            ? new SolidColorBrush(Color.FromRgb(176, 58, 46))
            : warn
                ? new SolidColorBrush(Color.FromRgb(196, 123, 22))
                : new SolidColorBrush(Color.FromRgb(61, 90, 115));
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 222, 228)),
            BorderThickness = new Thickness(1, 1, 1, 4),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 10, 10),
            MinWidth = 148,
            MinHeight = 72
        };
        if (danger || warn)
        {
            card.BorderBrush = accent;
        }

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = danger || warn ? accent : new SolidColorBrush(Color.FromRgb(32, 42, 54))
        });
        card.Child = stack;
        return card;
    }
}

public sealed class ReceiveView : WorkspaceView
{
    public ReceiveView()
    {
        var date = Date();
        var supplier = Box();
        var docNo = Box();
        var code = Box();
        var qty = Box("1");
        var price = Box("0");
        var lot = Box();
        var expiry = Date(DateTime.Today.AddDays(180));
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var form = new StackPanel();
        form.Children.Add(Title("입고 등록"));
        form.Children.Add(Note("수량·단가·LOT·유효기간을 키보드로 입력하세요. 증빙번호가 같으면 경고만 하고 저장합니다."));
        form.Children.Add(Row("입고일", date, "공급업체", supplier, "증빙번호", docNo));
        form.Children.Add(Row("품목코드", code, "수량", qty, "단가", price));
        form.Children.Add(Row("LOT", lot, "유효기간", expiry));
        form.Children.Add(Btn("입고 저장", (_, _) =>
        {
            status.Text = AppHost.Run((db, svc) =>
            {
                int? supplierId = null;
                if (!string.IsNullOrWhiteSpace(supplier.Text))
                {
                    var found = svc.SearchSuppliers(supplier.Text).FirstOrDefault()
                                ?? svc.CreateSupplier(supplier.Text);
                    supplierId = found.Id;
                }

                var doc = svc.Receive(date.SelectedDate ?? DateTime.Today, supplierId, string.IsNullOrWhiteSpace(docNo.Text) ? null : docNo.Text,
                [
                    new ReceiptLineRequest
                    {
                        ItemCode = code.Text.Trim(),
                        Quantity = decimal.Parse(qty.Text, CultureInfo.CurrentCulture),
                        UnitPrice = decimal.Parse(price.Text, CultureInfo.CurrentCulture),
                        LotNumber = lot.Text.Trim(),
                        ExpiryDate = expiry.SelectedDate
                    }
                ]);
                return doc.DuplicateWarning
                    ? "저장됨. 경고: 같은 공급업체·증빙번호 입고가 이미 있습니다."
                    : $"저장됨. 전표 {doc.Id}";
            });
        }));
        form.Children.Add(status);
        Content = new ScrollViewer { Content = form };
    }

    private static StackPanel Row(params object[] parts)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var part in parts)
        {
            if (part is string label)
            {
                row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
            }
            else if (part is UIElement el)
            {
                row.Children.Add(el);
            }
        }

        return row;
    }
}

public sealed class IssueView : WorkspaceView
{
    public IssueView()
    {
        var date = Date();
        var dept = Box();
        var code = Box();
        var qty = Box("1");
        var lot = Box();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var form = new StackPanel();
        form.Children.Add(Title("사용 등록"));
        form.Children.Add(Note("재고는 의원 단일 창고입니다. 부서는 사용 기록만 남깁니다. LOT를 비우면 FEFO입니다."));
        form.Children.Add(ReceiveViewRow(date, dept, code, qty, lot));
        form.Children.Add(Btn("사용 저장", (_, _) =>
        {
            status.Text = AppHost.Run((db, svc) =>
            {
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
                        ItemCode = code.Text.Trim(),
                        Quantity = decimal.Parse(qty.Text, CultureInfo.CurrentCulture),
                        LotNumber = string.IsNullOrWhiteSpace(lot.Text) ? null : lot.Text.Trim()
                    }
                ]);
                return $"저장됨. 전표 {doc.Id}, 원가 스냅샷 {doc.Lines.First().UnitCostSnapshot:N2}";
            });
        }));
        form.Children.Add(status);
        Content = new ScrollViewer { Content = form };
    }

    private static StackPanel ReceiveViewRow(DatePicker date, TextBox dept, TextBox code, TextBox qty, TextBox lot)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "사용일", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        row.Children.Add(date);
        row.Children.Add(new TextBlock { Text = "부서", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        row.Children.Add(dept);
        row.Children.Add(new TextBlock { Text = "품목코드", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        row.Children.Add(code);
        row.Children.Add(new TextBlock { Text = "수량", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        row.Children.Add(qty);
        row.Children.Add(new TextBlock { Text = "LOT(선택)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        row.Children.Add(lot);
        return row;
    }
}

public sealed class StockView : WorkspaceView
{
    public StockView()
    {
        var query = Box();
        var host = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(Title("재고현황"));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "검색", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        row.Children.Add(query);
        DataGrid? grid = null;
        void Reload()
        {
            using var db = AppHost.OpenDb();
            var svc = new InventoryService(db, AppHost.Actor);
            var rows = svc.SearchStockSnapshots(query.Text.Trim()).Select(s => new
            {
                s.Code,
                s.Name,
                OnHand = s.OnHand?.ToString("N3") ?? "미설정",
                Status = s.Status.ToString()
            }).ToList();
            var next = GridOf(rows);
            if (grid is null)
            {
                grid = next;
                DockPanel.SetDock(top, Dock.Top);
                host.Children.Add(top);
                host.Children.Add(grid);
            }
            else
            {
                host.Children.Remove(grid);
                grid = next;
                host.Children.Add(grid);
            }
        }

        row.Children.Add(Btn("조회", (_, _) => Reload()));
        top.Children.Add(row);
        Content = host;
        Reload();
    }
}

public sealed class LedgerView : WorkspaceView
{
    public LedgerView()
    {
        var idBox = Box();
        var reason = Box("입력 오류");
        var adjCode = Box();
        var adjLot = Box();
        var adjQty = Box("-1");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        using var db = AppHost.OpenDb();
        var svc = new InventoryService(db, AppHost.Actor);
        var rows = svc.ListDocuments().Select(d => new
        {
            d.Id,
            Type = d.Type.ToString(),
            d.DocumentDate,
            d.DocumentNo,
            d.IsCancelled,
            Lines = d.Lines.Count
        }).ToList();
        var panel = new StackPanel();
        panel.Children.Add(Title("거래내역"));
        panel.Children.Add(Note("취소는 반대 처리만 합니다. 원 행을 물리 삭제하지 않습니다."));
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(new TextBlock { Text = "전표번호", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        tools.Children.Add(idBox);
        tools.Children.Add(new TextBlock { Text = "사유", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        tools.Children.Add(reason);
        tools.Children.Add(Btn("취소", (_, _) =>
        {
            status.Text = AppHost.Run((_, s) =>
            {
                s.CancelDocument(int.Parse(idBox.Text, CultureInfo.CurrentCulture), reason.Text);
                return "취소(반대 처리) 완료. 화면을 다시 여세요.";
            });
        }));
        var adj = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        adj.Children.Add(new TextBlock { Text = "조정 품목", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        adj.Children.Add(adjCode);
        adj.Children.Add(new TextBlock { Text = "LOT", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        adj.Children.Add(adjLot);
        adj.Children.Add(new TextBlock { Text = "증감", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        adj.Children.Add(adjQty);
        adj.Children.Add(Btn("반품·폐기·조정", (_, _) =>
        {
            status.Text = AppHost.Run((_, s) =>
            {
                s.Adjust(
                    DateTime.Today,
                    adjCode.Text.Trim(),
                    adjLot.Text.Trim(),
                    decimal.Parse(adjQty.Text, CultureInfo.CurrentCulture),
                    AdjustmentType.Disposal,
                    reason.Text);
                return "조정 전표를 저장했습니다. 원 입고 행은 그대로입니다.";
            });
        }));
        panel.Children.Add(tools);
        panel.Children.Add(adj);
        panel.Children.Add(status);
        panel.Children.Add(GridOf(rows));
        Content = new ScrollViewer { Content = panel };
    }
}

public sealed class LotsView : WorkspaceView
{
    public LotsView()
    {
        using var db = AppHost.OpenDb();
        var rows = db.Lots.Select(l => new
        {
            Item = l.Item.Code,
            l.LotNumber,
            l.ReceivedDate,
            l.ExpiryDate,
            l.Quantity
        }).OrderBy(l => l.ExpiryDate).ToList();
        var panel = new StackPanel();
        panel.Children.Add(Title("LOT·유효기간"));
        panel.Children.Add(GridOf(rows));
        Content = panel;
    }
}

public sealed class ReorderView : WorkspaceView
{
    public ReorderView()
    {
        using var db = AppHost.OpenDb();
        var svc = new InventoryService(db, AppHost.Actor);
        var rows = svc.ReorderItems().Select(i => new { i.Code, i.Name, OnHand = svc.GetOnHand(i.Code), i.MinStock }).ToList();
        var panel = new StackPanel();
        panel.Children.Add(Title("발주 필요 품목"));
        panel.Children.Add(Note("예측·AI가 발주를 자동 확정하지 않습니다. 이 목록은 현재고와 최소재고만 봅니다."));
        panel.Children.Add(GridOf(rows));
        Content = panel;
    }
}

public sealed class StatsView : WorkspaceView
{
    public StatsView()
    {
        var year = Box(DateTime.Today.Year.ToString(CultureInfo.InvariantCulture));
        var month = Box(DateTime.Today.Month.ToString(CultureInfo.InvariantCulture));
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        var panel = new StackPanel();
        panel.Children.Add(Title("통계·보고서"));
        panel.Children.Add(Note("연도와 월을 따로 집계합니다. Excel의 '3월' 합산처럼 연도를 섞지 않습니다."));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "연도", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        row.Children.Add(year);
        row.Children.Add(new TextBlock { Text = "월", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        row.Children.Add(month);
        row.Children.Add(Btn("조회", (_, _) =>
        {
            using var db = AppHost.OpenDb();
            var svc = new InventoryService(db, AppHost.Actor);
            var y = int.Parse(year.Text, CultureInfo.InvariantCulture);
            var m = int.Parse(month.Text, CultureInfo.InvariantCulture);
            var qty = svc.UsageQuantity(y, m);
            var forecast = UsageForecast.Predict(Enumerable.Range(1, 12).Select(mm => svc.UsageQuantity(y, mm)).ToList());
            result.Text = $"{y}년 {m}월 사용수량: {qty:N3}\n예측: {(forecast.Available ? forecast.ModelName + " " + string.Join(", ", forecast.Future) : forecast.Warning)}";
        }));
        panel.Children.Add(row);
        panel.Children.Add(result);
        Content = panel;
    }
}

public sealed class CloseView : WorkspaceView
{
    public CloseView()
    {
        var year = Box(DateTime.Today.Year.ToString(CultureInfo.InvariantCulture));
        var month = Box(DateTime.Today.Month.ToString(CultureInfo.InvariantCulture));
        var reason = Box();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel();
        panel.Children.Add(Title("월 마감"));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(year);
        row.Children.Add(month);
        row.Children.Add(Btn("마감", (_, _) =>
        {
            status.Text = AppHost.Run((_, svc) =>
            {
                svc.CloseMonth(int.Parse(year.Text, CultureInfo.InvariantCulture), int.Parse(month.Text, CultureInfo.InvariantCulture));
                return "마감되었습니다. 해당 월 입고·사용은 저장되지 않습니다.";
            });
        }));
        row.Children.Add(reason);
        row.Children.Add(Btn("마감 해제", (_, _) =>
        {
            status.Text = AppHost.Run((_, svc) =>
            {
                svc.ReopenMonth(
                    int.Parse(year.Text, CultureInfo.InvariantCulture),
                    int.Parse(month.Text, CultureInfo.InvariantCulture),
                    reason.Text);
                return "마감이 해제되었습니다.";
            });
        }));
        panel.Children.Add(row);
        panel.Children.Add(Note("해제 사유는 필수입니다."));
        panel.Children.Add(status);
        Content = panel;
    }
}

public sealed class MastersView : WorkspaceView
{
    public MastersView()
    {
        var code = Box();
        var name = Box();
        var category = Box("소모품");
        var unit = Box("개");
        var min = Box("0");
        var lot = Box("OPEN");
        var qty = Box("0");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel();
        panel.Children.Add(Title("기준정보"));
        panel.Children.Add(Note("거래가 있는 품목은 삭제할 수 없고 사용중지합니다. 초기재고를 비우면 0이 아니라 미설정입니다."));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in new UIElement[] { Label("코드"), code, Label("이름"), name, Label("분류"), category, Label("단위"), unit, Label("최소"), min })
        {
            row.Children.Add(child);
        }

        panel.Children.Add(row);
        panel.Children.Add(Btn("품목 등록", (_, _) =>
        {
            status.Text = AppHost.Run((_, svc) =>
            {
                svc.CreateItem(code.Text.Trim(), name.Text.Trim(), category.Text, unit.Text, unit.Text, decimal.Parse(min.Text, CultureInfo.CurrentCulture));
                return "품목이 등록되었고 초기재고는 미설정입니다.";
            });
        }));
        var openRow = new StackPanel { Orientation = Orientation.Horizontal };
        openRow.Children.Add(Label("초기 LOT"));
        openRow.Children.Add(lot);
        openRow.Children.Add(Label("수량"));
        openRow.Children.Add(qty);
        openRow.Children.Add(Btn("초기재고 입력", (_, _) =>
        {
            status.Text = AppHost.Run((_, svc) =>
            {
                svc.SaveOpeningDraft(code.Text.Trim(), lot.Text.Trim(), decimal.Parse(qty.Text, CultureInfo.CurrentCulture), DateTime.Today, DateTime.Today.AddDays(365));
                return "초기재고를 입력 중 상태로 저장했습니다.";
            });
        }));
        openRow.Children.Add(Btn("초기재고 확정", (_, _) =>
        {
            status.Text = AppHost.Run((_, svc) =>
            {
                svc.ConfirmOpening(code.Text.Trim());
                return "초기재고를 확정했습니다.";
            });
        }));
        openRow.Children.Add(Btn("사용중지", (_, _) =>
        {
            status.Text = AppHost.Run((_, svc) =>
            {
                svc.DeactivateItem(code.Text.Trim());
                return "사용중지했습니다.";
            });
        }));
        panel.Children.Add(openRow);
        var extra = new StackPanel { Orientation = Orientation.Horizontal };
        var dept = Box("외래");
        var supplier = Box();
        extra.Children.Add(Label("부서"));
        extra.Children.Add(dept);
        extra.Children.Add(Btn("부서 추가", (_, _) =>
        {
            status.Text = AppHost.Run((_, svc) =>
            {
                svc.CreateDepartment(dept.Text);
                return "부서를 추가했습니다.";
            });
        }));
        extra.Children.Add(Label("공급업체"));
        extra.Children.Add(supplier);
        extra.Children.Add(Btn("업체 추가", (_, _) =>
        {
            status.Text = AppHost.Run((_, svc) =>
            {
                svc.CreateSupplier(supplier.Text);
                return "공급업체를 추가했습니다.";
            });
        }));
        panel.Children.Add(extra);
        panel.Children.Add(status);
        using var db = AppHost.OpenDb();
        panel.Children.Add(GridOf(db.Items.Select(i => new { i.Code, i.Name, i.OpeningStatus, i.IsActive, i.MinStock }).ToList()));
        Content = new ScrollViewer { Content = panel };
    }

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) };
}

public sealed class UsersView : WorkspaceView
{
    public UsersView()
    {
        var user = Box();
        var pass = new PasswordBox { MinWidth = 140, Margin = new Thickness(4) };
        var role = new ComboBox
        {
            ItemsSource = Enum.GetValues<UserRole>(),
            SelectedItem = UserRole.Viewer,
            MinWidth = 160,
            Margin = new Thickness(4)
        };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel();
        panel.Children.Add(Title("사용자·권한"));
        panel.Children.Add(Note("비밀번호는 해시로만 저장합니다. 평문을 남기지 않습니다."));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(user);
        row.Children.Add(pass);
        row.Children.Add(role);
        row.Children.Add(Btn("사용자 추가", (_, _) =>
        {
            if (AppSession.Current?.Permissions.CanManageUsers != true)
            {
                status.Text = "원인: 사용자 관리 권한이 없습니다.\n조치: 관리자에게 요청하세요.";
                return;
            }

            status.Text = AppHost.Run((db, _) =>
            {
                new AuthenticationService(db).CreateUser(user.Text.Trim(), pass.Password, (UserRole)role.SelectedItem);
                return "사용자를 추가했습니다.";
            });
        }));
        panel.Children.Add(row);
        panel.Children.Add(status);
        using var db = AppHost.OpenDb();
        panel.Children.Add(GridOf(db.Users.Select(u => new { u.UserName, u.Role, u.IsActive }).ToList()));
        Content = panel;
    }
}

public sealed class BackupView : WorkspaceView
{
    public BackupView()
    {
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel();
        panel.Children.Add(Title("백업·복원"));
        panel.Children.Add(Note("복원 전에 현재 DB를 .pre-restore 로 복사합니다. Excel 가져오기는 마스터만 / 마스터+기초 / 전체이력입니다. 빈 수식 행은 거래가 아닙니다. 기초와 이력을 같이 넣으면 이중계산 경고 후 기초는 건너뜁니다."));
        panel.Children.Add(Btn("지금 백업", (_, _) =>
        {
            var dlg = new SaveFileDialog { Filter = "SQLite (*.db)|*.db", FileName = $"inventory-{DateTime.Today:yyyyMMdd}.db" };
            if (dlg.ShowDialog() == true)
            {
                BackupService.Backup(AppHost.DatabasePath, dlg.FileName);
                status.Text = "백업했습니다.";
            }
        }));
        panel.Children.Add(Btn("복원", (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "SQLite (*.db)|*.db" };
            if (dlg.ShowDialog() == true)
            {
                BackupService.Restore(dlg.FileName, AppHost.DatabasePath);
                status.Text = "복원했습니다. 프로그램을 다시 로그인하세요.";
            }
        }));
        panel.Children.Add(Btn("Excel 내보내기", (_, _) =>
        {
            var dlg = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "재고내보내기.xlsx" };
            if (dlg.ShowDialog() == true)
            {
                using var db = AppHost.OpenDb();
                ExcelCatalog.ExportStock(db, dlg.FileName);
                status.Text = "내보냈습니다.";
            }
        }));
        panel.Children.Add(Btn("Excel 가져오기(마스터만)", (_, _) => Import(ImportMode.MasterOnly, status)));
        panel.Children.Add(Btn("Excel 가져오기(마스터+기초)", (_, _) => Import(ImportMode.MasterAndOpening, status)));
        panel.Children.Add(Btn("Excel 가져오기(전체이력)", (_, _) => Import(ImportMode.FullHistory, status)));
        panel.Children.Add(status);
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static void Import(ImportMode mode, TextBlock status)
    {
        var dlg = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx" };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        var preview = ExcelCatalog.PreviewMaster(dlg.FileName);
        if (MessageBox.Show($"미리보기: 품목 {preview.ItemCodes.Count}개, 빈 행 skip {preview.EmptyRowsSkipped}. 가져올까요?", ProductInfo.DisplayName, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            return;
        }

        using var db = AppHost.OpenDb();
        var result = ExcelCatalog.Import(db, dlg.FileName, mode);
        status.Text = $"가져옴: 품목 {result.ImportedItems}, 거래 {result.TransactionRows}, 기초확정 {result.OpeningConfirmed}";
        if (result.DoubleCountWarning)
        {
            status.Text += "\n" + result.Warning;
        }
    }
}

public sealed class SettingsView : WorkspaceView
{
    public SettingsView()
    {
        using var db = AppHost.OpenDb();
        var store = new SettingsStore(db);
        var days = Box(store.Get(SettingsStore.ExpiryWarningDays, "90"));
        var backup = Box(store.Get(SettingsStore.BackupFolder, global::System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpringClinicInventory",
            "backups")));
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel();
        panel.Children.Add(Title("환경설정"));
        panel.Children.Add(new TextBlock { Text = $"앱 버전 {ProductInfo.DisplayName} {ProductInfo.Version}" });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "유효기간 경고일", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        row.Children.Add(days);
        row.Children.Add(new TextBlock { Text = "백업 폴더", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        row.Children.Add(backup);
        row.Children.Add(Btn("저장", (_, _) =>
        {
            using var inner = AppHost.OpenDb();
            var s = new SettingsStore(inner);
            s.Set(SettingsStore.ExpiryWarningDays, days.Text.Trim());
            s.Set(SettingsStore.BackupFolder, backup.Text.Trim());
            status.Text = "저장했습니다.";
        }));
        panel.Children.Add(row);
        if (AppSession.Current?.Role == UserRole.Administrator)
        {
            panel.Children.Add(Note("테스트 데이터는 개발·예측 확인용입니다. 운영 의원 DB에서는 만들지 마세요. 기존 거래를 지우지는 않습니다."));
            panel.Children.Add(Btn("테스트 데이터 생성 (약 2만 건)", (_, _) =>
            {
                if (MessageBox.Show(
                        "오늘 기준 직전 12~13개월, 약 2만 건의 가상 입고·사용을 추가합니다.\n운영 데이터면 '아니오'를 누르세요.",
                        ProductInfo.DisplayName,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }

                status.Text = AppHost.Run((db, _) =>
                {
                    var existing = DemoSeedService.CountBusinessDocuments(db);
                    if (existing >= DemoSeedService.BusyThreshold)
                    {
                        var again = MessageBox.Show(
                            $"이미 거래 {existing}건이 있습니다. 그래도 추가할까요? 기존 데이터는 삭제하지 않습니다.",
                            ProductInfo.DisplayName,
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);
                        if (again != MessageBoxResult.Yes)
                        {
                            return "테스트 데이터 생성을 취소했습니다.";
                        }

                        return DemoSeedService.Generate(db, DateTime.Today, force: true).Message;
                    }

                    return DemoSeedService.Generate(db, DateTime.Today, force: false).Message;
                });
            }));
        }

        panel.Children.Add(Btn("업데이트 확인", async (_, _) =>
        {
            status.Text = await VelopackUpdater.CheckAndDownloadAsync();
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = UpdateChecker.ReleasesUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                status.Text += $"\n브라우저에서 {UpdateChecker.ReleasesUrl} 를 열어 Setup.exe를 받으세요.";
            }
        }));
        panel.Children.Add(status);
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}

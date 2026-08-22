using Inventory.Core;
using Inventory.Infrastructure;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using Microsoft.Win32;
using SkiaSharp;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Inventory.App.Views;

public abstract class WorkspaceView : UserControl
{
    protected static TextBox Box(string text = "") => new() { Text = text, MinWidth = 160, Width = 180 };
    protected static DatePicker Date(DateTime? value = null) => new() { SelectedDate = value ?? DateTime.Today, Width = 160 };

    protected static Button Styled(string text, string styleKey, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0) };
        if (Application.Current?.TryFindResource(styleKey) is Style style)
        {
            button.Style = style;
        }

        button.Click += click;
        return button;
    }

    protected static Button Btn(string text, RoutedEventHandler click) => Styled(text, "GhostButton", click);
    protected static Button Primary(string text, RoutedEventHandler click) => Styled(text, "PrimaryButton", click);
    protected static Button Danger(string text, RoutedEventHandler click) => Styled(text, "DangerButton", click);

    protected static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 8) };

    protected static void Alert(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        MessageBox.Show(message, ProductInfo.DisplayName);
    }

    protected static StackPanel Field(string label, UIElement input)
    {
        if (input is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Left;
            fe.Margin = new Thickness(0);
        }

        var stack = new StackPanel { Margin = new Thickness(0, 0, 16, 12) };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4)
        });
        stack.Children.Add(input);
        return stack;
    }

    protected static Border Section(string title, UIElement body, UIElement? extra = null)
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        if (extra is not null)
        {
            DockPanel.SetDock(extra, Dock.Right);
            head.Children.Add(extra);
        }

        head.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 234)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12),
            Child = stack
        };
    }

    protected static WrapPanel FormRow(params UIElement[] fields)
    {
        var row = new WrapPanel();
        foreach (var field in fields)
        {
            row.Children.Add(field);
        }

        return row;
    }

    protected static DataGrid TableGrid(object items, params (string Header, string Binding)[] columns)
        => TableGrid(items, allowMultiSelect: false, columns);

    protected static DataGrid TableGrid(object items, bool allowMultiSelect, params (string Header, string Binding)[] columns)
    {
        var grid = new DataGrid
        {
            ItemsSource = items is System.Collections.IEnumerable enumerable ? enumerable : new[] { items },
            AutoGenerateColumns = columns.Length == 0,
            MaxHeight = UiLayout.ListMaxHeight,
            SelectionMode = allowMultiSelect ? DataGridSelectionMode.Extended : DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow
        };
        foreach (var column in columns)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Header,
                Binding = new System.Windows.Data.Binding(column.Binding),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                IsReadOnly = true
            });
        }

        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        return grid;
    }

    protected static DockPanel Pager(TextBlock count, TextBlock pageText, Action prev, Action next)
    {
        var bar = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Btn("이전", (_, _) => prev()));
        buttons.Children.Add(Btn("다음", (_, _) => next()));
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(count);
        left.Children.Add(pageText);
        bar.Children.Add(left);
        return bar;
    }

    protected static Border ItemCard(string title, string meta, Action? click = null, string? badge = null)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 222, 228)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = click is null ? Cursors.Arrow : Cursors.Hand
        };
        var row = new DockPanel();
        if (!string.IsNullOrWhiteSpace(badge))
        {
            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(232, 238, 243)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            chip.Child = new TextBlock { Text = badge, FontSize = 12 };
            DockPanel.SetDock(chip, Dock.Right);
            row.Children.Add(chip);
        }

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(meta))
        {
            text.Children.Add(new TextBlock { Text = meta, FontSize = 12, Foreground = Brushes.DimGray, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
        }

        row.Children.Add(text);
        card.Child = row;
        if (click is not null)
        {
            card.MouseLeftButtonUp += (_, _) => click();
        }

        return card;
    }

    protected static string StatusKo(StockStatusKind status) => status switch
    {
        StockStatusKind.Unset => "미설정",
        StockStatusKind.Reorder => "발주 권장",
        StockStatusKind.OutOfStock => "품절",
        StockStatusKind.Expiring => "유효기간 임박",
        StockStatusKind.Inactive => "사용중지",
        _ => "정상"
    };

    protected static string DocTypeKo(DocumentType type) => type switch
    {
        DocumentType.Receipt => "입고",
        DocumentType.Issue => "출고",
        DocumentType.Adjustment => "조정",
        DocumentType.Opening => "초기재고",
        DocumentType.Reversal => "취소(반대)",
        _ => type.ToString()
    };
}

public sealed class DashboardView : WorkspaceView
{
    private sealed class ItemRow : INotifyPropertyChanged
    {
        private bool _selected;
        public bool 선택
        {
            get => _selected;
            set
            {
                if (_selected == value)
                {
                    return;
                }

                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(선택)));
            }
        }
        public required string 품목 { get; init; }
        public required string 코드 { get; init; }
        public required string 현재고 { get; init; }
        public required string 상태 { get; init; }
        public required StockStatusKind Kind { get; init; }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public DashboardView()
    {
        using var db = AppHost.OpenDb();
        var svc = new InventoryService(db, AppHost.Actor);
        var today = DateTime.Today;
        var kpi = DashboardMetrics.Build(db, svc, today);

        var panel = new StackPanel();
        if (DemoSeedService.ShouldAutoSeed(db) && AppSession.Current?.Role == UserRole.Administrator)
        {
            var seedButton = Btn("테스트 데이터 생성 (품목 1,000 · 1년치)", (_, _) =>
            {
                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    var message = AppHost.Run((inner, _) =>
                        DemoSeedService.TryAutoSeed(inner, DateTime.Today, AppHost.Actor).Message);
                    Alert(message);
                    if (Window.GetWindow(this) is MainWindow main)
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
            panel.Children.Add(Note("입고·출고 거래가 없습니다. 이 버튼을 누르면 대시보드·예측용 가상 데이터가 생깁니다. 실제 거래가 있으면 자동으로 넣지 않습니다."));
        }
        else if (AppSession.Current?.Role == UserRole.Administrator)
        {
            var replaceButton = Btn("샘플 데이터를 다양하게 다시 만들기", (_, _) =>
            {
                if (MessageBox.Show(
                        $"기존 품목·입고·출고를 모두 삭제한 뒤 품목 {DemoSeedService.DefaultItemCount:N0}개를 새로 만듭니다.\n운영 의원 데이터면 '아니오'를 누르세요.",
                        ProductInfo.DisplayName,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }

                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    var message = AppHost.Run((inner, _) =>
                        DemoSeedService.ReplaceSample(inner, DateTime.Today, AppHost.Actor).Message);
                    Alert(message);
                    if (Window.GetWindow(this) is MainWindow main)
                    {
                        main.ShowDashboard();
                    }
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            });
            replaceButton.FontSize = 15;
            replaceButton.Padding = new Thickness(14, 8, 14, 8);
            replaceButton.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(replaceButton);
            panel.Children.Add(Note($"테스트 데이터가 너무 비슷하면 이 버튼으로 다시 만듭니다. 기존 품목·입고·출고는 모두 삭제되고 품목 {DemoSeedService.DefaultItemCount:N0}개가 새로 만들어집니다."));
        }
        else
        {
            panel.Children.Add(Note("표에서 품목을 하나 이상 선택하면 출고 추이를 봅니다. 여러 개를 고르면 함께 비교하고, 한 번에 최대 8개입니다. 예측은 참고용이며 자동 발주하지 않습니다."));
        }

        var cards = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        cards.Children.Add(KpiCard("발주 필요", kpi.ReorderItems.ToString("N0"), warn: kpi.ReorderItems > 0, open: "stock"));
        cards.Children.Add(KpiCard("품절", kpi.OutOfStockItems.ToString("N0"), danger: kpi.OutOfStockItems > 0, open: "stock"));
        cards.Children.Add(KpiCard("유효기간 임박", kpi.ExpiringLots.ToString("N0"), warn: kpi.ExpiringLots > 0, open: "stock"));
        cards.Children.Add(KpiCard("오늘 입·출고", $"{kpi.TodayReceiptDocs + kpi.TodayIssueDocs:N0}", open: "stats"));
        panel.Children.Add(cards);
        panel.Children.Add(new TextBlock
        {
            Text = $"사용 품목 {kpi.ActiveItems:N0} · 당월 구매 {kpi.MonthPurchaseAmount:N0}원 · 당월 출고 {kpi.MonthIssueQty:N3} · 당월 폐기 {kpi.MonthDisposalQty:N3}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        var query = Box();
        query.Width = 220;
        var filter = "all";
        var page = 1;
        var all = new List<ItemRow>();
        var suppressSelect = false;
        var gridHost = new StackPanel();
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        var pageText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var chartHost = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        SKColor[] palette =
        [
            new SKColor(31, 111, 115),
            new SKColor(196, 123, 22),
            new SKColor(61, 90, 115),
            new SKColor(46, 125, 50),
            new SKColor(123, 31, 162),
            new SKColor(176, 58, 46),
            new SKColor(2, 119, 189),
            new SKColor(93, 64, 55)
        ];

        void ShowEmptyChart()
        {
            chartHost.Children.Clear();
            chartHost.Children.Add(new TextBlock
            {
                Text = $"표에서 품목의 「선택」을 켜면 출고 추이가 나옵니다. 여러 개를 고르면 함께 비교합니다. 한 번에 최대 {UiLayout.ChartItemMax}개입니다.",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap
            });
        }

        void UpdateSelectionCount()
        {
            count.Text = $"총 {all.Count:N0}건 · 선택 {all.Count(r => r.선택):N0}개";
        }

        void ShowChart()
        {
            UpdateSelectionCount();
            var picked = all.Where(r => r.선택).Take(UiLayout.ChartItemMax).ToList();
            if (picked.Count == 0)
            {
                ShowEmptyChart();
                return;
            }

            using var inner = AppHost.OpenDb();
            var seriesMap = DashboardMetrics.TrailingMonthlyIssuesByItems(
                inner, DateTime.Today, picked.Select(r => r.코드).ToList());
            var plot = DashboardChartBuilder.Build(
                seriesMap,
                picked.Select(r => (r.코드, r.품목)).ToList());
            var gridPaint = new SolidColorPaint(new SKColor(230, 233, 236)) { StrokeThickness = 1 };
            var combinedLabels = DashboardChartBuilder.CombinedLabels(plot).ToArray();
            var forecastRows = new List<object>();

            chartHost.Children.Clear();
            chartHost.Children.Add(new TextBlock
            {
                Text = picked.Count == 1
                    ? $"{picked[0].품목} ({picked[0].코드}) 월별 사용과 예측"
                    : $"선택한 {picked.Count}개 품목을 품목별 작은 그래프로 봅니다. 품목이 많아도 겹치지 않습니다.",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap
            });
            chartHost.Children.Add(new TextBlock
            {
                Text = "실선·진한 점은 실제 사용, 점선·연한 점은 다음 3개월 예측입니다. 그래프마다 자기 수량 크기로 그려 작은 품목도 잘 보입니다.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 8)
            });
            if (!string.IsNullOrWhiteSpace(plot.Insight))
            {
                chartHost.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(240, 246, 246)),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 0, 10),
                    CornerRadius = new CornerRadius(4),
                    Child = new TextBlock { Text = plot.Insight, TextWrapping = TextWrapping.Wrap }
                });
            }

            var grid = new WrapPanel();
            for (var i = 0; i < plot.Lines.Count; i++)
            {
                var line = plot.Lines[i];
                var color = palette[i % palette.Length];
                var actualValues = DashboardChartBuilder.ActualWithGap(line);
                var forecastValues = DashboardChartBuilder.ForecastWithAnchor(line);
                var lastActual = line.Actual.Count == 0 ? 0 : line.Actual[^1];
                var firstForecast = line.Forecast.Count > 0 ? line.Forecast[0] : double.NaN;
                string trendText;
                Brush trendBrush;
                if (double.IsNaN(firstForecast))
                {
                    trendText = "예측 불가(데이터 부족)";
                    trendBrush = Brushes.DimGray;
                }
                else if (DashboardChartBuilder.IsRising(line))
                {
                    trendText = $"▲ 다음달 예측 {firstForecast:N0}개";
                    trendBrush = new SolidColorBrush(Color.FromRgb(176, 58, 46));
                }
                else if (DashboardChartBuilder.IsFalling(line))
                {
                    trendText = $"▼ 다음달 예측 {firstForecast:N0}개";
                    trendBrush = new SolidColorBrush(Color.FromRgb(31, 111, 115));
                }
                else
                {
                    trendText = $"− 다음달 예측 {firstForecast:N0}개 (비슷)";
                    trendBrush = Brushes.DimGray;
                }

                var miniSeries = new List<ISeries>
                {
                    new LineSeries<double>
                    {
                        Name = "실제",
                        Values = actualValues,
                        Fill = null,
                        GeometrySize = 6,
                        LineSmoothness = 0,
                        GeometryFill = new SolidColorPaint(color),
                        GeometryStroke = new SolidColorPaint(new SKColor(255, 255, 255), 1.5f),
                        Stroke = new SolidColorPaint(color, 2.5f),
                        YToolTipLabelFormatter = point => $"{point.Coordinate.PrimaryValue:N0}개"
                    },
                    new LineSeries<double>
                    {
                        Name = "예측",
                        Values = forecastValues,
                        Fill = null,
                        GeometrySize = 6,
                        LineSmoothness = 0,
                        GeometryFill = new SolidColorPaint(new SKColor(255, 255, 255)),
                        GeometryStroke = new SolidColorPaint(color, 1.5f),
                        Stroke = new SolidColorPaint(color, 2)
                        {
                            PathEffect = new LiveChartsCore.SkiaSharpView.Painting.Effects.DashEffect([5, 4])
                        },
                        YToolTipLabelFormatter = point => $"{point.Coordinate.PrimaryValue:N0}개(예측)"
                    }
                };

                var card = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(224, 228, 232)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 10, 10),
                    Width = 250
                };
                var cardStack = new StackPanel();
                cardStack.Children.Add(new TextBlock
                {
                    Text = $"{line.Name} ({line.Code})",
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 2)
                });
                cardStack.Children.Add(new TextBlock
                {
                    Text = $"최근 달 {lastActual:N0}개",
                    Foreground = Brushes.DimGray,
                    FontSize = 12
                });
                cardStack.Children.Add(new TextBlock
                {
                    Text = trendText,
                    Foreground = trendBrush,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                cardStack.Children.Add(new CartesianChart
                {
                    Height = 130,
                    LegendPosition = LiveChartsCore.Measure.LegendPosition.Hidden,
                    Series = miniSeries,
                    XAxes = [new Axis { Labels = combinedLabels, SeparatorsPaint = gridPaint, TextSize = 9, LabelsRotation = 0 }],
                    YAxes = [new Axis { MinLimit = 0, SeparatorsPaint = gridPaint, TextSize = 9 }]
                });
                card.Child = cardStack;
                grid.Children.Add(card);

                string Qty(int index) =>
                    index < line.Forecast.Count && !double.IsNaN(line.Forecast[index])
                        ? line.Forecast[index].ToString("N0")
                        : "—";
                forecastRows.Add(new
                {
                    품목 = line.Name,
                    코드 = line.Code,
                    예측1 = Qty(0),
                    예측2 = Qty(1),
                    예측3 = Qty(2)
                });
            }

            chartHost.Children.Add(grid);
            chartHost.Children.Add(new TextBlock
            {
                Text = "예측 수량",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 6)
            });
            chartHost.Children.Add(TableGrid(forecastRows, ("품목", "품목"), ("코드", "코드"), ("예측+1", "예측1"), ("예측+2", "예측2"), ("예측+3", "예측3")));
        }

        void OnRowSelectedChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (suppressSelect || e.PropertyName != nameof(ItemRow.선택) || sender is not ItemRow row)
            {
                return;
            }

            if (row.선택 && all.Count(r => r.선택) > UiLayout.ChartItemMax)
            {
                suppressSelect = true;
                row.선택 = false;
                suppressSelect = false;
                Alert($"출고 추이는 한 번에 {UiLayout.ChartItemMax}개까지 비교합니다.");
                return;
            }

            ShowChart();
        }

        void Render()
        {
            var pages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)UiLayout.PageSize));
            page = Math.Clamp(page, 1, pages);
            var slice = all.Skip((page - 1) * UiLayout.PageSize).Take(UiLayout.PageSize).ToList();
            UpdateSelectionCount();
            pageText.Text = $"{page}/{pages}";
            var next = TableGrid(slice,
                ("품목", nameof(ItemRow.품목)),
                ("코드", nameof(ItemRow.코드)),
                ("현재고", nameof(ItemRow.현재고)),
                ("상태", nameof(ItemRow.상태)));
            next.IsReadOnly = false;
            next.CanUserAddRows = false;
            next.Columns.Insert(0, new DataGridTemplateColumn
            {
                Header = "선택",
                Width = 56,
                CellTemplate = CheckCell()
            });
            next.LoadingRow += (_, e) =>
            {
                if (e.Row.Item is ItemRow row && row.Kind is StockStatusKind.OutOfStock)
                {
                    e.Row.Background = new SolidColorBrush(Color.FromRgb(252, 235, 233));
                }
                else if (e.Row.Item is ItemRow warn && warn.Kind is StockStatusKind.Reorder or StockStatusKind.Expiring)
                {
                    e.Row.Background = new SolidColorBrush(Color.FromRgb(255, 244, 224));
                }
            };
            gridHost.Children.Clear();
            gridHost.Children.Add(next);
        }

        DataTemplate CheckCell()
        {
            var factory = new FrameworkElementFactory(typeof(CheckBox));
            factory.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(ItemRow.선택))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            factory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            factory.SetValue(UIElement.FocusableProperty, true);
            factory.AddHandler(CheckBox.ClickEvent, new RoutedEventHandler((sender, _) =>
            {
                if (sender is CheckBox box && box.DataContext is ItemRow row)
                {
                    row.선택 = box.IsChecked == true;
                }
            }));
            return new DataTemplate { VisualTree = factory };
        }

        void Reload()
        {
            var keep = all.Where(r => r.선택).Select(r => r.코드).ToHashSet();
            using var inner = AppHost.OpenDb();
            var innerSvc = new InventoryService(inner, AppHost.Actor);
            var snaps = innerSvc.SearchStockSnapshots(query.Text.Trim());
            var shown = filter switch
            {
                "out" => snaps.Where(r => r.Status == StockStatusKind.OutOfStock),
                "reorder" => snaps.Where(r => r.Status == StockStatusKind.Reorder),
                "unset" => snaps.Where(r => r.Status == StockStatusKind.Unset),
                "exp" => snaps.Where(r => r.Status == StockStatusKind.Expiring),
                _ => snaps
            };
            suppressSelect = true;
            all = shown.Select(s =>
            {
                var row = new ItemRow
                {
                    선택 = keep.Contains(s.Code),
                    품목 = s.Name,
                    코드 = s.Code,
                    현재고 = s.OnHand?.ToString("N3") ?? "미설정",
                    상태 = StatusKo(s.Status),
                    Kind = s.Status
                };
                row.PropertyChanged += OnRowSelectedChanged;
                return row;
            }).ToList();
            suppressSelect = false;
            page = 1;
            Render();
            ShowChart();
        }

        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        void Chip(string id, string label) => chips.Children.Add(Btn(label, (_, _) =>
        {
            filter = id;
            Reload();
        }));
        Chip("all", "전체");
        Chip("out", "품절");
        Chip("reorder", "발주");
        Chip("unset", "미설정");
        Chip("exp", "임박");

        var filters = new StackPanel();
        filters.Children.Add(FormRow(Field("품목", query)));
        var apply = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        apply.Children.Add(Primary("적용", (_, _) => Reload()));
        apply.Children.Add(Btn("초기화", (_, _) =>
        {
            query.Text = "";
            filter = "all";
            Reload();
        }));
        apply.Children.Add(Btn("선택 해제", (_, _) =>
        {
            suppressSelect = true;
            foreach (var row in all)
            {
                row.선택 = false;
            }

            suppressSelect = false;
            Render();
            ShowChart();
        }));
        filters.Children.Add(apply);
        filters.Children.Add(chips);

        var listBody = new StackPanel();
        listBody.Children.Add(gridHost);
        listBody.Children.Add(Pager(count, pageText, () => { page--; Render(); }, () => { page++; Render(); }));

        var root = new StackPanel();
        root.Children.Add(Section("요약", panel));
        root.Children.Add(Section("검색·필터", filters));
        root.Children.Add(Section("품목 목록", listBody));
        root.Children.Add(Section("품목 출고 추이", chartHost));

        Content = root;
        Reload();
    }

    private Border KpiCard(string label, string value, bool warn = false, bool danger = false, string? open = null)
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
            MinHeight = 72,
            Cursor = open is null ? Cursors.Arrow : Cursors.Hand,
            ToolTip = open is null ? null : "클릭하면 해당 목록으로 이동합니다."
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
        if (open is not null)
        {
            card.MouseLeftButtonUp += (_, _) =>
            {
                if (Window.GetWindow(this) is MainWindow main)
                {
                    main.OpenMenu(open);
                }
            };
        }

        return card;
    }
}

public sealed class BackupView : WorkspaceView
{
    public BackupView()
    {
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel();
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
        Content = Section("백업·엑셀", panel);
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
        backup.MaxWidth = 560;
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = $"앱 버전 {ProductInfo.DisplayName} {ProductInfo.Version}", Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(Field("유효기간 경고일", days));
        panel.Children.Add(Field("백업 폴더", backup));
        panel.Children.Add(Btn("저장", (_, _) =>
        {
            using var inner = AppHost.OpenDb();
            var s = new SettingsStore(inner);
            s.Set(SettingsStore.ExpiryWarningDays, days.Text.Trim());
            s.Set(SettingsStore.BackupFolder, backup.Text.Trim());
            status.Text = "저장했습니다.";
        }));
        if (AppSession.Current?.Role == UserRole.Administrator)
        {
            panel.Children.Add(Note($"테스트 데이터는 개발·예측 확인용입니다. 운영 의원 DB에서는 만들지 마세요. 다시 만들면 기존 품목·입고·출고를 모두 지우고 품목 {DemoSeedService.DefaultItemCount:N0}개로 교체합니다."));
            panel.Children.Add(Btn($"테스트 데이터 다시 만들기 (품목 {DemoSeedService.DefaultItemCount:N0})", (_, _) =>
            {
                if (MessageBox.Show(
                        $"기존 품목·입고·출고를 모두 삭제한 뒤 품목 {DemoSeedService.DefaultItemCount:N0}개와 1년치 계절 거래를 새로 만듭니다.\n운영 데이터면 '아니오'를 누르세요.",
                        ProductInfo.DisplayName,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }

                status.Text = AppHost.Run((inner, _) =>
                    DemoSeedService.ReplaceSample(inner, DateTime.Today, AppHost.Actor).Message);
            }));
        }

        panel.Children.Add(Note("업데이트는 상단의 「업데이트」 또는 아래 버튼입니다. 설치본에서만 자동 적용됩니다. 개발 실행(dotnet run)에서는 Setup.exe를 받아 설치하세요."));
        var progressBar = new ProgressBar
        {
            Height = 16,
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 8, 0, 8),
            Visibility = Visibility.Collapsed
        };
        Button? updateBtn = null;
        updateBtn = Btn("업데이트", async (_, _) =>
        {
            if (updateBtn is not null)
            {
                updateBtn.IsEnabled = false;
            }

            progressBar.Visibility = Visibility.Visible;
            progressBar.Value = 0;
            status.Text = "업데이트를 준비하는 중...";
            try
            {
                var progress = new Progress<string>(text =>
                {
                    status.Text = text;
                    var digits = System.Text.RegularExpressions.Regex.Match(text ?? "", @"(\d+)\s*%");
                    if (digits.Success && int.TryParse(digits.Groups[1].Value, out var pct))
                    {
                        progressBar.Value = Math.Clamp(pct, 0, 100);
                    }
                });
                status.Text = await VelopackUpdater.ApplyFromButtonAsync(progress, applyAndRestart: true);
            }
            catch (Exception ex)
            {
                status.Text = $"원인: {AppLog.Sanitize(ex.Message)}\n조치: 입고·출고는 계속하세요. {UpdateChecker.ReleasesUrl}";
            }
            finally
            {
                if (updateBtn is not null)
                {
                    updateBtn.IsEnabled = true;
                }

                if (progressBar.Value < 100)
                {
                    progressBar.Visibility = Visibility.Collapsed;
                }
            }
        });
        panel.Children.Add(updateBtn);
        panel.Children.Add(progressBar);
        panel.Children.Add(status);
        Content = Section("환경설정", panel);
    }
}

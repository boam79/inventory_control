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
    protected enum ColumnAlign { Left, Right }

    protected sealed record ColumnSpec(
        string Header,
        string Binding,
        ColumnAlign Align = ColumnAlign.Left,
        double? Width = null,
        bool IsStatus = false);

    protected static Brush ResourceBrush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

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

    protected static (Border Banner, TextBlock Label) CreatePageBanner()
    {
        var label = new TextBlock { TextWrapping = TextWrapping.Wrap };
        return (UiComponents.PageStatusBanner(label), label);
    }

    protected static void SetBanner((Border Banner, TextBlock Label) banner, string? message, bool isError = false) =>
        UiComponents.SetPageStatus(banner.Banner, banner.Label, message, isError);

    protected static StackPanel PageRoot((Border Banner, TextBlock Label) banner, params UIElement[] sections)
    {
        var root = new StackPanel();
        root.Children.Add(banner.Banner);
        foreach (var section in sections)
        {
            root.Children.Add(section);
        }

        return root;
    }

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
            BorderBrush = ResourceBrush("ClinicLineBrush", Color.FromRgb(226, 232, 234)),
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

    protected static DataGrid TableGrid(object items, params ColumnSpec[] columns)
        => TableGrid(items, allowMultiSelect: false, columns);

    protected static DataGrid TableGrid(object items, bool allowMultiSelect, params (string Header, string Binding)[] columns)
        => TableGrid(items, allowMultiSelect, columns.Select(c => new ColumnSpec(c.Header, c.Binding)).ToArray());

    protected static DataGrid TableGrid(object items, bool allowMultiSelect, params ColumnSpec[] columns)
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
            var col = new DataGridTextColumn
            {
                Header = column.Header,
                Binding = new Binding(column.Binding),
                Width = column.Width is { } w
                    ? new DataGridLength(w)
                    : new DataGridLength(1, DataGridLengthUnitType.Star),
                IsReadOnly = true
            };
            if (column.Align == ColumnAlign.Right)
            {
                col.ElementStyle = new Style(typeof(TextBlock));
                col.ElementStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            }

            grid.Columns.Add(col);
        }

        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        return grid;
    }

    protected static void ApplyStockRowStyle(DataGridRow row, StockStatusKind kind)
    {
        if (kind is StockStatusKind.OutOfStock)
        {
            row.Background = ResourceBrush("ClinicRowDangerBrush", Color.FromRgb(252, 235, 233));
            row.BorderBrush = ResourceBrush("ClinicDangerBrush", Color.FromRgb(192, 57, 43));
            row.BorderThickness = new Thickness(4, 0, 0, 0);
        }
        else if (kind is StockStatusKind.Reorder or StockStatusKind.Expiring)
        {
            row.Background = ResourceBrush("ClinicRowWarnBrush", Color.FromRgb(255, 244, 224));
        }
    }

    protected static WrapPanel BuildFilterChips(string activeFilter, Action<string> setFilter, params (string Id, string Label)[] chips)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (id, label) in chips)
        {
            panel.Children.Add(UiComponents.FilterChip(label, activeFilter == id, (_, _) => setFilter(id)));
        }

        return panel;
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
        var pageBanner = CreatePageBanner();

        var panel = new StackPanel();
        panel.Children.Add(Note("상단 그래프는 전체 출고 추이입니다. 아래에서 품목을 선택하면 품목별 작은 그래프로 비교합니다. 예측은 참고용이며 자동 발주하지 않습니다."));

        var cards = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        cards.Children.Add(KpiCard("발주 필요", kpi.ReorderItems.ToString("N0"), "재고 ≤ 최소", warn: kpi.ReorderItems > 0, open: "stock"));
        cards.Children.Add(KpiCard("품절", kpi.OutOfStockItems.ToString("N0"), "현재고 0", danger: kpi.OutOfStockItems > 0, open: "stock"));
        cards.Children.Add(KpiCard("유효기간 임박", kpi.ExpiringLots.ToString("N0"), "경고일 이내 LOT", warn: kpi.ExpiringLots > 0, open: "stock"));
        cards.Children.Add(KpiCard("오늘 입·출고", $"{kpi.TodayReceiptDocs + kpi.TodayIssueDocs:N0}", $"입고 {kpi.TodayReceiptDocs} · 출고 {kpi.TodayIssueDocs}", open: "stats"));
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
        var heroHost = new StackPanel();
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
                Text = $"품목 「선택」을 켜면 아래에 품목별 작은 그래프가 나옵니다. 한 번에 최대 {UiLayout.ChartItemMax}개까지 비교합니다.",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap
            });
        }

        void ShowHeroChart()
        {
            using var inner = AppHost.OpenDb();
            var monthly = DashboardMetrics.TrailingMonthlyIssues(inner, DateTime.Today);
            var line = DashboardChartBuilder.BuildAggregateLine(monthly);
            var historyLabels = monthly.Select(s => $"{s.Year}-{s.Month:00}").ToArray();
            var heroLabels = DashboardChartBuilder.HeroLabels(historyLabels);
            var color = new SKColor(31, 111, 115);
            var gridPaint = new SolidColorPaint(new SKColor(230, 233, 236)) { StrokeThickness = 1 };
            var actualValues = DashboardChartBuilder.ActualWithGap(line);
            var forecastValues = DashboardChartBuilder.ForecastWithAnchor(line);
            var (nextQty, deltaPct, hasForecast) = DashboardChartBuilder.NextMonthOutlook(line);
            Brush deltaBrush = !hasForecast
                ? Brushes.DimGray
                : deltaPct > 0.5
                    ? new SolidColorBrush(Color.FromRgb(46, 125, 79))
                    : deltaPct < -0.5
                        ? new SolidColorBrush(Color.FromRgb(192, 57, 43))
                        : Brushes.DimGray;
            var rangeText = historyLabels.Length == 0
                ? "실적 데이터 없음"
                : hasForecast
                    ? $"{historyLabels[0]} — {heroLabels[^1]} (실적 + 예측)"
                    : $"{historyLabels[0]} — {historyLabels[^1]}";

            heroHost.Children.Clear();
            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleStack = new StackPanel();
            titleStack.Children.Add(new TextBlock
            {
                Text = "전체 월별 출고와 다음 3개월 예측",
                FontSize = 12,
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = rangeText,
                FontSize = 11,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 2, 0, 0)
            });
            header.Children.Add(titleStack);
            var badgeStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            badgeStack.Children.Add(new TextBlock
            {
                Text = "다음달 예상 출고",
                FontSize = 11,
                Foreground = Brushes.DimGray,
                HorizontalAlignment = HorizontalAlignment.Right
            });
            badgeStack.Children.Add(new TextBlock
            {
                Text = hasForecast ? nextQty.ToString("N0") : "—",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = ResourceBrush("ClinicAccentBrush", Color.FromRgb(31, 111, 115)),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 2, 0, 0)
            });
            badgeStack.Children.Add(new TextBlock
            {
                Text = hasForecast && historyLabels.Length > 0 && line.Actual[^1] > 0
                    ? deltaPct > 0.5
                        ? $"▲ {Math.Abs(deltaPct):N0}% vs 이번달"
                        : deltaPct < -0.5
                            ? $"▼ {Math.Abs(deltaPct):N0}% vs 이번달"
                            : "− vs 이번달"
                    : hasForecast ? "예측 참고" : "데이터 부족",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = deltaBrush,
                HorizontalAlignment = HorizontalAlignment.Right
            });
            Grid.SetColumn(badgeStack, 1);
            header.Children.Add(badgeStack);
            heroHost.Children.Add(header);

            var heroSeries = new List<ISeries>
            {
                new LineSeries<double>
                {
                    Name = "실적 (출고)",
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
                    Name = "3개월 예측",
                    Values = forecastValues,
                    Fill = null,
                    GeometrySize = 6,
                    LineSmoothness = 0,
                    GeometryFill = new SolidColorPaint(new SKColor(255, 255, 255)),
                    GeometryStroke = new SolidColorPaint(color, 1.5f),
                    Stroke = new SolidColorPaint(color, 2)
                    {
                        PathEffect = new LiveChartsCore.SkiaSharpView.Painting.Effects.DashEffect([6, 4])
                    },
                    YToolTipLabelFormatter = point => $"{point.Coordinate.PrimaryValue:N0}개(예측)"
                }
            };
            heroHost.Children.Add(new CartesianChart
            {
                Height = 280,
                LegendPosition = LiveChartsCore.Measure.LegendPosition.Bottom,
                Series = heroSeries,
                XAxes = [new Axis { Labels = heroLabels, SeparatorsPaint = gridPaint, TextSize = 10, LabelsRotation = 0 }],
                YAxes = [new Axis { MinLimit = 0, SeparatorsPaint = gridPaint, TextSize = 10 }]
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
                new ColumnSpec("품목", nameof(ItemRow.품목)),
                new ColumnSpec("코드", nameof(ItemRow.코드)),
                new ColumnSpec("현재고", nameof(ItemRow.현재고), ColumnAlign.Right),
                new ColumnSpec("상태", nameof(ItemRow.상태), Width: 96, IsStatus: true));
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
                if (e.Row.Item is ItemRow row)
                {
                    ApplyStockRowStyle(e.Row, row.Kind);
                }
            };
            gridHost.Children.Clear();
            if (slice.Count == 0)
            {
                gridHost.Children.Add(UiComponents.EmptyState(
                    "표시할 품목이 없습니다",
                    filter == "all" && string.IsNullOrWhiteSpace(query.Text)
                        ? "품목을 등록하거나 검색 조건을 바꿔 보세요."
                        : "검색·필터 조건에 맞는 품목이 없습니다. 초기화를 눌러 전체를 볼 수 있습니다."));
            }
            else
            {
                gridHost.Children.Add(next);
            }
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
            if (!all.Any(r => r.선택))
            {
                suppressSelect = true;
                var autoPick = all
                    .Where(r => r.Kind is StockStatusKind.Reorder or StockStatusKind.OutOfStock)
                    .Take(UiLayout.ChartItemMax)
                    .ToList();
                if (autoPick.Count == 0)
                {
                    autoPick = all.Take(Math.Min(3, UiLayout.ChartItemMax)).ToList();
                }

                foreach (var row in autoPick)
                {
                    row.선택 = true;
                }

                suppressSelect = false;
            }

            page = 1;
            Render();
            ShowHeroChart();
            ShowChart();
        }

        var chipsHost = new StackPanel();
        void RenderChips()
        {
            chipsHost.Children.Clear();
            chipsHost.Children.Add(BuildFilterChips(filter, id => { filter = id; Reload(); RenderChips(); },
                ("all", "전체"), ("out", "품절"), ("reorder", "발주"), ("unset", "미설정"), ("exp", "임박")));
        }

        var filters = new StackPanel();
        filters.Children.Add(FormRow(Field("품목", query)));
        var apply = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        apply.Children.Add(Primary("적용", (_, _) => Reload()));
        apply.Children.Add(Btn("초기화", (_, _) =>
        {
            query.Text = "";
            filter = "all";
            Reload();
            RenderChips();
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
        filters.Children.Add(chipsHost);

        var listBody = new StackPanel();
        listBody.Children.Add(gridHost);
        listBody.Children.Add(Pager(count, pageText, () => { page--; Render(); }, () => { page++; Render(); }));

        Content = PageRoot(pageBanner,
            Section("요약", panel),
            Section("전체 출고 추이 · 3개월 예측", heroHost),
            Section("품목 출고 추이", chartHost),
            Section("검색·필터", filters),
            Section("품목 목록", listBody));
        RenderChips();
        ShowHeroChart();
        Reload();
    }

    private Border KpiCard(string label, string value, string context, bool warn = false, bool danger = false, string? open = null)
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
            Foreground = danger || warn ? accent : ResourceBrush("ClinicTextPrimaryBrush", Color.FromRgb(32, 42, 54))
        });
        stack.Children.Add(new TextBlock
        {
            Text = context,
            FontSize = 11,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 2, 0, 0)
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
        var pageBanner = CreatePageBanner();
        var panel = new StackPanel();
        panel.Children.Add(Note("복원 전에 현재 DB를 .pre-restore 로 복사합니다. Excel 가져오기는 마스터만 / 마스터+기초 / 전체이력입니다. 빈 수식 행은 거래가 아닙니다. 기초와 이력을 같이 넣으면 이중계산 경고 후 기초는 건너뜁니다."));
        panel.Children.Add(Primary("지금 백업", (_, _) =>
        {
            var dlg = new SaveFileDialog { Filter = "SQLite (*.db)|*.db", FileName = $"inventory-{DateTime.Today:yyyyMMdd}.db" };
            if (dlg.ShowDialog() == true)
            {
                BackupService.Backup(AppHost.DatabasePath, dlg.FileName);
                SetBanner(pageBanner, "백업했습니다.");
                using (var db = AppHost.OpenDb())
                {
                    new SettingsStore(db).Set(SettingsStore.LastBackupDate, DateTime.Today.ToString("yyyy-MM-dd"));
                }

                if (Window.GetWindow(this) is MainWindow main)
                {
                    main.OpenMenu("backup", force: true);
                }
            }
        }));
        panel.Children.Add(Danger("복원", (_, _) =>
        {
            if (MessageBox.Show(
                    "선택한 백업 파일로 DB를 덮어씁니다. 현재 DB는 .pre-restore 로 보관됩니다.\n계속할까요?",
                    ProductInfo.DisplayName,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            var dlg = new OpenFileDialog { Filter = "SQLite (*.db)|*.db" };
            if (dlg.ShowDialog() == true)
            {
                BackupService.Restore(dlg.FileName, AppHost.DatabasePath);
                SetBanner(pageBanner, "복원했습니다. 프로그램을 다시 시작하세요.");
            }
        }));
        panel.Children.Add(Btn("Excel 내보내기", (_, _) =>
        {
            var dlg = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "재고내보내기.xlsx" };
            if (dlg.ShowDialog() == true)
            {
                using var db = AppHost.OpenDb();
                ExcelCatalog.ExportStock(db, dlg.FileName);
                SetBanner(pageBanner, "내보냈습니다.");
            }
        }));
        panel.Children.Add(Btn("Excel 가져오기(마스터만)", (_, _) => Import(ImportMode.MasterOnly, pageBanner)));
        panel.Children.Add(Btn("Excel 가져오기(마스터+기초)", (_, _) => Import(ImportMode.MasterAndOpening, pageBanner)));
        panel.Children.Add(Danger("Excel 가져오기(전체이력)", (_, _) =>
        {
            if (MessageBox.Show(
                    "전체 이력 가져오기는 기존 거래와 중복될 수 있습니다. 계속할까요?",
                    ProductInfo.DisplayName,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            Import(ImportMode.FullHistory, pageBanner);
        }));
        Content = PageRoot(pageBanner, Section("백업·엑셀", panel));
    }

    private static void Import(ImportMode mode, (Border Banner, TextBlock Label) pageBanner)
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

        try
        {
            using var db = AppHost.OpenDb();
            var result = ExcelCatalog.Import(db, dlg.FileName, mode);
            var text = $"가져옴: 품목 {result.ImportedItems}, 거래 {result.TransactionRows}, 기초확정 {result.OpeningConfirmed}";
            if (result.DoubleCountWarning)
            {
                text += "\n" + result.Warning;
            }

            SetBanner(pageBanner, text, result.DoubleCountWarning);
        }
        catch (Exception ex)
        {
            SetBanner(pageBanner, $"원인: {AppLog.Sanitize(ex.Message)}", isError: true);
        }
    }
}

public sealed class SettingsView : WorkspaceView
{
    public SettingsView()
    {
        using var db = AppHost.OpenDb();
        var store = new SettingsStore(db);
        var pageBanner = CreatePageBanner();
        var days = Box(store.Get(SettingsStore.ExpiryWarningDays, "90"));
        var backup = Box(store.Get(SettingsStore.BackupFolder, global::System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpringClinicInventory",
            "backups")));
        backup.MaxWidth = 560;
        var fontScale = new ComboBox { Width = 180 };
        fontScale.Items.Add("보통");
        fontScale.Items.Add("크게");
        fontScale.Items.Add("아주 크게");
        fontScale.SelectedIndex = store.Get(SettingsStore.FontScale, "normal") switch
        {
            "large" => 1,
            "xlarge" => 2,
            _ => 0
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = $"앱 버전 {ProductInfo.DisplayName} {ProductInfo.Version}", Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(Field("유효기간 경고일", days));
        panel.Children.Add(Field("백업 폴더", backup));
        panel.Children.Add(Field("글자 크기", fontScale));
        panel.Children.Add(Primary("저장", (_, _) =>
        {
            using var inner = AppHost.OpenDb();
            var s = new SettingsStore(inner);
            s.Set(SettingsStore.ExpiryWarningDays, days.Text.Trim());
            s.Set(SettingsStore.BackupFolder, backup.Text.Trim());
            s.Set(SettingsStore.FontScale, fontScale.SelectedIndex switch
            {
                1 => "large",
                2 => "xlarge",
                _ => "normal"
            });
            SetBanner(pageBanner, "저장했습니다. 글자 크기는 다음 화면 이동 후 반영됩니다.");
            MainWindow.ApplyFontScaleFromSettings();
        }));

        var devBody = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (AppSession.Current?.Role == UserRole.Administrator)
        {
            devBody.Children.Add(Note($"테스트 데이터는 개발·예측 확인용입니다. 운영 의원 DB에서는 만들지 마세요. 다시 만들면 기존 품목·입고·출고를 모두 지우고 품목 {DemoSeedService.DefaultItemCount:N0}개로 교체합니다."));
            devBody.Children.Add(Btn($"테스트 데이터 다시 만들기 (품목 {DemoSeedService.DefaultItemCount:N0})", (_, _) =>
            {
                if (MessageBox.Show(
                        $"기존 품목·입고·출고를 모두 삭제한 뒤 품목 {DemoSeedService.DefaultItemCount:N0}개와 1년치 계절 거래를 새로 만듭니다.\n운영 데이터면 '아니오'를 누르세요.",
                        ProductInfo.DisplayName,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }

                SetBanner(pageBanner, AppHost.Run((inner, _) =>
                    DemoSeedService.ReplaceSample(inner, DateTime.Today, AppHost.Actor).Message));
                if (Window.GetWindow(this) is MainWindow main)
                {
                    main.ShowDashboard();
                }
            }));
            devBody.Children.Add(Btn("테스트 데이터 생성 (거래 없을 때)", (_, _) =>
            {
                SetBanner(pageBanner, AppHost.Run((inner, _) =>
                    DemoSeedService.TryAutoSeed(inner, DateTime.Today, AppHost.Actor).Message));
            }));
        }
        else
        {
            devBody.Children.Add(Note("개발자 도구는 관리자만 사용할 수 있습니다."));
        }

        var devExpander = new Expander
        {
            Header = "개발자",
            IsExpanded = false,
            Margin = new Thickness(0, 12, 0, 0),
            Content = devBody
        };
        panel.Children.Add(devExpander);

        var resetMode = DataResetMode.Empty;
        var resetBody = new StackPanel();
        resetBody.Children.Add(Note(
            "품목·입고·출고·재고·월마감 데이터가 모두 삭제됩니다. 사용자 계정과 환경설정은 유지됩니다. 되돌릴 수 없으니 먼저 「백업」 메뉴에서 백업하세요."));
        var resetEmpty = new RadioButton
        {
            Content = "완전 초기화 — 빈 DB(스키마 유지)",
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var resetSample = new RadioButton
        {
            Content = $"샘플 데이터로 재설정 — 품목 {DemoSeedService.DefaultItemCount:N0}개·1년치 거래(데모·교육용)"
        };
        resetEmpty.Checked += (_, _) => resetMode = DataResetMode.Empty;
        resetSample.Checked += (_, _) => resetMode = DataResetMode.SampleSeed;
        resetBody.Children.Add(resetEmpty);
        resetBody.Children.Add(resetSample);
        if (AppSession.Current?.Role == UserRole.Administrator)
        {
            resetBody.Children.Add(Danger("데이터 초기화…", (_, _) =>
            {
                if (Window.GetWindow(this) is not Window owner)
                {
                    return;
                }

                var dlg = new DataResetConfirmDialog(owner, resetMode);
                if (dlg.ShowDialog() != true)
                {
                    return;
                }

                var mode = dlg.SelectedMode;
                SetBanner(pageBanner, AppHost.Run((inner, _) =>
                    DataResetService.Reset(inner, mode, DateTime.Today, AppHost.Actor).Message));
                if (Window.GetWindow(this) is MainWindow main)
                {
                    main.ShowDashboard();
                }
            }));
        }
        else
        {
            resetBody.Children.Add(Note("데이터 초기화는 관리자만 실행할 수 있습니다."));
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
            SetBanner(pageBanner, "업데이트를 준비하는 중...");
            try
            {
                var progress = new Progress<string>(text =>
                {
                    SetBanner(pageBanner, text);
                    var digits = System.Text.RegularExpressions.Regex.Match(text ?? "", @"(\d+)\s*%");
                    if (digits.Success && int.TryParse(digits.Groups[1].Value, out var pct))
                    {
                        progressBar.Value = Math.Clamp(pct, 0, 100);
                    }
                });
                SetBanner(pageBanner, await VelopackUpdater.ApplyFromButtonAsync(progress, applyAndRestart: true));
            }
            catch (Exception ex)
            {
                SetBanner(pageBanner, $"원인: {AppLog.Sanitize(ex.Message)}\n조치: 입고·출고는 계속하세요. {UpdateChecker.ReleasesUrl}", isError: true);
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

        // 사용 알림(하트비트)은 설정 UI에 노출하지 않음.
        // MainWindow 시작 시 UsageHeartbeatService + UsageNotifyDefaults로 자동 발송.

        Content = PageRoot(pageBanner,
            Section("환경설정", panel),
            Section("데이터 초기화", resetBody));
    }
}

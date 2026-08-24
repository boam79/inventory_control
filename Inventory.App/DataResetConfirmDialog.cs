using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Inventory.Infrastructure;

namespace Inventory.App;

internal sealed class DataResetConfirmDialog : Window
{
    public DataResetMode SelectedMode { get; private set; } = DataResetMode.Empty;

    public DataResetConfirmDialog(Window owner, DataResetMode initialMode)
    {
        Owner = owner;
        Title = "데이터 초기화 확인";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.White;

        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };

        root.Children.Add(new TextBlock
        {
            Text =
                "경고: 품목·입고·출고·재고·월마감 등 재고 업무 데이터가 모두 삭제됩니다.\n" +
                "사용자 계정과 환경설정(글자 크기, 백업 폴더 등)은 유지됩니다.\n" +
                "되돌릴 수 없으니 먼저 백업을 권장합니다.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 20, 24, 12),
            Foreground = Application.Current?.TryFindResource("ClinicDangerBrush") as Brush
                         ?? new SolidColorBrush(Color.FromRgb(192, 57, 43)),
            FontSize = 14,
            LineHeight = 22,
            FontWeight = FontWeights.SemiBold
        });

        var modePanel = new StackPanel { Margin = new Thickness(24, 0, 24, 12) };
        modePanel.Children.Add(new TextBlock
        {
            Text = "초기화 방식",
            FontSize = 12,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var emptyMode = new RadioButton
        {
            Content = "완전 초기화 — 빈 DB(스키마 유지)",
            IsChecked = initialMode == DataResetMode.Empty,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var sampleMode = new RadioButton
        {
            Content = $"샘플 데이터로 재설정 — 품목 {DemoSeedService.DefaultItemCount:N0}개·1년치 거래",
            IsChecked = initialMode == DataResetMode.SampleSeed
        };
        modePanel.Children.Add(emptyMode);
        modePanel.Children.Add(sampleMode);
        root.Children.Add(modePanel);

        var understand = new CheckBox
        {
            Content = "데이터가 모두 삭제됨을 이해합니다",
            Margin = new Thickness(24, 0, 24, 8)
        };
        root.Children.Add(understand);

        var phraseLabel = new TextBlock
        {
            Text = $"아래에 「{DataResetService.ConfirmPhrase}」를 입력하세요",
            Margin = new Thickness(24, 0, 24, 4),
            FontSize = 12,
            Foreground = Brushes.DimGray
        };
        root.Children.Add(phraseLabel);

        var phraseBox = new TextBox
        {
            Margin = new Thickness(24, 0, 24, 16),
            MinWidth = 200,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        root.Children.Add(phraseBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(24, 0, 24, 0)
        };

        var cancel = new Button
        {
            Content = "취소",
            MinWidth = 96,
            Margin = new Thickness(0, 0, 8, 0),
            Style = Application.Current?.TryFindResource("GhostButton") as Style
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };

        var confirm = new Button
        {
            Content = "데이터 초기화 실행",
            MinWidth = 140,
            IsEnabled = false,
            Style = Application.Current?.TryFindResource("DangerButton") as Style
        };

        void RefreshConfirm()
        {
            confirm.IsEnabled = understand.IsChecked == true
                                && string.Equals(phraseBox.Text.Trim(), DataResetService.ConfirmPhrase, StringComparison.Ordinal);
        }

        understand.Checked += (_, _) => RefreshConfirm();
        understand.Unchecked += (_, _) => RefreshConfirm();
        phraseBox.TextChanged += (_, _) => RefreshConfirm();

        confirm.Click += (_, _) =>
        {
            SelectedMode = sampleMode.IsChecked == true ? DataResetMode.SampleSeed : DataResetMode.Empty;
            DialogResult = true;
            Close();
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        root.Children.Add(buttons);
        Content = root;
    }
}
